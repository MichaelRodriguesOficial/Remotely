using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Remotely.Shared.Models.Inventory;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;

namespace Remotely.Agent.Services.Inventory;

/// <summary>
/// Inventário do Windows via WMI/CIM e registro (as mesmas fontes que o GLPI Agent usa).
/// </summary>
[SupportedOSPlatform("windows")]
public class InventoryCollectorWin : InventoryCollectorBase
{
    private const string Cimv2 = @"root\cimv2";

    public InventoryCollectorWin(ILogger<InventoryCollectorWin> logger)
        : base(logger)
    {
    }

    protected override void CollectCore(DeviceInventory inventory, CancellationToken cancellationToken)
    {
        var chassisCodes = new List<int>();
        var isServerOs = false;

        Run(inventory, "Identificação", () => CollectIdentification(inventory, chassisCodes));
        cancellationToken.ThrowIfCancellationRequested();

        Run(inventory, "Sistema operacional", () => isServerOs = CollectOperatingSystem(inventory));
        Run(inventory, "BIOS", () => CollectBios(inventory));
        Run(inventory, "Placa-mãe", () => CollectMotherboard(inventory));
        cancellationToken.ThrowIfCancellationRequested();

        Run(inventory, "Processadores", () => CollectProcessors(inventory));
        Run(inventory, "Memória", () => CollectMemory(inventory));
        Run(inventory, "Discos", () => CollectDisks(inventory));
        Run(inventory, "Volumes", () => CollectVolumes(inventory));
        cancellationToken.ThrowIfCancellationRequested();

        Run(inventory, "Placas de vídeo", () => CollectVideo(inventory));
        Run(inventory, "Monitores", () => CollectMonitors(inventory));
        Run(inventory, "Placas de som", () => CollectSound(inventory));
        Run(inventory, "Controladoras", () => CollectControllers(inventory));
        Run(inventory, "Bateria", () => CollectBatteries(inventory));
        Run(inventory, "TPM", () => CollectTpm(inventory));
        cancellationToken.ThrowIfCancellationRequested();

        Run(inventory, "Adaptadores de rede", () => CollectNetworkAdapters(inventory));
        Run(inventory, "Antivírus", () => CollectAntivirus(inventory, isServerOs));
        Run(inventory, "Softwares", () => CollectSoftware(inventory));

        inventory.Hypervisor ??= DetectHypervisor(inventory.Manufacturer, inventory.Model, inventory.Bios?.Manufacturer, inventory.Bios?.Version);
        inventory.DeviceType = ClassifyDeviceType(chassisCodes, isServerOs, inventory.Hypervisor, inventory.Model);
        if (chassisCodes.Count > 0)
        {
            inventory.ChassisType = string.Join(", ", chassisCodes.Distinct().Select(ChassisName));
        }
    }

    // ---------------------------------------------------------------------
    // Identificação
    // ---------------------------------------------------------------------

    private void CollectIdentification(DeviceInventory inventory, List<int> chassisCodes)
    {
        var product = Query("SELECT UUID, IdentifyingNumber, Vendor, Name, SKUNumber, Version FROM Win32_ComputerSystemProduct").FirstOrDefault();
        var system = Query("SELECT Manufacturer, Model, Domain, PartOfDomain, Workgroup, SystemSKUNumber, PCSystemType FROM Win32_ComputerSystem").FirstOrDefault();
        var enclosure = Query("SELECT ChassisTypes, SerialNumber, SMBIOSAssetTag FROM Win32_SystemEnclosure").FirstOrDefault();

        inventory.Uuid = Clean(Get(product, "UUID"));
        inventory.Manufacturer = Clean(Get(system, "Manufacturer")) ?? Clean(Get(product, "Vendor"));
        inventory.Model = Clean(Get(system, "Model")) ?? Clean(Get(product, "Name"));

        // Lenovo guarda o nome comercial ("ThinkPad T14 Gen 2") em Version e um código em Model.
        var productVersion = Clean(Get(product, "Version"));
        if (inventory.Manufacturer?.Contains("lenovo", StringComparison.OrdinalIgnoreCase) == true &&
            productVersion is not null &&
            !productVersion.Equals(inventory.Model, StringComparison.OrdinalIgnoreCase))
        {
            inventory.Model = $"{productVersion} ({inventory.Model})";
        }

        inventory.SerialNumber = Clean(Get(product, "IdentifyingNumber")) ?? Clean(Get(enclosure, "SerialNumber"));
        inventory.Sku = Clean(Get(system, "SystemSKUNumber")) ?? Clean(Get(product, "SKUNumber"));
        inventory.FirmwareAssetTag = Clean(Get(enclosure, "SMBIOSAssetTag"));

        var partOfDomain = ToBool(Get(system, "PartOfDomain"));
        inventory.PartOfDomain = partOfDomain;
        if (partOfDomain == true)
        {
            inventory.Domain = Clean(Get(system, "Domain"));
        }
        else
        {
            inventory.Workgroup = Clean(Get(system, "Workgroup")) ?? Clean(Get(system, "Domain"));
        }

        if (Get(enclosure, "ChassisTypes") is IEnumerable codes and not string)
        {
            foreach (var code in codes)
            {
                if (ToInt(code) is { } value)
                {
                    chassisCodes.Add(value);
                }
            }
        }

        // PCSystemType 2 = portátil (quando o gabinete não informa nada útil).
        if (chassisCodes.All(x => x is 1 or 2) && ToInt(Get(system, "PCSystemType")) == 2)
        {
            chassisCodes.Add(10);
        }
    }

