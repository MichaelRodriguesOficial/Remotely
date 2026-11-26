using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;

namespace Remotely.Shared.Models.Diagnostics;

/// <summary>Dados de saúde de um disco juntados de todas as fontes (Windows, SMART ATA, log NVMe, smartctl).</summary>
public sealed class DiskHealthData
{
    public string Group { get; set; } = "Disco";
    public bool IsHdd { get; set; }
    public bool IsNvme { get; set; }
    public bool IsExternal { get; set; }

    /// <summary>De onde vieram os números (ex.: "log SMART do NVMe").</summary>
    public List<string> Sources { get; } = new();

    /// <summary>HealthStatus do MSFT_PhysicalDisk: 0 saudável, 1 atenção, 2 com defeito.</summary>
    public int? WindowsHealth { get; set; }
    public bool PredictFailure { get; set; }

    public int? TemperatureC { get; set; }
    public int? TemperatureLimitC { get; set; }
    public long? MinutesAboveWarningTemp { get; set; }
    public long? MinutesAboveCriticalTemp { get; set; }

    /// <summary>Porcentagem da vida útil usada (SSD). Pode passar de 100.</summary>
    public int? PercentUsed { get; set; }
    public int? AvailableSpare { get; set; }
    public int? AvailableSpareThreshold { get; set; }
    public int? NvmeCriticalWarning { get; set; }

    public long? PowerOnHours { get; set; }
    public long? PowerCycles { get; set; }
    public long? UnsafeShutdowns { get; set; }
    public double? TerabytesWritten { get; set; }
    public double? TerabytesRead { get; set; }

    public long? MediaErrors { get; set; }
    public long? ReadErrorsUncorrected { get; set; }
    public long? WriteErrorsUncorrected { get; set; }
    public long? ReadWriteErrorsCorrected { get; set; }

    public long? ReallocatedSectors { get; set; }
    public long? ReallocationEvents { get; set; }
    public long? PendingSectors { get; set; }
    public long? OfflineUncorrectable { get; set; }
    public long? ReportedUncorrectable { get; set; }
    public long? CommandTimeouts { get; set; }
    public long? SpinRetries { get; set; }
    public long? CrcErrors { get; set; }

    /// <summary>Atributos SMART que chegaram ao limite do fabricante (falha prevista).</summary>
    public List<string> FailingAttributes { get; } = new();

    public bool HasSmartDetail =>
        PercentUsed is not null || ReallocatedSectors is not null || PendingSectors is not null ||
        MediaErrors is not null || NvmeCriticalWarning is not null || FailingAttributes.Count > 0;
}

/// <summary>Atributo SMART de disco ATA/SATA.</summary>
public sealed record AtaSmartAttribute(int Id, int Current, int Worst, int Threshold, long Raw)
{
    public bool IsFailing => Threshold > 0 && Current > 0 && Current <= Threshold;
}

/// <summary>
/// Leitura e avaliação de SMART. As funções são puras (recebem os bytes) para poderem ser testadas
/// sem o hardware; o agente só busca os bytes no disco.
/// </summary>
public static class DiskHealth
{
    public const string VerdictLabel = "Veredito";

    private static readonly Dictionary<int, string> AtaNames = new()
    {
        [1] = "Taxa de erros de leitura",
        [3] = "Tempo para girar",
        [5] = "Setores realocados",
        [7] = "Erros de posicionamento",
        [9] = "Horas ligado",
        [10] = "Tentativas de girar",
        [12] = "Ciclos liga/desliga",
        [177] = "Desgaste (Wear Leveling)",
        [184] = "Erros ponta a ponta",
        [187] = "Erros incorrigíveis reportados",
        [188] = "Comandos expirados",
        [190] = "Temperatura do ar",
        [194] = "Temperatura",
        [196] = "Eventos de realocação",
        [197] = "Setores pendentes",
        [198] = "Setores incorrigíveis",
        [199] = "Erros de CRC (cabo)",
        [202] = "Vida útil restante",
        [231] = "Vida útil restante",
        [232] = "Reserva disponível",
        [233] = "Indicador de desgaste",
        [241] = "Total gravado",
        [242] = "Total lido",
    };

