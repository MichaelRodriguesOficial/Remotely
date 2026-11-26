using System.ComponentModel.DataAnnotations;

namespace Remotely.Shared.Entities;

/// <summary>Registro de uma sessão de controle remoto (quem, onde, quando e como terminou).</summary>
public class RemoteControlSessionLog
{
    [Key]
    [StringLength(64)]
    public string ID { get; set; } = Guid.NewGuid().ToString();

    [StringLength(64)]
    public string OrganizationID { get; set; } = string.Empty;

    /// <summary>Vazio no modo assistido (ID de 9 dígitos), quando o computador não é um dispositivo cadastrado.</summary>
    [StringLength(64)]
    public string? DeviceID { get; set; }

    /// <summary>Nome do computador no momento da sessão (fica no histórico mesmo se o dispositivo for excluído).</summary>
    [StringLength(256)]
    public string? DeviceName { get; set; }

    /// <summary>Login do técnico no painel.</summary>
    [StringLength(256)]
    public string? RequesterUserName { get; set; }

    /// <summary>Nome exibido ao usuário.</summary>
    [StringLength(256)]
    public string? RequesterName { get; set; }

    /// <summary>"Unattended" (autônomo) ou "Attended" (assistido).</summary>
    [StringLength(20)]
    public string Mode { get; set; } = string.Empty;

    public bool ViewOnly { get; set; }

    [StringLength(64)]
    public string? ViewerIp { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>Como a sessão terminou (ex.: "Encerrada pelo técnico", "Navegador fechado").</summary>
    [StringLength(100)]
    public string? EndReason { get; set; }
}
