namespace Remotely.Shared.Models;

/// <summary>Pedido de eventos do Windows (ou do journal no Linux) para a aba Eventos do dispositivo.</summary>
public class DeviceEventQuery
{
    /// <summary>System, Application, Security ou Setup (no Linux: todos vêm do journal).</summary>
    public string LogName { get; set; } = "System";

    /// <summary>1 = crítico, 2 = erro, 3 = aviso, 4 = informação.</summary>
    public List<int> Levels { get; set; } = [1, 2, 3];

    /// <summary>Período em horas (1 a 720).</summary>
    public int Hours { get; set; } = 24;

    /// <summary>Máximo de eventos devolvidos (até 500).</summary>
    public int MaxEvents { get; set; } = 200;

    /// <summary>Texto, origem ou ID do evento.</summary>
    public string? Search { get; set; }

    public static readonly string[] LogNames = ["System", "Application", "Security", "Setup"];

    /// <summary>Corrige valores fora da faixa vindos do navegador.</summary>
    public DeviceEventQuery Normalize()
    {
        return new DeviceEventQuery
        {
            LogName = LogNames.FirstOrDefault(x => x.Equals(LogName, StringComparison.OrdinalIgnoreCase)) ?? "System",
            Levels = (Levels ?? []).Where(x => x is >= 1 and <= 4).Distinct().OrderBy(x => x).ToList() is { Count: > 0 } levels ? levels : [1, 2, 3],
            Hours = Math.Clamp(Hours, 1, 720),
            MaxEvents = Math.Clamp(MaxEvents, 10, 500),
            Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim()[..Math.Min(Search.Trim().Length, 200)]
        };
    }

    /// <summary>O evento passa no filtro de texto (mensagem, origem ou ID)?</summary>
    public bool Matches(DeviceEventEntry entry)
    {
        if (string.IsNullOrWhiteSpace(Search))
        {
            return true;
        }

        return entry.Message.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
            entry.Source.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
            entry.EventId.ToString() == Search;
    }
}

public class DeviceEventEntry
{
    public DateTimeOffset TimeCreated { get; set; }

    /// <summary>1 = crítico, 2 = erro, 3 = aviso, 4 = informação.</summary>
    public int Level { get; set; }

    public string Source { get; set; } = string.Empty;
    public int EventId { get; set; }
    public string Message { get; set; } = string.Empty;

    /// <summary>Log de segurança: "Sucesso" ou "Falha" de auditoria.</summary>
    public string? Audit { get; set; }

    public const int MaxMessageLength = 2000;

    public static string Trim(string? message)
    {
        var text = (message ?? string.Empty).Trim();
        return text.Length <= MaxMessageLength ? text : text[..MaxMessageLength] + "…";
    }
}

public class DeviceEventResult
{
    public List<DeviceEventEntry> Entries { get; set; } = [];

    /// <summary>Mais eventos do que o máximo pedido: a lista foi cortada.</summary>
    public bool Truncated { get; set; }

    public string? Error { get; set; }
}