    /// <summary>Nome em português do atributo SMART.</summary>
    public static string AttributeName(int id) =>
        AtaNames.TryGetValue(id, out var name) ? $"{name} ({id})" : $"Atributo {id}";

    // -----------------------------------------------------------------
    // NVMe
    // -----------------------------------------------------------------

    /// <summary>
    /// Lê o log "SMART / Health Information" do NVMe (página 02h, 512 bytes, especificação NVMe 1.4 §5.14.1.2).
    /// </summary>
    public static bool TryParseNvmeHealthLog(ReadOnlySpan<byte> log, DiskHealthData data)
    {
        if (log.Length < 200)
        {
            return false;
        }

        data.NvmeCriticalWarning = log[0];

        var kelvin = BinaryPrimitives.ReadUInt16LittleEndian(log.Slice(1, 2));
        if (kelvin is > 200 and < 500)
        {
            data.TemperatureC = kelvin - 273;
        }

        data.AvailableSpare = log[3];
        data.AvailableSpareThreshold = log[4];
        data.PercentUsed = log[5];

        // "Data units" = 1000 blocos de 512 bytes.
        data.TerabytesRead = ReadUInt128(log.Slice(32, 16)) * 512_000d / 1e12;
        data.TerabytesWritten = ReadUInt128(log.Slice(48, 16)) * 512_000d / 1e12;
        data.PowerCycles = ToLong(ReadUInt128(log.Slice(112, 16)));
        data.PowerOnHours = ToLong(ReadUInt128(log.Slice(128, 16)));
        data.UnsafeShutdowns = ToLong(ReadUInt128(log.Slice(144, 16)));
        data.MediaErrors = ToLong(ReadUInt128(log.Slice(160, 16)));
        data.MinutesAboveWarningTemp = BinaryPrimitives.ReadUInt32LittleEndian(log.Slice(192, 4));
        data.MinutesAboveCriticalTemp = BinaryPrimitives.ReadUInt32LittleEndian(log.Slice(196, 4));

        data.Sources.Add("log SMART do NVMe");
        return true;
    }

    // -----------------------------------------------------------------
    // ATA / SATA
    // -----------------------------------------------------------------

    /// <summary>
    /// Lê a tabela de atributos SMART (bloco "READ DATA" de 512 bytes ou o VendorSpecific do WMI)
    /// e os limites (bloco "READ THRESHOLDS"). Ambos têm 2 bytes de versão e 30 entradas de 12 bytes.
    /// </summary>
    public static List<AtaSmartAttribute> ParseAtaAttributes(ReadOnlySpan<byte> data, ReadOnlySpan<byte> thresholds)
    {
        var limits = new Dictionary<int, int>();
        for (var offset = 2; offset + 12 <= thresholds.Length && offset < 2 + 30 * 12; offset += 12)
        {
            var id = thresholds[offset];
            if (id != 0)
            {
                limits[id] = thresholds[offset + 1];
            }
        }

        var result = new List<AtaSmartAttribute>();
        for (var offset = 2; offset + 12 <= data.Length && offset < 2 + 30 * 12; offset += 12)
        {
            var id = data[offset];
            if (id == 0)
            {
                continue;
            }

            long raw = 0;
            for (var i = 5; i >= 0; i--)
            {
                raw = (raw << 8) | data[offset + 5 + i];
            }

            result.Add(new AtaSmartAttribute(id, data[offset + 3], data[offset + 4], limits.GetValueOrDefault(id), raw));
        }

        return result;
    }