    // ---------------------------------------------------------------------
    // Sistema operacional
    // ---------------------------------------------------------------------

    private bool CollectOperatingSystem(DeviceInventory inventory)
    {
        var os = Query("SELECT Caption, Version, BuildNumber, OSArchitecture, SerialNumber, InstallDate, LastBootUpTime, ProductType FROM Win32_OperatingSystem").FirstOrDefault();

        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");

        var build = Clean(Get(os, "BuildNumber")) ?? Environment.OSVersion.Version.Build.ToString(CultureInfo.InvariantCulture);
        var ubr = key?.GetValue("UBR");
        var caption = Clean(Get(os, "Caption")) ?? Clean(key?.GetValue("ProductName"));

        // O registro e algumas versões do WMI ainda dizem "Windows 10" no Windows 11.
        if (caption is not null &&
            int.TryParse(build, NumberStyles.Integer, CultureInfo.InvariantCulture, out var buildNumber) &&
            buildNumber >= 22000)
        {
            caption = caption.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);
        }

        var productType = ToInt(Get(os, "ProductType"));

        inventory.OperatingSystem = new InventoryOperatingSystem
        {
            Name = caption,
            Edition = Clean(key?.GetValue("EditionID")),
            Version = Clean(Get(os, "Version")),
            Build = ubr is null ? build : $"{build}.{ToLong(ubr)}",
            DisplayVersion = Clean(key?.GetValue("DisplayVersion")) ?? Clean(key?.GetValue("ReleaseId")),
            Architecture = Clean(Get(os, "OSArchitecture")) ?? GetArchitectureName(RuntimeInformation.OSArchitecture),
            SerialNumber = Clean(Get(os, "SerialNumber")),
            InstallDate = ToDate(Get(os, "InstallDate")),
            LastBoot = ToDate(Get(os, "LastBootUpTime")),
            Language = CultureInfo.InstalledUICulture.Name,
            TimeZone = TimeZoneInfo.Local.DisplayName
        };

        Run(inventory, "Licença do Windows", () =>
        {
            // ApplicationID do Windows (exclui Office e outros produtos).
            var license = Query(
                "SELECT LicenseStatus FROM SoftwareLicensingProduct " +
                "WHERE ApplicationID = '55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL")
                .FirstOrDefault();

            inventory.OperatingSystem!.LicenseStatus = ToInt(Get(license, "LicenseStatus")) switch
            {
                0 => "Não licenciado",
                1 => "Ativado",
                2 => "Período de carência (OOB)",
                3 => "Período de carência (OOT)",
                4 => "Não original",
                5 => "Notificação",
                6 => "Carência estendida",
                _ => null
            };
        });

