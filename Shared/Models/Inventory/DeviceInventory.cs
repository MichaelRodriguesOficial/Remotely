namespace Remotely.Shared.Models.Inventory;

/// <summary>
/// Inventário de hardware e software coletado pelo agente (no estilo do
/// GLPI Inventory). Trafega como JSON compactado (gzip) e é guardado no
/// servidor como JSON. Os campos manuais (número de inventário e localização)
/// ficam no próprio dispositivo, não aqui.
/// </summary>
public class DeviceInventory
{
    /// <summary>Versão do formato, para evoluções futuras.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    /// 2: <see cref="InventoryOperatingSystem.SerialNumber"/> passou a ser a chave do
    /// produto e o ID do produto foi para <see cref="InventoryOperatingSystem.ProductId"/>.
    /// </summary>
    public const int CurrentSchemaVersion = 2;

    public DateTimeOffset CollectedAt { get; set; } = DateTimeOffset.Now;

    // ---- Identificação ----
    public string? Uuid { get; set; }

    /// <summary>Desktop, Notebook, Servidor, Máquina virtual, Tablet, Mini PC, Tudo em um...</summary>
    public string? DeviceType { get; set; }

    /// <summary>Tipo de gabinete informado pelo SMBIOS (ex.: "Notebook", "Torre").</summary>
    public string? ChassisType { get; set; }

    /// <summary>VMware, Hyper-V, VirtualBox, KVM... quando é uma máquina virtual.</summary>
    public string? Hypervisor { get; set; }

    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? Sku { get; set; }

    /// <summary>Número de patrimônio gravado pelo fabricante no SMBIOS, quando existe.</summary>
    public string? FirmwareAssetTag { get; set; }

    public string? Domain { get; set; }
    public bool? PartOfDomain { get; set; }
    public string? Workgroup { get; set; }

    // ---- Sistema operacional ----
    public InventoryOperatingSystem? OperatingSystem { get; set; }

    // ---- Firmware ----
    public InventoryBios? Bios { get; set; }

    public InventoryMotherboard? Motherboard { get; set; }

    // ---- Componentes ----
    public List<InventoryProcessor> Processors { get; set; } = [];
    public List<InventoryMemoryModule> Memory { get; set; } = [];
    public List<InventoryDisk> Disks { get; set; } = [];
    public List<InventoryComponent> Components { get; set; } = [];

    // ---- Volumes, rede, softwares e segurança ----
    public List<InventoryVolume> Volumes { get; set; } = [];
    public List<InventoryNetworkPort> NetworkPorts { get; set; } = [];
    public List<InventorySoftware> Software { get; set; } = [];
    public List<InventoryAntivirus> Antivirus { get; set; } = [];

    /// <summary>Partes que não puderam ser coletadas (para diagnóstico).</summary>
    public List<string> Warnings { get; set; } = [];
}

public class InventoryOperatingSystem
{
    public string? Name { get; set; }
    public string? Edition { get; set; }
    public string? Version { get; set; }
    public string? Build { get; set; }
    public string? DisplayVersion { get; set; }
    public string? Architecture { get; set; }
    public string? KernelVersion { get; set; }

    /// <summary>
    /// Número de série do sistema. No Windows, a chave do produto (como no GLPI):
    /// decodificada do registro ou gravada no firmware (OEM).
    /// </summary>
    public string? SerialNumber { get; set; }

    /// <summary>ID do produto (Windows) ou machine-id (Linux).</summary>
    public string? ProductId { get; set; }

    public string? Language { get; set; }
    public string? TimeZone { get; set; }
    public DateTimeOffset? InstallDate { get; set; }
    public DateTimeOffset? LastBoot { get; set; }
    public string? LicenseStatus { get; set; }
}

public class InventoryBios
{
    public string? Manufacturer { get; set; }
    public string? Version { get; set; }
    public string? SmbiosVersion { get; set; }
    public string? SerialNumber { get; set; }
    public DateTimeOffset? ReleaseDate { get; set; }