    public static void ApplyAtaAttributes(IReadOnlyList<AtaSmartAttribute> attributes, DiskHealthData data)
    {
        if (attributes.Count == 0)
        {
            return;
        }

        data.Sources.Add("SMART do disco");

        foreach (var attribute in attributes)
        {
            if (attribute.IsFailing)
            {
                data.FailingAttributes.Add(AttributeName(attribute.Id));
            }

            switch (attribute.Id)
            {
                case 5:
                    data.ReallocatedSectors = attribute.Raw & 0xFFFFFFFF;
                    break;
                case 9:
                    // Alguns fabricantes guardam minutos/segundos nos bytes altos.
                    data.PowerOnHours ??= attribute.Raw & 0xFFFFFFFF;
                    break;
                case 10:
                    data.SpinRetries = attribute.Raw & 0xFFFF;
                    break;
                case 12:
                    data.PowerCycles ??= attribute.Raw & 0xFFFFFFFF;
                    break;
                case 187:
                    data.ReportedUncorrectable = attribute.Raw & 0xFFFF;
                    break;
                case 188:
                    data.CommandTimeouts = attribute.Raw & 0xFFFF;
                    break;
                case 190 when data.TemperatureC is null:
                case 194:
                    var temperature = (int)(attribute.Raw & 0xFF);
                    if (temperature is > 0 and < 120)
                    {
                        data.TemperatureC = temperature;
                    }
                    break;
                case 196:
                    data.ReallocationEvents = attribute.Raw & 0xFFFF;
                    break;
                case 197:
                    data.PendingSectors = attribute.Raw & 0xFFFFFFFF;
                    break;
                case 198:
                    data.OfflineUncorrectable = attribute.Raw & 0xFFFFFFFF;
                    break;
                case 199:
                    data.CrcErrors = attribute.Raw & 0xFFFFFFFF;
                    break;
                case 177 or 202 or 231 or 233 when !data.IsHdd && attribute.Current is > 0 and <= 100:
                    // Valor normalizado = vida restante (100 novo, 0 gasto).
                    data.PercentUsed = Math.Max(data.PercentUsed ?? 0, 100 - attribute.Current);
                    break;
                case 241 when !data.IsHdd && attribute.Raw > 0:
                    // A maioria dos SSDs conta em LBAs de 512 bytes.
                    data.TerabytesWritten ??= attribute.Raw * 512d / 1e12;
                    break;
            }
        }
    }

    // -----------------------------------------------------------------
    // Linux: saída JSON do smartctl (smartmontools 7+)
    // -----------------------------------------------------------------

