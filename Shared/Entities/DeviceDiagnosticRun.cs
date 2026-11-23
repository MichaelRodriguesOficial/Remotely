using System.ComponentModel.DataAnnotations;

namespace Remotely.Shared.Entities;

/// <summary>Uma execução da aba Diagnóstico (vários testes) num dispositivo.</summary>
public class DeviceDiagnosticRun
{
    [Key]
    [StringLength(64)]
    public string ID { get; set; } = Guid.NewGuid().ToString();

    public string DeviceID { get; set; } = null!;

    [StringLength(256)]
    public string? RequestedBy { get; set; }

    /// <summary>IDs dos testes pedidos, separados por vírgula.</summary>
    [StringLength(500)]
    public string Tests { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>Resumo: Running, Ok, Warning, Error.</summary>
    [StringLength(20)]
    public string Status { get; set; } = "Running";

    /// <summary>JSON de <see cref="Remotely.Shared.Models.Diagnostics.DiagnosticReport"/>.</summary>
    public string? Data { get; set; }
}
