using Remotely.Shared.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.Json;

namespace Remotely.Agent.Services.Events;

/// <summary>Lê os eventos pedidos pela aba Eventos: log de eventos do Windows ou journal do Linux.</summary>
public static class DeviceEventReader
{
    /// <summary>Quantos eventos no máximo olhar quando há busca por texto (para não varrer o log inteiro).</summary>
    private const int MaxScanned = 20_000;

    public static DeviceEventResult Read(DeviceEventQuery query)
    {
        query = query.Normalize();
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return ReadWindows(query);
            }

            if (OperatingSystem.IsLinux())
            {
                return ReadJournal(query);
            }

            return new DeviceEventResult { Error = "Eventos disponíveis só no Windows e no Linux." };
        }
        catch (EventLogNotFoundException)
        {
            return new DeviceEventResult { Error = $"O log \"{query.LogName}\" não existe neste computador." };
        }
        catch (UnauthorizedAccessException)
        {
            return new DeviceEventResult { Error = "Sem permissão para ler este log." };
        }
        catch (Exception ex)
        {
            return new DeviceEventResult { Error = $"Não foi possível ler os eventos: {ex.Message}" };
        }
    }

    [SupportedOSPlatform("windows")]
    private static DeviceEventResult ReadWindows(DeviceEventQuery query)
    {
        var result = new DeviceEventResult();
        var isSecurity = query.LogName == "Security";
        var milliseconds = (long)query.Hours * 3_600_000;

        // No log de segurança os eventos não têm nível: o filtro vira sucesso/falha de auditoria.
        string xpath;
        if (isSecurity)
        {
            var failuresOnly = !query.Levels.Contains(4);
            xpath = failuresOnly
                ? $"*[System[band(Keywords,4503599627370496) and TimeCreated[timediff(@SystemTime) <= {milliseconds}]]]"
                : $"*[System[TimeCreated[timediff(@SystemTime) <= {milliseconds}]]]";
        }
        else
        {
            // Nível 0 ("Sempre registrar") conta como informação.
            var levels = query.Levels.SelectMany(x => x == 4 ? new[] { 0, 4 } : new[] { x }).Select(x => $"Level={x}");
            xpath = $"*[System[({string.Join(" or ", levels)}) and TimeCreated[timediff(@SystemTime) <= {milliseconds}]]]";
        }

        var eventQuery = new EventLogQuery(query.LogName, PathType.LogName, xpath) { ReverseDirection = true };
        using var reader = new EventLogReader(eventQuery);
        var scanned = 0;
        for (var record = reader.ReadEvent(); record is not null; record = reader.ReadEvent())
        {
            using (record)
            {
                if (++scanned > MaxScanned)
                {
                    result.Truncated = true;
                    break;
                }

                var entry = new DeviceEventEntry
                {
                    TimeCreated = record.TimeCreated is { } time ? new DateTimeOffset(time) : DateTimeOffset.MinValue,
                    Source = (record.ProviderName ?? string.Empty).Replace("Microsoft-Windows-", ""),
                    EventId = record.Id,
                    Level = record.Level switch
                    {
                        1 => 1,
                        2 => 2,
                        3 => 3,
                        _ => 4
                    }
                };

                if (isSecurity)
                {
                    var failure = ((record.Keywords ?? 0) & 4503599627370496L) != 0;
                    entry.Audit = failure ? "Falha" : "Sucesso";
                    entry.Level = failure ? 2 : 4;
                }

                string? message = null;
                try
                {
                    message = record.FormatDescription();
                }
                catch
                {
                    // Sem o arquivo de mensagens do provedor: mostra os dados brutos.
                }
                message ??= string.Join(" | ", record.Properties.Select(x => x.Value?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)));
                entry.Message = DeviceEventEntry.Trim(message);

                if (!query.Matches(entry))
                {
                    continue;
                }

                if (result.Entries.Count >= query.MaxEvents)
                {
                    result.Truncated = true;
                    break;
                }
                result.Entries.Add(entry);
            }
        }

        return result;
    }

    private static DeviceEventResult ReadJournal(DeviceEventQuery query)
    {
        // Prioridades do syslog: 0-2 crítico, 3 erro, 4 aviso, 5-7 informação.
        var maxPriority = query.Levels.Max() switch
        {
            1 => 2,
            2 => 3,
            3 => 4,
            _ => 7
        };

        var psi = new ProcessStartInfo("journalctl")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in new[] { "--no-pager", "-o", "json", "-r", "--since", $"-{query.Hours}h", "-p", $"0..{maxPriority}", "-n", MaxScanned.ToString() })
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi);
        if (process is null)
        {
            return new DeviceEventResult { Error = "journalctl não encontrado." };
        }

        var result = new DeviceEventResult();
        string? line;
        while ((line = process.StandardOutput.ReadLine()) is not null)
        {
            try
            {
                using var json = JsonDocument.Parse(line);
                var root = json.RootElement;
                var priority = int.TryParse(Text(root, "PRIORITY"), out var p) ? p : 6;
                var level = priority <= 2 ? 1 : priority == 3 ? 2 : priority == 4 ? 3 : 4;
                if (!query.Levels.Contains(level))
                {
                    continue;
                }

                var entry = new DeviceEventEntry
                {
                    Level = level,
                    Source = Text(root, "SYSLOG_IDENTIFIER") ?? Text(root, "_COMM") ?? "?",
                    Message = DeviceEventEntry.Trim(Text(root, "MESSAGE")),
                    TimeCreated = long.TryParse(Text(root, "__REALTIME_TIMESTAMP"), out var micro)
                        ? DateTimeOffset.FromUnixTimeMilliseconds(micro / 1000).ToLocalTime()
                        : DateTimeOffset.MinValue
                };

                if (!query.Matches(entry))
                {
                    continue;
                }

                if (result.Entries.Count >= query.MaxEvents)
                {
                    result.Truncated = true;
                    break;
                }
                result.Entries.Add(entry);
            }
            catch (JsonException)
            {
                // Linha que não é JSON (aviso do journalctl).
            }
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch
        {
            // Já terminou.
        }

        return result;
    }

    private static string? Text(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            // O journal manda mensagens com bytes inválidos como array de números.
            JsonValueKind.Array => System.Text.Encoding.UTF8.GetString(value.EnumerateArray().Select(x => x.TryGetByte(out var b) ? b : (byte)'?').ToArray()),
            _ => null
        };
    }
}