        // ProductType: 1 = estação de trabalho, 2 = controlador de domínio, 3 = servidor.
        return productType is 2 or 3;
    }

    // ---------------------------------------------------------------------
    // Firmware e placa-mãe
    // ---------------------------------------------------------------------

    private void CollectBios(DeviceInventory inventory)
    {
        var bios = Query("SELECT Manufacturer, SMBIOSBIOSVersion, Version, ReleaseDate, SerialNumber, SMBIOSMajorVersion, SMBIOSMinorVersion FROM Win32_BIOS").FirstOrDefault();

        var major = ToInt(Get(bios, "SMBIOSMajorVersion"));
        var minor = ToInt(Get(bios, "SMBIOSMinorVersion"));

        inventory.Bios = new InventoryBios
        {
            Manufacturer = Clean(Get(bios, "Manufacturer")),
            Version = Clean(Get(bios, "SMBIOSBIOSVersion")) ?? Clean(Get(bios, "Version")),
            ReleaseDate = ToDate(Get(bios, "ReleaseDate")),
            SerialNumber = Clean(Get(bios, "SerialNumber")),
            SmbiosVersion = major is null ? null : $"{major}.{minor ?? 0}"
        };

        using (var control = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control"))
        {
            inventory.Bios.Mode = ToInt(control?.GetValue("PEFirmwareType")) switch
            {
                1 => "Legado (BIOS)",
                2 => "UEFI",
                _ => null
            };
        }

        using (var secureBoot = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State"))
        {
            var value = secureBoot?.GetValue("UEFISecureBootEnabled");
            inventory.Bios.SecureBoot = value is null ? null : ToBool(value);
        }

        inventory.SerialNumber ??= inventory.Bios.SerialNumber;
    }

    private void CollectMotherboard(DeviceInventory inventory)
    {
        var board = Query("SELECT Manufacturer, Product, Version, SerialNumber FROM Win32_BaseBoard").FirstOrDefault();
        if (board is null)
        {
            return;
        }

        inventory.Motherboard = new InventoryMotherboard
        {
            Manufacturer = Clean(Get(board, "Manufacturer")),
            Product = Clean(Get(board, "Product")),
            Version = Clean(Get(board, "Version")),
            SerialNumber = Clean(Get(board, "SerialNumber"))
        };
    }

    // ---------------------------------------------------------------------
    // Componentes
    // ---------------------------------------------------------------------

    private void CollectProcessors(DeviceInventory inventory)
    {
        foreach (var cpu in Query("SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, SocketDesignation, Architecture FROM Win32_Processor"))
        {
            inventory.Processors.Add(new InventoryProcessor
            {
                Name = Clean(Get(cpu, "Name")),
                Manufacturer = Clean(Get(cpu, "Manufacturer")) switch
                {
                    "GenuineIntel" => "Intel",
                    "AuthenticAMD" => "AMD",
                    var vendor => vendor
                },
                Cores = ToInt(Get(cpu, "NumberOfCores")),
                Threads = ToInt(Get(cpu, "NumberOfLogicalProcessors")),
                MaxClockMhz = ToInt(Get(cpu, "MaxClockSpeed")),
                Socket = Clean(Get(cpu, "SocketDesignation")),
                Architecture = ToInt(Get(cpu, "Architecture")) switch
                {
                    0 => "x86",
                    5 => "ARM",
                    9 => "x64",
                    12 => "ARM64",
                    _ => null
                }
            });
        }
    }

    private void CollectMemory(DeviceInventory inventory)
    {
        foreach (var module in Query("SELECT DeviceLocator, BankLabel, Capacity, ConfiguredClockSpeed, Speed, SMBIOSMemoryType, FormFactor, Manufacturer, PartNumber, SerialNumber FROM Win32_PhysicalMemory"))
        {
            var speed = ToInt(Get(module, "ConfiguredClockSpeed"));
            if (speed is null or 0)
            {
                speed = ToInt(Get(module, "Speed"));
            }

            inventory.Memory.Add(new InventoryMemoryModule
            {
                Slot = Clean(Get(module, "DeviceLocator")) ?? Clean(Get(module, "BankLabel")),
                CapacityBytes = ToLong(Get(module, "Capacity")),
                SpeedMhz = speed,
                Type = ToInt(Get(module, "SMBIOSMemoryType")) switch
                {
                    18 => "DDR",
                    19 => "DDR2",
                    20 => "DDR",
                    21 => "DDR2",
                    22 => "DDR2 FB-DIMM",
                    24 => "DDR3",
                    26 => "DDR4",
                    27 => "LPDDR",
                    28 => "LPDDR2",
                    29 => "LPDDR3",
                    30 => "LPDDR4",
                    34 => "DDR5",
                    35 => "LPDDR5",
                    _ => null
                },
                FormFactor = ToInt(Get(module, "FormFactor")) switch
                {
                    8 => "DIMM",
                    12 => "SODIMM",
                    13 => "SRIMM",
                    _ => null
                },
                Manufacturer = Clean(Get(module, "Manufacturer")),
                PartNumber = Clean(Get(module, "PartNumber")),
                SerialNumber = Clean(Get(module, "SerialNumber"))
            });
        }
    }

    private void CollectDisks(DeviceInventory inventory)
    {
        // MSFT_PhysicalDisk informa se é SSD/HD, NVMe/SATA e a saúde do disco.
        var physical = Query(
            "SELECT FriendlyName, Model, SerialNumber, FirmwareVersion, Size, MediaType, BusType, HealthStatus FROM MSFT_PhysicalDisk",
            @"root\Microsoft\Windows\Storage",
            reportErrors: false);

        foreach (var disk in physical)
        {
            inventory.Disks.Add(new InventoryDisk
            {
                Model = Clean(Get(disk, "FriendlyName")) ?? Clean(Get(disk, "Model")),
                SerialNumber = Clean(Get(disk, "SerialNumber")),
                Firmware = Clean(Get(disk, "FirmwareVersion")),
                SizeBytes = ToLong(Get(disk, "Size")),
                MediaType = ToInt(Get(disk, "MediaType")) switch
                {
                    3 => "HD",
                    4 => "SSD",
                    5 => "SCM",
                    _ => null
                },
                Interface = ToInt(Get(disk, "BusType")) switch
                {
                    1 => "SCSI",
                    2 => "ATAPI",
                    3 => "ATA",
                    4 => "IEEE 1394",
                    6 => "Fibre Channel",
                    7 => "USB",
                    8 => "RAID",
                    9 => "iSCSI",
                    10 => "SAS",
                    11 => "SATA",
                    12 => "SD",
                    13 => "MMC",
                    15 => "Virtual (arquivo)",
                    16 => "Espaços de armazenamento",
                    17 => "NVMe",
                    _ => null
                },
                Health = ToInt(Get(disk, "HealthStatus")) switch
                {
                    0 => "Saudável",
                    1 => "Atenção",
                    2 => "Com defeito",
                    _ => null
                }
            });
        }

        // Complementa (ou substitui, em sistemas antigos) com Win32_DiskDrive.
        var drives = Query("SELECT Model, Manufacturer, SerialNumber, FirmwareRevision, Size, InterfaceType, MediaType FROM Win32_DiskDrive");
        foreach (var drive in drives)
        {
            var serial = Clean(Get(drive, "SerialNumber"));
            var model = Clean(Get(drive, "Model"));
            var existing = inventory.Disks.FirstOrDefault(x =>
                (serial is not null && string.Equals(x.SerialNumber, serial, StringComparison.OrdinalIgnoreCase)) ||
                (model is not null && string.Equals(x.Model, model, StringComparison.OrdinalIgnoreCase)));

            if (existing is not null)
            {
                existing.Manufacturer ??= CleanDiskManufacturer(Clean(Get(drive, "Manufacturer")));
                existing.Firmware ??= Clean(Get(drive, "FirmwareRevision"));
                continue;
            }

            if (physical.Count > 0)
            {
                continue;
            }

            var mediaType = Clean(Get(drive, "MediaType"));
            inventory.Disks.Add(new InventoryDisk
            {
                Model = model,
                Manufacturer = CleanDiskManufacturer(Clean(Get(drive, "Manufacturer"))),
                SerialNumber = serial,
                Firmware = Clean(Get(drive, "FirmwareRevision")),
                SizeBytes = ToLong(Get(drive, "Size")),
                Interface = Clean(Get(drive, "InterfaceType")),
                MediaType = mediaType?.Contains("Removable", StringComparison.OrdinalIgnoreCase) == true ? "Removível" : null
            });
        }
    }

    private static string? CleanDiskManufacturer(string? manufacturer) =>
        manufacturer?.StartsWith("(Standard", StringComparison.OrdinalIgnoreCase) == true ||
        manufacturer?.StartsWith("(Unidades", StringComparison.OrdinalIgnoreCase) == true
            ? null
            : manufacturer;

    private void CollectVolumes(DeviceInventory inventory)
    {
        var bitLocker = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var volume in Query(
            "SELECT DriveLetter, ProtectionStatus, ConversionStatus FROM Win32_EncryptableVolume",
            @"root\cimv2\Security\MicrosoftVolumeEncryption",
            reportErrors: false))
        {
            var letter = Clean(Get(volume, "DriveLetter"));
            if (letter is null)
            {
                continue;
            }

            bitLocker[letter] = (ToInt(Get(volume, "ProtectionStatus")), ToInt(Get(volume, "ConversionStatus"))) switch
            {
                (1, _) => "BitLocker ativo",
                (_, 2) => "BitLocker criptografando",
                (_, 3) => "BitLocker descriptografando",
                (0, 1) => "BitLocker suspenso",
                (0, _) => "Não criptografado",
                _ => "Desconhecido"
            };
        }

        foreach (var disk in Query("SELECT DeviceID, VolumeName, FileSystem, Size, FreeSpace, DriveType, ProviderName FROM Win32_LogicalDisk"))
        {
            var letter = Clean(Get(disk, "DeviceID"));
            var driveType = ToInt(Get(disk, "DriveType"));
            if (letter is null || driveType == 5 && ToLong(Get(disk, "Size")) is null)
            {
                // Leitor de CD/DVD vazio.
                continue;
            }

            inventory.Volumes.Add(new InventoryVolume
            {
                MountPoint = letter,
                Label = Clean(Get(disk, "VolumeName")),
                FileSystem = Clean(Get(disk, "FileSystem")),
                TotalBytes = ToLong(Get(disk, "Size")),
                FreeBytes = ToLong(Get(disk, "FreeSpace")),
                Device = Clean(Get(disk, "ProviderName")),
                Type = driveType switch
                {
                    2 => "Removível",
                    3 => "Local",
                    4 => "Rede",
                    5 => "CD/DVD",
                    6 => "Memória",
                    _ => "Outro"
                },
                Encryption = bitLocker.TryGetValue(letter, out var status) ? status : null
            });
        }

        if (inventory.Volumes.Count == 0)
        {
            inventory.Volumes = CollectVolumesFromDriveInfo();
        }
    }

    private void CollectVideo(DeviceInventory inventory)
    {
        foreach (var video in Query("SELECT Name, AdapterCompatibility, DriverVersion, DriverDate, AdapterRAM, CurrentHorizontalResolution, CurrentVerticalResolution, VideoProcessor, PNPDeviceID FROM Win32_VideoController"))
        {
            var name = Clean(Get(video, "Name"));
            if (name is null || name.Contains("Remote Display", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) && inventory.Components.Any(x => x.Category == "Placa de vídeo"))
            {
                continue;
            }

            var component = new InventoryComponent
            {
                Category = "Placa de vídeo",
                Name = name,
                Manufacturer = Clean(Get(video, "AdapterCompatibility")),
                Version = Clean(Get(video, "DriverVersion"))
            };

            var memory = GetVideoMemory(Clean(Get(video, "PNPDeviceID"))) ?? ToLong(Get(video, "AdapterRAM"));
            if (memory is > 0)
            {
                component.Details["Memória"] = InventoryFormat.Bytes(memory.Value);
            }

            var width = ToInt(Get(video, "CurrentHorizontalResolution"));
            var height = ToInt(Get(video, "CurrentVerticalResolution"));
            if (width is > 0 && height is > 0)
            {
                component.Details["Resolução"] = $"{width} x {height}";
            }

            if (ToDate(Get(video, "DriverDate")) is { } driverDate)
            {
                component.Details["Data do driver"] = driverDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            }

            inventory.Components.Add(component);
        }
    }

    /// <summary>
    /// AdapterRAM do WMI tem 32 bits (no máximo 4 GB). O valor real fica no registro do driver.
    /// </summary>
    private static long? GetVideoMemory(string? pnpDeviceId)
    {
        if (string.IsNullOrWhiteSpace(pnpDeviceId))
        {
            return null;
        }

        try
        {
            using var classKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (classKey is null)
            {
                return null;
            }

            foreach (var subKeyName in classKey.GetSubKeyNames())
            {
                using var subKey = classKey.OpenSubKey(subKeyName);
                var matchingDeviceId = subKey?.GetValue("MatchingDeviceId") as string;
                if (string.IsNullOrWhiteSpace(matchingDeviceId) ||
                    !pnpDeviceId.StartsWith(matchingDeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = subKey?.GetValue("HardwareInformation.qwMemorySize") ??
                    subKey?.GetValue("HardwareInformation.MemorySize");

                return value switch
                {
                    long l => l,
                    int i => (uint)i,
                    byte[] bytes when bytes.Length >= 8 => BitConverter.ToInt64(bytes, 0),
                    byte[] bytes when bytes.Length >= 4 => BitConverter.ToUInt32(bytes, 0),
                    _ => null
                };
            }
        }
        catch
        {
            // Sem acesso ao registro do driver.
        }

        return null;
    }

    private void CollectMonitors(DeviceInventory inventory)
    {
        foreach (var monitor in Query(
            "SELECT ManufacturerName, UserFriendlyName, SerialNumberID, YearOfManufacture, ProductCodeID FROM WmiMonitorID",
            @"root\wmi",
            reportErrors: false))
        {
            var name = DecodeWmiString(Get(monitor, "UserFriendlyName"));
            var manufacturerCode = DecodeWmiString(Get(monitor, "ManufacturerName"));
            var component = new InventoryComponent
            {
                Category = "Monitor",
                Name = name ?? DecodeWmiString(Get(monitor, "ProductCodeID")),
                Manufacturer = MonitorManufacturer(manufacturerCode),
                SerialNumber = Clean(DecodeWmiString(Get(monitor, "SerialNumberID")))
            };

            if (ToInt(Get(monitor, "YearOfManufacture")) is > 1990 and var year)
            {
                component.Details["Ano de fabricação"] = year.ToString(CultureInfo.InvariantCulture);
            }

            if (component.Name is not null || component.SerialNumber is not null)
            {
                inventory.Components.Add(component);
            }
        }
    }

    private static string? DecodeWmiString(object? value)
    {
        if (value is not IEnumerable values || value is string)
        {
            return Clean(value);
        }

        var builder = new StringBuilder();
        foreach (var item in values)
        {
            var code = ToInt(item);
            if (code is null or 0)
            {
                break;
            }
            builder.Append((char)code.Value);
        }
        return Clean(builder.ToString());
    }

    private static string? MonitorManufacturer(string? pnpCode) => pnpCode?.ToUpperInvariant() switch
    {
        null => null,
        "ACR" => "Acer",
        "AOC" => "AOC",
        "AUO" => "AU Optronics",
        "BNQ" => "BenQ",
        "BOE" => "BOE",
        "CMN" => "Chimei Innolux",
        "DEL" => "Dell",
        "ENC" => "Eizo",
        "GSM" => "LG",
        "HPN" or "HWP" => "HP",
        "IVM" => "Iiyama",
        "LEN" => "Lenovo",
        "LGD" => "LG Display",
        "MSI" => "MSI",
        "NEC" => "NEC",
        "PHL" => "Philips",
        "SAM" or "SEC" => "Samsung",
        "SDC" => "Samsung Display",
        "SHP" => "Sharp",
        "SNY" => "Sony",
        "VSC" => "ViewSonic",
        "ASU" or "AUS" => "ASUS",
        "POS" => "Positivo",
        _ => pnpCode
    };

    private void CollectSound(DeviceInventory inventory)
    {
        foreach (var sound in Query("SELECT Name, Manufacturer FROM Win32_SoundDevice"))
        {
            var name = Clean(Get(sound, "Name"));
            if (name is null)
            {
                continue;
            }

            inventory.Components.Add(new InventoryComponent
            {
                Category = "Placa de som",
                Name = name,
                Manufacturer = CleanDiskManufacturer(Clean(Get(sound, "Manufacturer")))
            });
        }
    }

    private void CollectControllers(DeviceInventory inventory)
    {
        void Add(string wmiClass, string type)
        {
            foreach (var controller in Query($"SELECT Name, Manufacturer FROM {wmiClass}"))
            {
                var name = Clean(Get(controller, "Name"));
                if (name is null)
                {
                    continue;
                }

                var component = new InventoryComponent
                {
                    Category = "Controladora",
                    Name = name,
                    Manufacturer = CleanDiskManufacturer(Clean(Get(controller, "Manufacturer")))
                };
                component.Details["Tipo"] = type;
                inventory.Components.Add(component);
            }
        }

        Add("Win32_USBController", "USB");
        Add("Win32_SCSIController", "Armazenamento (SCSI/NVMe/RAID)");
        Add("Win32_IDEController", "Armazenamento (IDE/SATA)");
    }

    private void CollectBatteries(DeviceInventory inventory)
    {
        var staticData = Query(
            "SELECT DeviceName, ManufactureName, SerialNumber, DesignedCapacity, Chemistry FROM BatteryStaticData",
            @"root\wmi",
            reportErrors: false);
        var fullCharge = Query(
            "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity",
            @"root\wmi",
            reportErrors: false);

        if (staticData.Count == 0)
        {
            foreach (var battery in Query("SELECT Name, DeviceID, EstimatedChargeRemaining FROM Win32_Battery"))
            {
                var component = new InventoryComponent
                {
                    Category = "Bateria",
                    Name = Clean(Get(battery, "Name")) ?? Clean(Get(battery, "DeviceID")) ?? "Bateria"
                };
                if (ToInt(Get(battery, "EstimatedChargeRemaining")) is { } charge)
                {
                    component.Details["Carga"] = $"{charge}%";
                }
                inventory.Components.Add(component);
            }
            return;
        }

        for (var i = 0; i < staticData.Count; i++)
        {
            var battery = staticData[i];
            var component = new InventoryComponent
            {
                Category = "Bateria",
                Name = Clean(Get(battery, "DeviceName")) ?? "Bateria",
                Manufacturer = Clean(Get(battery, "ManufactureName")),
                SerialNumber = Clean(Get(battery, "SerialNumber"))
            };

            var designed = ToLong(Get(battery, "DesignedCapacity"));
            var full = i < fullCharge.Count ? ToLong(Get(fullCharge[i], "FullChargedCapacity")) : null;

            if (designed is > 0)
            {
                component.Details["Capacidade de projeto"] = $"{designed.Value / 1000.0:0.#} Wh";
            }
            if (full is > 0)
            {
                component.Details["Capacidade atual"] = $"{full.Value / 1000.0:0.#} Wh";
            }
            if (designed is > 0 && full is > 0)
            {
                component.Details["Saúde"] = $"{Math.Min(100, 100.0 * full.Value / designed.Value):0}%";
            }

            inventory.Components.Add(component);
        }
    }

    private void CollectTpm(DeviceInventory inventory)
    {
        var tpm = Query(
            "SELECT ManufacturerIdTxt, ManufacturerVersion, SpecVersion, IsEnabled_InitialValue, IsActivated_InitialValue FROM Win32_Tpm",
            @"root\cimv2\Security\MicrosoftTpm",
            reportErrors: false).FirstOrDefault();

        if (tpm is null)
        {
            return;
        }

        var component = new InventoryComponent
        {
            Category = "TPM",
            Name = "Trusted Platform Module",
            Manufacturer = Clean(Get(tpm, "ManufacturerIdTxt")),
            Version = Clean(Get(tpm, "ManufacturerVersion"))
        };

        var spec = Clean(Get(tpm, "SpecVersion"));
        if (spec is not null)
        {
            component.Details["Especificação"] = spec.Split(',')[0].Trim();
        }
        if (ToBool(Get(tpm, "IsEnabled_InitialValue")) is { } enabled)
        {
            component.Details["Habilitado"] = enabled ? "Sim" : "Não";
        }

        inventory.Components.Add(component);
    }

    // ---------------------------------------------------------------------
    // Rede
    // ---------------------------------------------------------------------

    private void CollectNetworkAdapters(DeviceInventory inventory)
    {
        // O .NET informa IPs e status; o WMI completa com fabricante e se é físico.
        foreach (var adapter in Query("SELECT Name, Description, Manufacturer, MACAddress, PhysicalAdapter, NetConnectionID, NetEnabled FROM Win32_NetworkAdapter WHERE MACAddress IS NOT NULL"))
        {
            var physical = ToBool(Get(adapter, "PhysicalAdapter"));
            var description = Clean(Get(adapter, "Description")) ?? Clean(Get(adapter, "Name"));

            inventory.NetworkPorts.Add(new InventoryNetworkPort
            {
                Name = Clean(Get(adapter, "NetConnectionID")) ?? description,
                Description = description,
                Manufacturer = CleanDiskManufacturer(Clean(Get(adapter, "Manufacturer"))),
                MacAddress = FormatMac(Clean(Get(adapter, "MACAddress"))),
                Virtual = physical == false || LooksVirtual(description),
                Connected = ToBool(Get(adapter, "NetEnabled"))
            });
        }

        foreach (var config in Query("SELECT MACAddress, DHCPEnabled FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE"))
        {
            var mac = FormatMac(Clean(Get(config, "MACAddress")));
            foreach (var port in inventory.NetworkPorts.Where(x => x.MacAddress == mac))
            {
                port.Dhcp = ToBool(Get(config, "DHCPEnabled"));
            }
        }
    }

    // ---------------------------------------------------------------------
    // Antivírus
    // ---------------------------------------------------------------------

    private void CollectAntivirus(DeviceInventory inventory, bool isServerOs)
    {
        // Central de Segurança (só existe nas edições de estação de trabalho).
        if (!isServerOs)
        {
            foreach (var product in Query(
                "SELECT displayName, productState, pathToSignedProductExe FROM AntiVirusProduct",
                @"root\SecurityCenter2",
                reportErrors: false))
            {
                var name = Clean(Get(product, "displayName"));
                if (name is null)
                {
                    continue;
                }

                var state = ToLong(Get(product, "productState")) ?? 0;
                // productState: byte 2 = ativo (0x10 ou 0x11), byte 3 = 0x00 atualizado / 0x10 desatualizado.
                var enabledFlag = (state >> 12) & 0xF;
                var outdatedFlag = (state >> 4) & 0xF;

                inventory.Antivirus.Add(new InventoryAntivirus
                {
                    Name = name,
                    Manufacturer = AntivirusVendor(name),
                    Enabled = enabledFlag == 1,
                    UpToDate = outdatedFlag == 0,
                    Version = GetFileVersion(Clean(Get(product, "pathToSignedProductExe")))
                });
            }
        }

        // Microsoft Defender: versão e assinaturas (também no Windows Server).
        var defender = Query(
            "SELECT AMProductVersion, AntivirusEnabled, AntivirusSignatureVersion, AntivirusSignatureAge, RealTimeProtectionEnabled FROM MSFT_MpComputerStatus",
            @"root\Microsoft\Windows\Defender",
            reportErrors: false).FirstOrDefault();

        if (defender is null)
        {
            return;
        }

        var defenderEntry = inventory.Antivirus.FirstOrDefault(x =>
            x.Name.Contains("Defender", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Contains("Windows Security", StringComparison.OrdinalIgnoreCase) ||
            x.Name.Contains("Segurança do Windows", StringComparison.OrdinalIgnoreCase));

        var antivirusEnabled = ToBool(Get(defender, "AntivirusEnabled")) == true &&
            ToBool(Get(defender, "RealTimeProtectionEnabled")) != false;

        if (defenderEntry is null)
        {
            // Com outro antivírus ativo, o Defender fica em modo passivo; só lista se estiver ligado.
            if (!antivirusEnabled && inventory.Antivirus.Count > 0)
            {
                return;
            }

            defenderEntry = new InventoryAntivirus { Name = "Microsoft Defender Antivirus" };
            inventory.Antivirus.Add(defenderEntry);
            defenderEntry.Enabled = antivirusEnabled;
        }

        defenderEntry.Manufacturer = "Microsoft";
        defenderEntry.Version = Clean(Get(defender, "AMProductVersion")) ?? defenderEntry.Version;
        defenderEntry.SignatureVersion = Clean(Get(defender, "AntivirusSignatureVersion"));
        if (ToInt(Get(defender, "AntivirusSignatureAge")) is { } age)
        {
            defenderEntry.UpToDate ??= age <= 3;
            if (isServerOs)
            {
                defenderEntry.UpToDate = age <= 3;
            }
        }
    }

    private static string? AntivirusVendor(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("defender") || lower.Contains("windows")) return "Microsoft";
        if (lower.Contains("kaspersky")) return "Kaspersky";
        if (lower.Contains("eset")) return "ESET";
        if (lower.Contains("sophos")) return "Sophos";
        if (lower.Contains("trend")) return "Trend Micro";
        if (lower.Contains("bitdefender")) return "Bitdefender";
        if (lower.Contains("mcafee") || lower.Contains("trellix")) return "Trellix (McAfee)";
        if (lower.Contains("symantec") || lower.Contains("norton")) return "Gen Digital (Symantec/Norton)";
        if (lower.Contains("avast") || lower.Contains("avg")) return "Gen Digital (Avast/AVG)";
        if (lower.Contains("crowdstrike") || lower.Contains("falcon")) return "CrowdStrike";
        if (lower.Contains("sentinel")) return "SentinelOne";
        if (lower.Contains("cortex")) return "Palo Alto Networks";
        if (lower.Contains("panda") || lower.Contains("watchguard")) return "WatchGuard (Panda)";
        return null;
    }

    private static string? GetFileVersion(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            path = Environment.ExpandEnvironmentVariables(path.Trim('"'));
            if (!System.IO.File.Exists(path))
            {
                return null;
            }
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            return Clean(info.ProductVersion) ?? Clean(info.FileVersion);
        }
        catch
        {
            return null;
        }
    }

    // ---------------------------------------------------------------------
    // Softwares
    // ---------------------------------------------------------------------

    private void CollectSoftware(DeviceInventory inventory)
    {
        const string uninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        void ReadUninstallKey(RegistryKey? root, string? architecture)
        {
            using var uninstall = root?.OpenSubKey(uninstallPath);
            if (uninstall is null)
            {
                return;
            }

            foreach (var subKeyName in uninstall.GetSubKeyNames())
            {
                try
                {
                    using var app = uninstall.OpenSubKey(subKeyName);
                    if (app is null)
                    {
                        continue;
                    }

                    var name = Clean(app.GetValue("DisplayName"));
                    if (name is null ||
                        ToInt(app.GetValue("SystemComponent")) == 1 ||
                        app.GetValue("ParentKeyName") is not null ||
                        app.GetValue("ReleaseType") is string releaseType &&
                            (releaseType.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
                             releaseType.Contains("Hotfix", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var sizeKb = ToLong(app.GetValue("EstimatedSize"));

                    inventory.Software.Add(new InventorySoftware
                    {
                        Name = name,
                        Version = Clean(app.GetValue("DisplayVersion")),
                        Publisher = Clean(app.GetValue("Publisher")),
                        InstallDate = ParseInstallDate(app.GetValue("InstallDate") as string),
                        Architecture = architecture,
                        SizeBytes = sizeKb is > 0 ? sizeKb * 1024 : null,
                        Source = "Registro"
                    });
                }
                catch
                {
                    // Chave sem permissão ou removida durante a leitura.
                }
            }
        }

        using (var hklm64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
        {
            ReadUninstallKey(hklm64, Environment.Is64BitOperatingSystem ? "64 bits" : "32 bits");
        }

        if (Environment.Is64BitOperatingSystem)
        {
            // A visão de 32 bits aponta para SOFTWARE\WOW6432Node.
            using var hklm32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            ReadUninstallKey(hklm32, "32 bits");
        }

        // Softwares instalados só para o usuário (perfis carregados).
        using var users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64);
        foreach (var sid in users.GetSubKeyNames())
        {
            if (!sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) ||
                sid.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var userKey = users.OpenSubKey(sid);
            ReadUninstallKey(userKey, "Usuário");
        }
    }

    private static DateTimeOffset? ParseInstallDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        string[] formats = ["yyyyMMdd", "yyyy-MM-dd", "MM/dd/yyyy", "dd/MM/yyyy", "M/d/yyyy"];
        if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date) &&
            date.Year > 1990 && date <= DateTime.Now.AddDays(1))
        {
            return new DateTimeOffset(date);
        }
        return null;
    }

    // ---------------------------------------------------------------------
    // WMI
    // ---------------------------------------------------------------------

    private List<Dictionary<string, object?>> Query(string wql, string scope = Cimv2, bool reportErrors = true)
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
        catch (Exception ex)
        {
            if (reportErrors)
            {
                _logger.LogWarning(ex, "Inventory: WMI query failed ({scope}): {query}", scope, wql);
            }
            else
            {
                _logger.LogDebug(ex, "Inventory: optional WMI query failed ({scope}): {query}", scope, wql);
            }
        }

        return results;
    }

    private static object? Get(Dictionary<string, object?>? values, string name) =>
        values is not null && values.TryGetValue(name, out var value) ? value : null;

    private static DateTimeOffset? ToDate(object? value)
    {
        var text = value as string;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            // Formato DMTF do WMI: 20240115000000.000000-180
            var date = ManagementDateTimeConverter.ToDateTime(text);
            return date.Year < 1980 ? null : new DateTimeOffset(date);
        }
        catch
        {
            return null;
        }
    }
}
