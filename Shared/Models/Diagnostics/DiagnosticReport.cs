using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Remotely.Shared.Models.Diagnostics;

/// <summary>Testes disponíveis na aba Diagnóstico.</summary>
public static class DiagnosticTests
{
    public const string Disks = "disks";
    public const string Battery = "battery";
    public const string Devices = "devices";
    public const string Network = "network";
    public const string System = "system";
    public const string DiskSpeed = "disk-speed";
    public const string CpuStress = "cpu-stress";
    public const string MemoryTest = "memory-test";

    public sealed record TestInfo(string Id, string Name, string Description, bool Heavy, bool RequiresRestart = false);

    public static IReadOnlyList<TestInfo> All { get; } =
    [
        new(Disks, "Saúde dos discos", "SMART: temperatura, desgaste, erros de leitura e escrita, horas ligado e espaço livre.", false),
        new(Battery, "Bateria", "Capacidade real em relação à de fábrica, ciclos e carga.", false),
        new(Devices, "Dispositivos com erro", "Drivers com problema, telas azuis, desligamentos inesperados e erros críticos recentes.", false),
        new(Network, "Rede", "Ping do gateway, DNS, internet e servidor, perda de pacotes, latência e sinal do Wi-Fi.", false),
        new(System, "Sistema", "Reinício pendente, tempo ligado, atualizações, antivírus, BitLocker e último teste de memória.", false),
        new(DiskSpeed, "Velocidade do disco", "Grava e lê um arquivo temporário (até 1 GB) e mede MB/s. Cerca de 30 segundos.", true),
        new(CpuStress, "Estresse de CPU", "CPU a 100% por 60 segundos, medindo frequência e temperatura (superaquecimento).", true),
        new(MemoryTest, "Teste de memória", "Agenda o Diagnóstico de Memória do Windows e reinicia o computador em 2 minutos.", true, RequiresRestart: true),
    ];

    public static TestInfo? Get(string id) => All.FirstOrDefault(x => x.Id == id);
}

public enum DiagnosticStatus
{
    Pending = 0,
    Running = 1,
    Ok = 2,
    Warning = 3,
    Error = 4,
    Skipped = 5,
    Info = 6
}

/// <summary>Resultado de uma execução de diagnóstico (vários testes).</summary>
public class DiagnosticReport
{
    public string RunId { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? FinishedAt { get; set; }
    public bool Completed { get; set; }
    public List<DiagnosticTestResult> Tests { get; set; } = [];

    public DiagnosticStatus OverallStatus =>
        Tests.Count == 0 ? DiagnosticStatus.Pending :
        Tests.Any(x => x.Status == DiagnosticStatus.Error) ? DiagnosticStatus.Error :
        Tests.Any(x => x.Status == DiagnosticStatus.Warning) ? DiagnosticStatus.Warning :
        Tests.Any(x => x.Status is DiagnosticStatus.Pending or DiagnosticStatus.Running) ? DiagnosticStatus.Running :
        DiagnosticStatus.Ok;
}

public class DiagnosticTestResult
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DiagnosticStatus Status { get; set; }

    /// <summary>Uma frase com a conclusão ("2 discos saudáveis", "Perda de 20% no Wi-Fi").</summary>
    public string? Summary { get; set; }

    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public List<DiagnosticItem> Items { get; set; } = [];
}

/// <summary>Uma linha do resultado: o que foi verificado, o valor e se está bom.</summary>
public class DiagnosticItem
{
    /// <summary>Agrupador opcional (ex.: nome do disco).</summary>
    public string? Group { get; set; }

    public string Label { get; set; } = string.Empty;
    public string? Value { get; set; }
    public DiagnosticStatus Status { get; set; } = DiagnosticStatus.Info;

    /// <summary>Explicação ou recomendação quando há alerta.</summary>
    public string? Hint { get; set; }
}

/// <summary>JSON compactado (gzip) para trafegar relatórios grandes pelo SignalR.</summary>
public static class DiagnosticSerializer
{
    public const int MaxJsonBytes = 4 * 1024 * 1024;

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string ToJson(DiagnosticReport report) => JsonSerializer.Serialize(report, JsonOptions);

    public static DiagnosticReport? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DiagnosticReport>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static byte[] Compress(DiagnosticReport report)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(json, 0, json.Length);
        }
        return output.ToArray();
    }

    public static bool TryDecompress(byte[]? data, out DiagnosticReport? report)
    {
        report = null;
        if (data is null || data.Length == 0)
        {
            return false;
        }

        try
        {
            using var input = new MemoryStream(data);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > MaxJsonBytes)
                {
                    return false;
                }
                output.Write(buffer, 0, read);
            }

            report = JsonSerializer.Deserialize<DiagnosticReport>(output.ToArray(), JsonOptions);
            return report is not null && !string.IsNullOrWhiteSpace(report.RunId);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException)
        {
            return false;
        }
    }
}
