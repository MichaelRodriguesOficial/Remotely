using Remotely.Shared.Models.Diagnostics;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Runtime.Versioning;
using System.Text;

namespace Remotely.Agent.Services.Diagnostics;

/// <summary>Funções comuns dos testes de diagnóstico.</summary>
internal static class DiagnosticHelpers
{
    public static DiagnosticItem Item(string label, string? value, DiagnosticStatus status = DiagnosticStatus.Info, string? hint = null, string? group = null) =>
        new() { Label = label, Value = value, Status = status, Hint = hint, Group = group };

    /// <summary>O pior status entre os itens (erro &gt; alerta &gt; ok).</summary>
    public static DiagnosticStatus Worst(IEnumerable<DiagnosticItem> items)
    {
        var list = items.ToList();
        if (list.Any(x => x.Status == DiagnosticStatus.Error))
        {
            return DiagnosticStatus.Error;
        }
        if (list.Any(x => x.Status == DiagnosticStatus.Warning))
        {
            return DiagnosticStatus.Warning;
        }
        return DiagnosticStatus.Ok;
    }

    public static string? ToText(object? value)
    {
        var text = value switch
        {
            null => null,
            string s => s,
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
        text = text?.Replace("\0", string.Empty).Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public static long? ToLong(object? value)
    {
        try
        {
            return value switch
            {
                null => null,
                string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
                string => null,
                IEnumerable and not string => null,
                _ => Convert.ToInt64(value, CultureInfo.InvariantCulture)
            };
        }
        catch
        {
            return null;
        }
    }

    public static bool? ToBool(object? value)
    {
        try
        {
            return value switch
            {
                null => null,
                bool b => b,
                string s when bool.TryParse(s, out var parsed) => parsed,
                string => null,
                _ => Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0
            };
        }
        catch
        {
            return null;
        }
    }

    public static string FormatDuration(TimeSpan span) =>
        span.TotalDays >= 1
            ? $"{(int)span.TotalDays} dia(s) e {span.Hours} h"
            : span.TotalHours >= 1
                ? $"{(int)span.TotalHours} h e {span.Minutes} min"
                : $"{span.Minutes} min";

    /// <summary>Executa um programa com tempo limite e devolve código de saída e saída.</summary>
    public static (int ExitCode, string Output) Run(string fileName, string arguments, int timeoutSeconds = 30)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = OperatingSystem.IsWindows() ? null : Encoding.UTF8
            };
            if (!OperatingSystem.IsWindows())
            {
                psi.Environment["LC_ALL"] = "C";
            }

            using var process = Process.Start(psi);
            if (process is null)
            {
                return (-1, string.Empty);
            }

            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeSpan.FromSeconds(timeoutSeconds)))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Já terminou.
                }
                return (-1, string.Empty);
            }

            return (process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
        }
        catch
        {
            return (-1, string.Empty);
        }
    }

    [SupportedOSPlatform("windows")]
    public static List<Dictionary<string, object?>> Wmi(string wql, string scope = @"root\cimv2")
    {
        var results = new List<Dictionary<string, object?>>();
        try
        {
            var options = new System.Management.EnumerationOptions
            {
                Timeout = TimeSpan.FromSeconds(30),
                ReturnImmediately = true,
                Rewindable = false
            };
            using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(wql), options);
            using var collection = searcher.Get();
            foreach (ManagementBaseObject item in collection)
            {
                using (item)
                {
                    var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (PropertyData property in item.Properties)
                    {
                        values[property.Name] = property.Value;
                    }
                    results.Add(values);
                }
            }
        }
        catch
        {
            // Classe ausente nesta versão do Windows ou sem permissão.
        }
        return results;
    }

    public static object? Get(Dictionary<string, object?>? values, string name) =>
        values is not null && values.TryGetValue(name, out var value) ? value : null;
}
