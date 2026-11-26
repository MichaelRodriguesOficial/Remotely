using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Remotely.Shared.Models.Diagnostics;

/// <summary>Dados de uma bateria juntados do relatório do powercfg, do WMI ou do /sys (Linux).</summary>
public sealed class BatteryHealthData
{
    public string Group { get; set; } = "Bateria";
    public string? Manufacturer { get; set; }
    public string? Chemistry { get; set; }
    public string? ManufactureDate { get; set; }

    /// <summary>Capacidades em mWh, ou em % quando <see cref="RelativeUnits"/> (alguns notebooks não informam mWh).</summary>
    public long? DesignCapacity { get; set; }
    public long? FullChargeCapacity { get; set; }
    public bool RelativeUnits { get; set; }

    /// <summary>Unidade mostrada ao lado das capacidades. Padrão: mWh (ou % quando relativo).</summary>
    public string? CapacityUnit { get; set; }
    public long? CycleCount { get; set; }

    public int? ChargePercent { get; set; }
    /// <summary>"carregando", "na tomada" ou "na bateria".</summary>
    public string? PowerState { get; set; }

    /// <summary>Autonomia estimada pelo Windows com a carga cheia atual e com a capacidade de fábrica.</summary>
    public TimeSpan? RuntimeAtFullCharge { get; set; }
    public TimeSpan? RuntimeAtDesign { get; set; }

    /// <summary>Histórico da capacidade (data, carga cheia, capacidade de fábrica), do mais antigo ao mais novo.</summary>
    public List<BatteryCapacitySample> History { get; } = new();

    public List<string> Sources { get; } = new();

    public double? HealthPercent =>
        DesignCapacity is > 0 && FullChargeCapacity is > 0
            ? 100.0 * FullChargeCapacity.Value / DesignCapacity.Value
            : null;
}

public sealed record BatteryCapacitySample(DateTime Date, long FullChargeCapacity, long DesignCapacity)
{
    public double HealthPercent => DesignCapacity > 0 ? 100.0 * FullChargeCapacity / DesignCapacity : 0;
}