    /// <summary>UEFI ou Legado (BIOS).</summary>
    public string? Mode { get; set; }

    public bool? SecureBoot { get; set; }
}

public class InventoryMotherboard
{
    public string? Manufacturer { get; set; }
    public string? Product { get; set; }
    public string? Version { get; set; }
    public string? SerialNumber { get; set; }
}

public class InventoryProcessor
{
    public string? Name { get; set; }
    public string? Manufacturer { get; set; }
    public int? Cores { get; set; }
    public int? Threads { get; set; }
    public int? MaxClockMhz { get; set; }
    public string? Socket { get; set; }
    public string? Architecture { get; set; }
}

public class InventoryMemoryModule
{
    public string? Slot { get; set; }
    public long? CapacityBytes { get; set; }
    public int? SpeedMhz { get; set; }
    public string? Type { get; set; }
    public string? FormFactor { get; set; }
    public string? Manufacturer { get; set; }
    public string? PartNumber { get; set; }
    public string? SerialNumber { get; set; }
}

public class InventoryDisk
{
    public string? Model { get; set; }
    public string? Manufacturer { get; set; }
    public string? SerialNumber { get; set; }
    public string? Firmware { get; set; }
    public long? SizeBytes { get; set; }

    /// <summary>SATA, NVMe, USB, SCSI...</summary>
    public string? Interface { get; set; }

    /// <summary>SSD, HD, Removível...</summary>
    public string? MediaType { get; set; }

    public string? Health { get; set; }
}

public class InventoryVolume
{
    /// <summary>Letra (C:) ou ponto de montagem (/home).</summary>
    public string? MountPoint { get; set; }
    public string? Label { get; set; }
    public string? FileSystem { get; set; }
    public string? Device { get; set; }
    public long? TotalBytes { get; set; }
    public long? FreeBytes { get; set; }

    /// <summary>Local, Removível, Rede, CD/DVD...</summary>
    public string? Type { get; set; }

    /// <summary>Estado da criptografia (BitLocker/LUKS), quando conhecido.</summary>
    public string? Encryption { get; set; }
}

public class InventoryNetworkPort
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Manufacturer { get; set; }
    public string? MacAddress { get; set; }

    /// <summary>Ethernet, Wi-Fi, Loopback, Virtual...</summary>
    public string? Type { get; set; }

    public bool? Connected { get; set; }
    public long? SpeedMbps { get; set; }
    public bool? Dhcp { get; set; }
    public bool? Virtual { get; set; }
    public List<string> IPv4 { get; set; } = [];
    public List<string> IPv6 { get; set; } = [];
    public List<string> SubnetMasks { get; set; } = [];
    public List<string> Gateways { get; set; } = [];
    public List<string> DnsServers { get; set; } = [];
}

/// <summary>
/// Componentes sem estrutura própria: placa de vídeo, placa de som,
/// controladoras, bateria, monitores, leitores...
/// </summary>
public class InventoryComponent
{
    /// <summary>Categoria em português: "Placa de vídeo", "Placa de som", "Controladora", "Bateria"...</summary>
    public string Category { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Manufacturer { get; set; }
    public string? SerialNumber { get; set; }
    public string? Version { get; set; }

    /// <summary>Detalhes extras em pares chave/valor (ex.: "Memória" = "4 GB").</summary>
    public Dictionary<string, string> Details { get; set; } = [];
}

public class InventorySoftware
{
    public string Name { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? Publisher { get; set; }
    public DateTimeOffset? InstallDate { get; set; }
    public string? Architecture { get; set; }
    public long? SizeBytes { get; set; }

    /// <summary>Registro, dpkg, rpm, Microsoft Store...</summary>
    public string? Source { get; set; }
}

public class InventoryAntivirus
{
    public string Name { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? Version { get; set; }
    public bool? Enabled { get; set; }
    public bool? UpToDate { get; set; }
    public string? SignatureVersion { get; set; }
}
