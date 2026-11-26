using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Remotely.Shared.Entities;

public class DeviceGroup
{
    [StringLength(200)]
    public required string Name { get; set; }

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public string ID { get; set; } = null!;

    [JsonIgnore]
    public List<Device> Devices { get; set; } = new();

    [JsonIgnore]
    public Organization? Organization { get; set; }

    public string OrganizationID { get; set; } = null!;

    [JsonIgnore]
    public List<RemotelyUser> Users { get; set; } = new();

    [JsonIgnore]
    public List<ScriptSchedule>? ScriptSchedules { get; set; }

    /// <summary>
    /// Aceite do usuário no controle remoto dos computadores do grupo: null = padrão do servidor,
    /// "AutoAccept" (pergunta e aceita sozinho), "Ask" (exige o aceite) ou "Never" (não pergunta).
    /// </summary>
    [StringLength(20)]
    public string? RemoteControlConsent { get; set; }

    /// <summary>Avisa por e-mail/webhook quando alguém abre uma sessão num computador do grupo.</summary>
    public bool NotifyRemoteSessions { get; set; }
}
