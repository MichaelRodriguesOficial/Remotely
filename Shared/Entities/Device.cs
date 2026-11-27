using Remotely.Shared.Attributes;
using Remotely.Shared.Models;
using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace Remotely.Shared.Entities;

public class Device
{

    [Sortable]
    [Display(Name = "Versão do agente")]
    public string? AgentVersion { get; set; }

    public ICollection<Alert>? Alerts { get; set; }

    [StringLength(100)]
    [Sortable]
    [Display(Name = "Apelido")]
    public string? Alias { get; set; }

    [Sortable]
    [Display(Name = "Uso de CPU")]
    public double CpuUtilization { get; set; }

    [Sortable]
    [Display(Name = "Usuário atual")]
    public string? CurrentUser { get; set; }

    /// <summary>Nome (prenome) do usuário logado, quando o agente consegue separar.</summary>
    [StringLength(100)]
    public string? CurrentUserGivenName { get; set; }

    /// <summary>Sobrenome do usuário logado, quando o agente consegue separar.</summary>
    [StringLength(100)]
    public string? CurrentUserSurname { get; set; }

    /// <summary>Número de inventário (placa de patrimônio), preenchido manualmente.</summary>
    [StringLength(100)]
    [Sortable]
    [Display(Name = "Número de inventário")]
    public string? AssetTag { get; set; }

    /// <summary>Localização física, preenchida manualmente.</summary>
    [StringLength(200)]
    [Sortable]
    [Display(Name = "Localização")]
    public string? Location { get; set; }

    // Resumo do inventário (o inventário completo fica em DeviceInventories).

    [StringLength(50)]
    [Sortable]
    [Display(Name = "Tipo")]
    public string? DeviceType { get; set; }

    [StringLength(200)]
    [Sortable]
    [Display(Name = "Fabricante")]
    public string? Manufacturer { get; set; }

    [StringLength(200)]
    [Sortable]
    [Display(Name = "Modelo")]
    public string? Model { get; set; }

    [StringLength(200)]
    [Sortable]
    [Display(Name = "Processador")]
    public string? ProcessorName { get; set; }

    [StringLength(200)]
    [Sortable]
    [Display(Name = "Número de série")]
    public string? SerialNumber { get; set; }

    [Display(Name = "Inventário atualizado em")]
    public DateTimeOffset? InventoryUpdatedAt { get; set; }

    public DeviceGroup? DeviceGroup { get; set; }
    public string? DeviceGroupID { get; set; }

    [Sortable]
    [Display(Name = "Nome do dispositivo")]
    public string? DeviceName { get; set; }
    public List<Drive>? Drives { get; set; }

    [Key]
    public string ID { get; set; } = Guid.NewGuid().ToString();

    public bool Is64Bit { get; set; }
    public bool IsOnline { get; set; }

    [Sortable]
    [Display(Name = "Última conexão")]
    public DateTimeOffset LastOnline { get; set; }

    [Display(Name = "Endereços MAC")]
    public string[] MacAddresses { get; set; } = Array.Empty<string>();

    [StringLength(5000)]
    public string? Notes { get; set; }

    [JsonIgnore]
    public Organization? Organization { get; set; }

    public string OrganizationID { get; set; } = null!;
    public Architecture OSArchitecture { get; set; }

    [Sortable]
    [Display(Name = "Sistema operacional")]
    public string? OSDescription { get; set; }

    [Sortable]
    [Display(Name = "Plataforma")]
    public string? Platform { get; set; }

    [Sortable]
    [Display(Name = "Processadores")]
    public int ProcessorCount { get; set; }

    public string? PublicIP { get; set; }

    [JsonIgnore]
    public List<ScriptResult> ScriptResults { get; set; } = new();

    [JsonIgnore]
    public List<ScriptRun> ScriptRuns { get; set; } = new();

    [JsonIgnore]
    public List<ScriptSchedule> ScriptSchedules { get; set; } = new();

    public string? ServerVerificationToken { get; set; }

    /// <summary>
    /// Hash (SHA-256) da chave que o agente deste computador gerou. Só quem tem a chave
    /// conecta com este ID; vazio = ainda não registrada (registra no próximo contato).
    /// </summary>
    [StringLength(100)]
    [JsonIgnore]
    public string? AgentKeyHash { get; set; }

    [StringLength(200)]
    [Sortable]
    [Display(Name = "Tags")]
    public string? Tags { get; set; }

    [Sortable]
    [Display(Name = "Memória total")]
    public double TotalMemory { get; set; }

    [Sortable]
    [Display(Name = "Armazenamento total")]
    public double TotalStorage { get; set; }

    [Sortable]
    [Display(Name = "Memória usada")]
    public double UsedMemory { get; set; }

    [Sortable]
    [Display(Name = "Memória usada %")]
    public double UsedMemoryPercent
    {
        get
        {
            if (TotalMemory == 0)
            {
                return 0;
            }
            return UsedMemory / TotalMemory;
        }
    }

    [Sortable]
    [Display(Name = "Armazenamento usado")]
    public double UsedStorage { get; set; }

    [Sortable]
    [Display(Name = "Armazenamento usado %")]
    public double UsedStoragePercent
    {
        get
        {
            if (TotalStorage == 0)
            {
                return 0;
            }
            return UsedStorage / TotalStorage;
        }
    }
}