    public static DiskHealthData FromSmartctlJson(JsonElement root, string deviceName)
    {
        var model = Text(root, "model_name") ?? Text(root, "model_family");
        var protocol = root.TryGetProperty("device", out var device) ? Text(device, "protocol") : null;
        var data = new DiskHealthData
        {
            IsNvme = string.Equals(protocol, "NVMe", StringComparison.OrdinalIgnoreCase),
            IsHdd = Number(root, "rotation_rate") is > 0,
            IsExternal = device.ValueKind == JsonValueKind.Object && (Text(device, "type") ?? "").StartsWith("sat", StringComparison.Ordinal) &&
                         (Text(device, "name") ?? "").Contains("usb", StringComparison.OrdinalIgnoreCase)
        };
        var kind = data.IsNvme ? "NVMe" : data.IsHdd ? "HD" : "SSD";
        data.Group = model is null ? $"{deviceName} ({kind})" : $"{model} ({kind}, {deviceName})";

        if (root.TryGetProperty("smart_status", out var status) &&
            status.TryGetProperty("passed", out var passed) &&
            passed.ValueKind == JsonValueKind.False)
        {
            data.PredictFailure = true;
        }

        if (root.TryGetProperty("temperature", out var temperature) && Number(temperature, "current") is > 0 and var celsius)
        {
            data.TemperatureC = (int)celsius;
        }
        if (root.TryGetProperty("power_on_time", out var power) && Number(power, "hours") is > 0 and var hours)
        {
            data.PowerOnHours = hours;
        }
        data.PowerCycles = Number(root, "power_cycle_count");

        if (root.TryGetProperty("nvme_smart_health_information_log", out var nvme))
        {
            data.Sources.Add("log SMART do NVMe");
            data.NvmeCriticalWarning = (int?)Number(nvme, "critical_warning");
            data.AvailableSpare = (int?)Number(nvme, "available_spare");
            data.AvailableSpareThreshold = (int?)Number(nvme, "available_spare_threshold");
            data.PercentUsed = (int?)Number(nvme, "percentage_used");
            data.MediaErrors = Number(nvme, "media_errors");
            data.UnsafeShutdowns = Number(nvme, "unsafe_shutdowns");
            data.PowerCycles ??= Number(nvme, "power_cycles");
            data.PowerOnHours ??= Number(nvme, "power_on_hours");
            data.MinutesAboveWarningTemp = Number(nvme, "warning_temp_time");
            data.MinutesAboveCriticalTemp = Number(nvme, "critical_comp_time");
            if (Number(nvme, "data_units_written") is > 0 and var written)
            {
                data.TerabytesWritten = written * 512_000d / 1e12;
            }
        }

        if (root.TryGetProperty("ata_smart_attributes", out var ata) && ata.TryGetProperty("table", out var table))
        {
            var attributes = new List<AtaSmartAttribute>();
            foreach (var attribute in table.EnumerateArray())
            {
                var raw = attribute.TryGetProperty("raw", out var rawValue) ? Number(rawValue, "value") ?? 0 : 0;
                attributes.Add(new AtaSmartAttribute(
                    (int)(Number(attribute, "id") ?? 0),
                    (int)(Number(attribute, "value") ?? 0),
                    (int)(Number(attribute, "worst") ?? 0),
                    (int)(Number(attribute, "thresh") ?? 0),
                    raw));
            }
            ApplyAtaAttributes(attributes, data);
        }

        return data;

        static string? Text(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        static long? Number(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt64(out var number)
                ? number
                : null;
    }

    // -----------------------------------------------------------------
    // Avaliação
    // -----------------------------------------------------------------

    /// <summary>
    /// Monta o veredito (Bom / Atenção / Ruim) e os itens do disco. O veredito vem primeiro e diz o
    /// motivo; os itens que o justificam ficam com o mesmo status.
    /// </summary>
    public static List<DiagnosticItem> Evaluate(DiskHealthData d)
    {
        var items = new List<DiagnosticItem>();
        var bad = new List<string>();
        var caution = new List<string>();

        DiagnosticItem Add(string label, string value, DiagnosticStatus status = DiagnosticStatus.Ok, string? hint = null)
        {
            var item = new DiagnosticItem { Group = d.Group, Label = label, Value = value, Status = status, Hint = hint };
            items.Add(item);
            return item;
        }

        // Saúde informada pelo Windows / previsão de falha.
        switch (d.WindowsHealth)
        {
            case 0:
                Add("Saúde (Windows)", "Saudável");
                break;
            case 1:
                Add("Saúde (Windows)", "Atenção", DiagnosticStatus.Warning, "O Windows indica que o disco pode falhar.");
                caution.Add("o Windows marcou o disco com alerta");
                break;
            case 2:
                Add("Saúde (Windows)", "Com defeito", DiagnosticStatus.Error, "O Windows marcou o disco como defeituoso.");
                bad.Add("o Windows marcou o disco como defeituoso");
                break;
        }

        if (d.PredictFailure)
        {
            Add("Previsão de falha (SMART)", "O disco prevê falha", DiagnosticStatus.Error);
            bad.Add("o SMART prevê falha");
        }

        if (d.FailingAttributes.Count > 0)
        {
            Add("Atributos no limite", string.Join(", ", d.FailingAttributes), DiagnosticStatus.Error,
                "Esses valores chegaram ao limite definido pelo fabricante: o disco está falhando.");
            bad.Add("atributos SMART no limite do fabricante");
        }

        // NVMe: aviso crítico.
        if (d.NvmeCriticalWarning is > 0 and var warning)
        {
            var reasons = new List<string>();
            if ((warning & 0x01) != 0) reasons.Add("reserva abaixo do mínimo");
            if ((warning & 0x02) != 0) reasons.Add("temperatura fora do limite");
            if ((warning & 0x04) != 0) reasons.Add("confiabilidade degradada");
            if ((warning & 0x08) != 0) reasons.Add("disco em modo somente leitura");
            if ((warning & 0x10) != 0) reasons.Add("falha no backup da memória volátil");
            if ((warning & 0x20) != 0) reasons.Add("memória persistente somente leitura");
            var severe = (warning & 0x3D) != 0;
            Add("Aviso crítico (NVMe)", string.Join(", ", reasons), severe ? DiagnosticStatus.Error : DiagnosticStatus.Warning,
                severe ? "O próprio SSD está avisando que vai falhar. Faça backup e troque." : "O SSD esquentou além do limite.");
            (severe ? bad : caution).Add(string.Join(", ", reasons));
        }

        // Vida útil (SSD).
        if (d.PercentUsed is { } used && !d.IsHdd)
        {
            var remaining = Math.Max(0, 100 - used);
            var written = d.TerabytesWritten is > 0 ? $" · {FormatTb(d.TerabytesWritten.Value)} gravados" : "";
            var status = used >= 100 ? DiagnosticStatus.Error : used >= 90 ? DiagnosticStatus.Warning : DiagnosticStatus.Ok;
            Add("Vida útil", $"{remaining}% restante ({used}% usada){written}", status,
                status == DiagnosticStatus.Ok ? null : "SSD no fim da vida útil prevista pelo fabricante. Planeje a troca.");
            if (status == DiagnosticStatus.Error) bad.Add("vida útil esgotada");
            else if (status == DiagnosticStatus.Warning) caution.Add($"só {remaining}% de vida útil");
        }
        else if (d.TerabytesWritten is > 0)
        {
            Add("Total gravado", FormatTb(d.TerabytesWritten.Value), DiagnosticStatus.Info);
        }

        if (d.AvailableSpare is { } spare && d.AvailableSpareThreshold is { } spareThreshold && spare <= 100)
        {
            var status = spareThreshold > 0 && spare <= spareThreshold ? DiagnosticStatus.Error
                : spare < 100 && spare <= spareThreshold + 10 ? DiagnosticStatus.Warning
                : DiagnosticStatus.Ok;
            Add("Reserva disponível", $"{spare}% (mínimo {spareThreshold}%)", status,
                status == DiagnosticStatus.Ok ? null : "O SSD está gastando a área de reserva para substituir células ruins.");
            if (status == DiagnosticStatus.Error) bad.Add("reserva do SSD esgotada");
            else if (status == DiagnosticStatus.Warning) caution.Add("reserva do SSD perto do mínimo");
        }

        // Setores (ATA).
        CountItem("Setores realocados", d.ReallocatedSectors, 100,
            "Setores ruins já trocados pela reserva. Se o número crescer, o disco está se degradando.", "setores realocados");
        CountItem("Setores pendentes", d.PendingSectors, 10,
            "Setores que não puderam ser lidos e aguardam realocação: risco de perder arquivos.", "setores pendentes");
        CountItem("Setores incorrigíveis", d.OfflineUncorrectable, 1,
            "Setores ilegíveis: dados nesses pontos já foram perdidos.", "setores incorrigíveis");
        CountItem("Erros incorrigíveis reportados", d.ReportedUncorrectable, 10,
            "Leituras que o disco não conseguiu corrigir.", "erros incorrigíveis");
        CountItem("Erros de mídia (NVMe)", d.MediaErrors, 10,
            "Erros de integridade que o SSD não conseguiu recuperar.", "erros de mídia");

        if (d.IsHdd && d.SpinRetries is > 0)
        {
            Add("Tentativas de girar", d.SpinRetries.Value.ToString(CultureInfo.InvariantCulture), DiagnosticStatus.Warning,
                "O motor do HD tem dificuldade para girar: sinal de defeito mecânico ou fonte fraca.");
            caution.Add("o motor tem dificuldade para girar");
        }

        if (d.CommandTimeouts is > 0)
        {
            var status = d.CommandTimeouts > 100 ? DiagnosticStatus.Warning : DiagnosticStatus.Info;
            Add("Comandos expirados", d.CommandTimeouts.Value.ToString(CultureInfo.InvariantCulture), status,
                "Pode ser cabo/conector, fonte ou o próprio disco.");
            if (status == DiagnosticStatus.Warning) caution.Add("muitos comandos expirados");
        }

        if (d.CrcErrors is > 0)
        {
            var status = d.CrcErrors > 100 ? DiagnosticStatus.Warning : DiagnosticStatus.Info;
            Add("Erros de comunicação (CRC)", d.CrcErrors.Value.ToString(CultureInfo.InvariantCulture), status,
                "Erros no cabo ou conector SATA, não no disco. Troque o cabo se o número crescer.");
            if (status == DiagnosticStatus.Warning) caution.Add("muitos erros de cabo/conector");
        }

        // Erros contados pelo Windows.
        if (d.ReadErrorsUncorrected is not null || d.WriteErrorsUncorrected is not null)
        {
            var total = (d.ReadErrorsUncorrected ?? 0) + (d.WriteErrorsUncorrected ?? 0);
            Add("Erros não corrigidos (Windows)", $"{d.ReadErrorsUncorrected ?? 0} de leitura, {d.WriteErrorsUncorrected ?? 0} de escrita",
                total > 0 ? DiagnosticStatus.Error : DiagnosticStatus.Ok,
                total > 0 ? "O disco perdeu dados em leituras ou gravações." : null);
            if (total > 0) bad.Add("erros de leitura/escrita não corrigidos");
        }
        if (d.ReadWriteErrorsCorrected is > 0)
        {
            Add("Erros corrigidos", d.ReadWriteErrorsCorrected.Value.ToString(CultureInfo.InvariantCulture),
                d.ReadWriteErrorsCorrected > 1000 ? DiagnosticStatus.Warning : DiagnosticStatus.Info);
            if (d.ReadWriteErrorsCorrected > 1000) caution.Add("muitos erros corrigidos");
        }

        // Temperatura.
        if (d.TemperatureC is { } temperature)
        {
            var (warn, error) = d.IsHdd ? (50, 60) : d.IsNvme ? (70, 80) : (65, 75);
            if (d.TemperatureLimitC is > 0 and var limit && !d.IsHdd)
            {
                error = Math.Min(error, limit);
                warn = Math.Min(warn, limit - 5);
            }
            var status = temperature >= error ? DiagnosticStatus.Error : temperature >= warn ? DiagnosticStatus.Warning : DiagnosticStatus.Ok;
            var history = d.MinutesAboveWarningTemp is > 0
                ? $" · {FormatMinutes(d.MinutesAboveWarningTemp.Value)} acima do limite de alerta"
                : "";
            Add("Temperatura", $"{temperature} °C{(d.TemperatureLimitC is > 0 ? $" (limite {d.TemperatureLimitC} °C)" : "")}{history}",
                status, status == DiagnosticStatus.Ok ? null : "Disco quente: verifique ventilação, poeira e se o notebook está sobre superfície macia.");
            if (status != DiagnosticStatus.Ok) caution.Add("temperatura alta");
        }

        // Informativos.
        if (d.PowerOnHours is > 0 and var hours)
        {
            Add("Tempo ligado", $"{hours.ToString("N0", PtBr)} horas (~{(hours / 24.0 / 365).ToString("0.0", PtBr)} anos)", DiagnosticStatus.Info);
        }
        if (d.PowerCycles is > 0 and var cycles)
        {
            Add("Ciclos liga/desliga", cycles.ToString("N0", PtBr), DiagnosticStatus.Info);
        }
        if (d.UnsafeShutdowns is > 0 and var unsafeShutdowns)
        {
            var ratio = d.PowerCycles is > 0 ? (double)unsafeShutdowns / d.PowerCycles.Value : 0;
            Add("Desligamentos inesperados", unsafeShutdowns.ToString("N0", PtBr), DiagnosticStatus.Info,
                ratio > 0.3 ? "Muitos desligamentos sem aviso (falta de energia, bateria ou botão segurado). Isso aumenta o risco de corromper dados." : null);
        }

        // Veredito no topo do grupo.
        DiagnosticItem verdict;
        if (bad.Count > 0)
        {
            verdict = new DiagnosticItem
            {
                Group = d.Group, Label = VerdictLabel, Value = $"Ruim: {Join(bad)}", Status = DiagnosticStatus.Error,
                Hint = "Faça backup agora e programe a troca do disco."
            };
        }
        else if (caution.Count > 0)
        {
            verdict = new DiagnosticItem
            {
                Group = d.Group, Label = VerdictLabel, Value = $"Atenção: {Join(caution)}", Status = DiagnosticStatus.Warning,
                Hint = "Acompanhe: rode o teste de novo em alguns dias e veja se os números aumentam."
            };
        }
        else if (d.HasSmartDetail || d.WindowsHealth == 0)
        {
            verdict = new DiagnosticItem { Group = d.Group, Label = VerdictLabel, Value = "Bom", Status = DiagnosticStatus.Ok };
        }
        else
        {
            verdict = new DiagnosticItem
            {
                Group = d.Group, Label = VerdictLabel, Value = "Sem dados SMART", Status = DiagnosticStatus.Info,
                Hint = d.IsExternal ? "Discos USB costumam esconder o SMART." : "O controlador não expôs o SMART deste disco."
            };
        }

        if (d.Sources.Count > 0)
        {
            items.Add(new DiagnosticItem
            {
                Group = d.Group, Label = "Fonte dos dados", Value = string.Join(" + ", d.Sources.Distinct()), Status = DiagnosticStatus.Info
            });
        }

        items.Insert(0, verdict);

        // Itens que só repetem o motivo não somam como problema extra no resumo do teste.
        return items;

        void CountItem(string label, long? value, long errorAt, string hint, string reason)
        {
            if (value is null)
            {
                return;
            }
            var status = value >= errorAt ? DiagnosticStatus.Error : value > 0 ? DiagnosticStatus.Warning : DiagnosticStatus.Ok;
            Add(label, value.Value.ToString("N0", PtBr), status, status == DiagnosticStatus.Ok ? null : hint);
            if (status == DiagnosticStatus.Error) bad.Add($"{value.Value.ToString("N0", PtBr)} {reason}");
            else if (status == DiagnosticStatus.Warning) caution.Add($"{value.Value.ToString("N0", PtBr)} {reason}");
        }
    }

    /// <summary>Resumo do teste de discos a partir dos vereditos (ex.: "NVMe Samsung: Bom. HD Seagate: Atenção.").</summary>
    public static string? Summarize(IEnumerable<DiagnosticItem> items)
    {
        var verdicts = items.Where(x => x.Label == VerdictLabel && x.Status != DiagnosticStatus.Info).ToList();
        if (verdicts.Count == 0)
        {
            return null;
        }

        var bad = verdicts.Count(x => x.Status == DiagnosticStatus.Error);
        var caution = verdicts.Count(x => x.Status == DiagnosticStatus.Warning);
        var good = verdicts.Count - bad - caution;
        var parts = new List<string>();
        if (bad > 0) parts.Add($"{bad} ruim");
        if (caution > 0) parts.Add($"{caution} em atenção");
        if (good > 0) parts.Add($"{good} bom");
        return $"{verdicts.Count} disco(s): {string.Join(", ", parts)}.";
    }

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static string Join(List<string> reasons) => string.Join("; ", reasons.Distinct());

    private static string FormatTb(double tb) =>
        tb >= 1 ? $"{tb.ToString("0.0", PtBr)} TB" : $"{(tb * 1000).ToString("0", PtBr)} GB";

    private static string FormatMinutes(long minutes) =>
        minutes >= 120 ? $"{(minutes / 60.0).ToString("0", PtBr)} h" : $"{minutes} min";

    private static double ReadUInt128(ReadOnlySpan<byte> value)
    {
        var low = BinaryPrimitives.ReadUInt64LittleEndian(value[..8]);
        var high = BinaryPrimitives.ReadUInt64LittleEndian(value.Slice(8, 8));
        return high * 18446744073709551616d + low;
    }

    private static long ToLong(double value) => value >= long.MaxValue ? long.MaxValue : (long)value;
}