/// <summary>Leitura e avaliação da bateria (funções puras, testáveis sem o notebook).</summary>
public static class BatteryHealth
{
    public const string VerdictLabel = "Veredito";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Lê o XML de "powercfg /batteryreport /xml". Retorna uma entrada por bateria.</summary>
    public static List<BatteryHealthData> ParsePowercfgXml(string xml)
    {
        var result = new List<BatteryHealthData>();
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml, LoadOptions.None);
        }
        catch (XmlException)
        {
            return result;
        }

        var root = doc.Root;
        if (root is null)
        {
            return result;
        }

        var batteries = Descendants(root, "Batteries").SelectMany(x => Children(x, "Battery")).ToList();
        for (var i = 0; i < batteries.Count; i++)
        {
            var battery = batteries[i];
            var data = new BatteryHealthData
            {
                Group = Value(battery, "Id") is { Length: > 0 } id ? id : $"Bateria {i + 1}",
                Manufacturer = Value(battery, "Manufacturer"),
                Chemistry = ChemistryName(Value(battery, "Chemistry")),
                ManufactureDate = Value(battery, "ManufactureDate"),
                DesignCapacity = Long(Value(battery, "DesignCapacity")),
                FullChargeCapacity = Long(Value(battery, "FullChargeCapacity")),
                CycleCount = Long(Value(battery, "CycleCount")),
                RelativeUnits = Value(battery, "RelativeCapacity") is "1" or "true"
            };
            data.Sources.Add("relatório de bateria do Windows");
            result.Add(data);
        }

        if (result.Count == 0)
        {
            return result;
        }

        // Autonomia e histórico são do conjunto de baterias; ficam na primeira (quase sempre a única).
        var first = result[0];
        if (Descendants(root, "RuntimeEstimates").FirstOrDefault() is { } runtime)
        {
            first.RuntimeAtFullCharge = Duration(Children(runtime, "FullChargeCapacity").FirstOrDefault(), "ActiveRuntime");
            first.RuntimeAtDesign = Duration(Children(runtime, "DesignCapacity").FirstOrDefault(), "ActiveRuntime");
        }

        foreach (var entry in Descendants(root, "History").SelectMany(x => Children(x, "HistoryEntry")))
        {
            var date = Date(Attr(entry, "LocalStartDate") ?? Attr(entry, "StartDate") ?? Value(entry, "LocalStartDate"));
            var full = Long(Attr(entry, "FullChargeCapacity") ?? Value(entry, "FullChargeCapacity"));
            var design = Long(Attr(entry, "DesignCapacity") ?? Value(entry, "DesignCapacity"));
            if (date is not null && full is > 0 && design is > 0)
            {
                first.History.Add(new BatteryCapacitySample(date.Value, full.Value, design.Value));
            }
        }
        first.History.Sort((a, b) => a.Date.CompareTo(b.Date));

        return result;
    }

    /// <summary>Monta o veredito (Bom / Atenção / Ruim) e os itens da bateria.</summary>
    public static List<DiagnosticItem> Evaluate(BatteryHealthData b)
    {
        var items = new List<DiagnosticItem>();
        var bad = new List<string>();
        var caution = new List<string>();
        var unit = b.CapacityUnit ?? (b.RelativeUnits ? "%" : " mWh");

        DiagnosticItem Add(string label, string value, DiagnosticStatus status = DiagnosticStatus.Info, string? hint = null)
        {
            var item = new DiagnosticItem { Group = b.Group, Label = label, Value = value, Status = status, Hint = hint };
            items.Add(item);
            return item;
        }

        var health = b.HealthPercent;
        if (health is { } h)
        {
            // Bateria nova pode passar de 100%: mostra 100 e explica.
            var shown = Math.Min(100, h);
            var status = h < 60 ? DiagnosticStatus.Error : h < 80 ? DiagnosticStatus.Warning : DiagnosticStatus.Ok;
            Add("Capacidade", $"{shown.ToString("0", PtBr)}% da original ({Format(b.FullChargeCapacity!.Value)} de {Format(b.DesignCapacity!.Value)}{unit})",
                status,
                status switch
                {
                    DiagnosticStatus.Error => "Segura menos de 60% da carga de fábrica: troque a bateria.",
                    DiagnosticStatus.Warning => "Já perdeu parte da capacidade: a autonomia está menor. Programe a troca.",
                    _ => null
                });
            if (status == DiagnosticStatus.Error) bad.Add($"só {shown.ToString("0", PtBr)}% da capacidade original");
            else if (status == DiagnosticStatus.Warning) caution.Add($"{shown.ToString("0", PtBr)}% da capacidade original");
        }

        if (b.CycleCount is > 0 and var cycles)
        {
            var status = cycles >= 1000 ? DiagnosticStatus.Warning : DiagnosticStatus.Info;
            Add("Ciclos de carga", cycles.ToString("N0", PtBr), status,
                status == DiagnosticStatus.Warning ? "Muitos ciclos: a maioria das baterias de notebook é feita para 500 a 1.000." : null);
            if (status == DiagnosticStatus.Warning) caution.Add($"{cycles.ToString("N0", PtBr)} ciclos");
        }

        // Ritmo de desgaste nos últimos ~6 meses do histórico.
        if (b.History.Count >= 2)
        {
            var latest = b.History[^1];
            var baseline = b.History.LastOrDefault(x => x.Date <= latest.Date.AddDays(-150)) ?? b.History[0];
            var days = (latest.Date - baseline.Date).TotalDays;
            if (days >= 30)
            {
                var lost = baseline.HealthPercent - latest.HealthPercent;
                var months = Math.Max(1, (int)Math.Round(days / 30));
                var status = lost >= 15 ? DiagnosticStatus.Warning : DiagnosticStatus.Info;
                var text = lost >= 0.5
                    ? $"perdeu {lost.ToString("0", PtBr)} pontos em {months} mês(es)"
                    : $"estável nos últimos {months} mês(es)";
                Add("Ritmo de desgaste", text, status,
                    status == DiagnosticStatus.Warning
                        ? "Desgaste rápido: calor, notebook sempre a 100% na tomada ou bateria com defeito."
                        : null);
                if (status == DiagnosticStatus.Warning) caution.Add("desgaste acelerado");
            }
        }

        if (b.RuntimeAtFullCharge is { } runtime && runtime > TimeSpan.Zero)
        {
            var design = b.RuntimeAtDesign is { } d && d > TimeSpan.Zero ? $" (nova: {FormatDuration(d)})" : "";
            Add("Autonomia estimada", $"{FormatDuration(runtime)} com carga cheia{design}", DiagnosticStatus.Info,
                "Estimativa do Windows com base no uso recente.");
        }

        if (b.ChargePercent is { } charge)
        {
            Add("Carga atual", $"{charge}%{(b.PowerState is null ? "" : $" ({b.PowerState})")}");
        }

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(b.Manufacturer)) details.Add(b.Manufacturer!);
        if (!string.IsNullOrWhiteSpace(b.Chemistry)) details.Add(b.Chemistry!);
        if (!string.IsNullOrWhiteSpace(b.ManufactureDate)) details.Add($"fabricada em {b.ManufactureDate}");
        if (details.Count > 0)
        {
            Add("Identificação", string.Join(" · ", details));
        }

        DiagnosticItem verdict;
        if (bad.Count > 0)
        {
            verdict = new DiagnosticItem
            {
                Group = b.Group, Label = VerdictLabel, Value = $"Ruim: {string.Join("; ", bad)}",
                Status = DiagnosticStatus.Error, Hint = "Troque a bateria. Bateria muito gasta também pode desligar o notebook de repente."
            };
        }
        else if (caution.Count > 0)
        {
            verdict = new DiagnosticItem
            {
                Group = b.Group, Label = VerdictLabel, Value = $"Atenção: {string.Join("; ", caution)}",
                Status = DiagnosticStatus.Warning, Hint = "Ainda funciona, mas a autonomia caiu. Planeje a troca."
            };
        }
        else if (health is not null)
        {
            verdict = new DiagnosticItem { Group = b.Group, Label = VerdictLabel, Value = "Bom", Status = DiagnosticStatus.Ok };
        }
        else
        {
            verdict = new DiagnosticItem
            {
                Group = b.Group, Label = VerdictLabel, Value = "Sem dados de capacidade", Status = DiagnosticStatus.Info,
                Hint = "O firmware do notebook não informou a capacidade de fábrica."
            };
        }

        if (b.Sources.Count > 0)
        {
            items.Add(new DiagnosticItem
            {
                Group = b.Group, Label = "Fonte dos dados", Value = string.Join(" + ", b.Sources.Distinct()), Status = DiagnosticStatus.Info
            });
        }
        items.Insert(0, verdict);
        return items;
    }

    public static string? Summarize(IEnumerable<DiagnosticItem> items)
    {
        var verdicts = items.Where(x => x.Label == VerdictLabel).ToList();
        if (verdicts.Count == 0)
        {
            return null;
        }
        if (verdicts.Count == 1)
        {
            return $"{verdicts[0].Value}.";
        }
        return $"{verdicts.Count} baterias: {string.Join(", ", verdicts.Select(x => (x.Value ?? "").Split(':')[0].Trim()))}.";
    }

    private static string Format(long value) => value.ToString("N0", PtBr);

    private static string FormatDuration(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours} h {span.Minutes:00} min" : $"{span.Minutes} min";

    private static string? ChemistryName(string? code) => code?.Trim().ToUpperInvariant() switch
    {
        null or "" => null,
        "LION" or "LI-I" or "LI" => "Íon de lítio",
        "LIP" or "LIPO" => "Polímero de lítio",
        "NIMH" => "Níquel-metal hidreto",
        "PBAC" => "Chumbo-ácido",
        var other => other
    };

    private static IEnumerable<XElement> Descendants(XElement root, string name) =>
        root.DescendantsAndSelf().Where(x => x.Name.LocalName == name);

    private static IEnumerable<XElement> Children(XElement? element, string name) =>
        element?.Elements().Where(x => x.Name.LocalName == name) ?? Enumerable.Empty<XElement>();

    private static string? Value(XElement element, string name) =>
        Children(element, name).FirstOrDefault()?.Value.Trim() is { Length: > 0 } value ? value : null;

    private static string? Attr(XElement element, string name) =>
        element.Attributes().FirstOrDefault(x => x.Name.LocalName == name)?.Value;

    private static long? Long(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static DateTime? Date(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date) ? date : null;

    private static TimeSpan? Duration(XElement? element, string name)
    {
        if (element is null || Value(element, name) is not { } text)
        {
            return null;
        }
        try
        {
            return XmlConvert.ToTimeSpan(text);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
