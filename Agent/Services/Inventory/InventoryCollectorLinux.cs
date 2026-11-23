using Microsoft.Extensions.Logging;
using Remotely.Shared.Models.Inventory;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace Remotely.Agent.Services.Inventory;

/// <summary>
/// Inventário do Linux via /sys/class/dmi, /proc, lsblk, lspci, dmidecode e dpkg/rpm.
/// O agente roda como root, então os números de série do DMI ficam acessíveis.
/// </summary>
public class InventoryCollectorLinux : InventoryCollectorBase
{
    private const string DmiPath = "/sys/class/dmi/id";

    public InventoryCollectorLinux(ILogger<InventoryCollectorLinux> logger)
        : base(logger)
    {
    }

    protected override void CollectCore(DeviceInventory inventory, CancellationToken cancellationToken)
    {
        var chassisCodes = new List<int>();

        Run(inventory, "Identificação", () => CollectIdentification(inventory, chassisCodes));
        Run(inventory, "Sistema operacional", () => CollectOperatingSystem(inventory));
        Run(inventory, "BIOS", () => CollectBios(inventory));
        cancellationToken.ThrowIfCancellationRequested();

        Run(inventory, "Processadores", () => CollectProcessors(inventory));
        Run(inventory, "Memória", () => CollectMemory(inventory));
        Run(inventory, "Discos", () => CollectDisks(inventory));
        Run(inventory, "Volumes", () => inventory.Volumes = CollectVolumesFromDriveInfo());
        cancellationToken.ThrowIfCancellationRequested();

        Run(inventory, "Bateria", () => CollectBatteries(inventory));
        Run(inventory, "SMBIOS", () =>
        {
            const string table = "/sys/firmware/dmi/tables/DMI";
            ApplySmbios(inventory, File.Exists(table) ? File.ReadAllBytes(table) : null);
        });
        Run(inventory, "Dispositivos PCI", () => CollectPciDevices(inventory));
        Run(inventory, "Softwares", () => CollectSoftware(inventory));
        Run(inventory, "Antivírus", () => CollectAntivirus(inventory));

        inventory.Hypervisor ??= DetectHypervisor(inventory.Manufacturer, inventory.Model, inventory.Bios?.Manufacturer);
        inventory.DeviceType = ClassifyDeviceType(chassisCodes, isServerOs: false, inventory.Hypervisor, inventory.Model);
        if (chassisCodes.Count > 0)
        {
            inventory.ChassisType = string.Join(", ", chassisCodes.Select(ChassisName));
        }
    }

    private static string? Dmi(string name) => Clean(ReadFile(Path.Combine(DmiPath, name)));

    private void CollectIdentification(DeviceInventory inventory, List<int> chassisCodes)
    {
        inventory.Uuid = Dmi("product_uuid")?.ToUpperInvariant();
        inventory.Manufacturer = Dmi("sys_vendor");
        inventory.Model = Dmi("product_name");

        var version = Dmi("product_version");
        if (inventory.Manufacturer?.Contains("lenovo", StringComparison.OrdinalIgnoreCase) == true &&
            version is not null &&
            !version.Equals(inventory.Model, StringComparison.OrdinalIgnoreCase))
        {
            inventory.Model = $"{version} ({inventory.Model})";
        }

        inventory.SerialNumber = Dmi("product_serial") ?? Dmi("chassis_serial") ?? Dmi("board_serial");
        inventory.Sku = Dmi("product_sku");
        inventory.FirmwareAssetTag = Dmi("chassis_asset_tag");

        if (int.TryParse(Dmi("chassis_type"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var chassis))
        {
            chassisCodes.Add(chassis);
        }

        var virt = RunProcess("systemd-detect-virt", "--vm", 10)?.Trim();
        if (!string.IsNullOrWhiteSpace(virt) && virt != "none")
        {
            inventory.Hypervisor = virt switch
            {
                "vmware" => "VMware",
                "oracle" => "VirtualBox",
                "microsoft" => "Hyper-V",
                "kvm" or "qemu" => "QEMU/KVM",
                "xen" => "Xen",
                "parallels" => "Parallels",
                "amazon" => "Amazon EC2",
                "google" => "Google Cloud",
                _ => virt
            };
        }

        var domain = RunProcess("hostname", "-d", 5)?.Trim();
        if (!string.IsNullOrWhiteSpace(domain))
        {
            inventory.Domain = domain;
            inventory.PartOfDomain = File.Exists("/etc/sssd/sssd.conf") || File.Exists("/etc/krb5.keytab");
        }

        inventory.Motherboard = new InventoryMotherboard
        {
            Manufacturer = Dmi("board_vendor"),
            Product = Dmi("board_name"),
            Version = Dmi("board_version"),
            SerialNumber = Dmi("board_serial")
        };
    }

    private void CollectOperatingSystem(DeviceInventory inventory)
    {
        var osRelease = ReadKeyValueFile("/etc/os-release");

        string? Value(string key) => osRelease.TryGetValue(key, out var value) ? Clean(value) : null;

        var os = new InventoryOperatingSystem
        {
            Name = Value("PRETTY_NAME") ?? Value("NAME") ?? RuntimeInformation.OSDescription,
            Edition = Value("VARIANT") ?? Value("NAME"),
            Version = Value("VERSION_ID"),
            DisplayVersion = Value("VERSION"),
            Architecture = GetArchitectureName(RuntimeInformation.OSArchitecture),
            KernelVersion = Clean(ReadFile("/proc/sys/kernel/osrelease")),
            Language = Environment.GetEnvironmentVariable("LANG"),
            TimeZone = TimeZoneInfo.Local.Id
        };

        // /etc/machine-id identifica a instalação (equivale ao ID do produto).
        os.ProductId = Clean(ReadFile("/etc/machine-id"));

        var uptime = ReadFile("/proc/uptime")?.Split(' ').FirstOrDefault();
        if (double.TryParse(uptime, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            os.LastBoot = DateTimeOffset.Now.AddSeconds(-seconds);
        }

        // Data de criação da raiz (aproximação da instalação).
        foreach (var path in new[] { "/var/log/installer", "/root", "/etc/hostname" })
        {
            try
            {
                if (Directory.Exists(path) || File.Exists(path))
                {
                    var created = File.GetCreationTime(path);
                    if (created.Year > 2000)
                    {
                        os.InstallDate = new DateTimeOffset(created);
                        break;
                    }
                }
            }
            catch
            {
                // Sem informação de criação neste sistema de arquivos.
            }
        }

        inventory.OperatingSystem = os;
    }

    private void CollectBios(DeviceInventory inventory)
    {
        inventory.Bios = new InventoryBios
        {
            Manufacturer = Dmi("bios_vendor"),
            Version = Dmi("bios_version"),
            ReleaseDate = ParseDate(Dmi("bios_date")),
            Mode = Directory.Exists("/sys/firmware/efi") ? "UEFI" : "Legado (BIOS)"
        };

        // SecureBoot-8be4df61-...: os 4 primeiros bytes são atributos; o 5º é o estado.
        try
        {
            var secureBootVar = Directory.Exists("/sys/firmware/efi/efivars")
                ? Directory.GetFiles("/sys/firmware/efi/efivars", "SecureBoot-*").FirstOrDefault()
                : null;
            if (secureBootVar is not null)
            {
                var bytes = File.ReadAllBytes(secureBootVar);
                if (bytes.Length >= 5)
                {
                    inventory.Bios.SecureBoot = bytes[4] == 1;
                }
            }
        }
        catch
        {
            // Sem acesso às variáveis EFI.
        }

        var dmidecode = RunProcess("dmidecode", "-t 0", 15);
        var smbios = dmidecode is null ? null : Regex.Match(dmidecode, @"SMBIOS (\d+\.\d+)");
        if (smbios?.Success == true)
        {
            inventory.Bios.SmbiosVersion = smbios.Groups[1].Value;
        }
    }

    private void CollectProcessors(DeviceInventory inventory)
    {
        var cpuInfo = ReadFile("/proc/cpuinfo");
        if (cpuInfo is null)
        {
            return;
        }

        var blocks = cpuInfo.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseColonBlock)
            .Where(x => x.Count > 0)
            .ToList();

        var maxFreqKhz = ReadFile("/sys/devices/system/cpu/cpu0/cpufreq/cpuinfo_max_freq");

        foreach (var socket in blocks.GroupBy(x => x.GetValueOrDefault("physical id") ?? "0"))
        {
            var first = socket.First();
            var name = Clean(first.GetValueOrDefault("model name")) ??
                Clean(first.GetValueOrDefault("Hardware")) ??
                Clean(first.GetValueOrDefault("Processor"));

            int? mhz = null;
            if (long.TryParse(maxFreqKhz, NumberStyles.Integer, CultureInfo.InvariantCulture, out var khz) && khz > 0)
            {
                mhz = (int)(khz / 1000);
            }
            else if (double.TryParse(first.GetValueOrDefault("cpu MHz"), NumberStyles.Float, CultureInfo.InvariantCulture, out var current))
            {
                mhz = (int)current;
            }

            inventory.Processors.Add(new InventoryProcessor
            {
                Name = name,
                Manufacturer = Clean(first.GetValueOrDefault("vendor_id")) switch
                {
                    "GenuineIntel" => "Intel",
                    "AuthenticAMD" => "AMD",
                    var vendor => vendor
                },
                Cores = ToInt(first.GetValueOrDefault("cpu cores")) ?? socket.Count(),
                Threads = socket.Count(),
                MaxClockMhz = mhz,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString()
            });
        }
    }

    private void CollectMemory(DeviceInventory inventory)
    {
        var output = RunProcess("dmidecode", "-t 17", 15);
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        foreach (var block in output.Split("Memory Device", StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            var values = ParseColonBlock(block);
            var size = values.GetValueOrDefault("Size");
            var capacity = ParseDmiSize(size);
            if (capacity is null or 0)
            {
                // Slot vazio.
                continue;
            }

            inventory.Memory.Add(new InventoryMemoryModule
            {
                Slot = Clean(values.GetValueOrDefault("Locator")) ?? Clean(values.GetValueOrDefault("Bank Locator")),
                CapacityBytes = capacity,
                SpeedMhz = ParseLeadingInt(values.GetValueOrDefault("Configured Memory Speed")) ??
                    ParseLeadingInt(values.GetValueOrDefault("Configured Clock Speed")) ??
                    ParseLeadingInt(values.GetValueOrDefault("Speed")),
                Type = Clean(values.GetValueOrDefault("Type")),
                FormFactor = Clean(values.GetValueOrDefault("Form Factor")),
                Manufacturer = Clean(values.GetValueOrDefault("Manufacturer")),
                PartNumber = Clean(values.GetValueOrDefault("Part Number")),
                SerialNumber = Clean(values.GetValueOrDefault("Serial Number"))
            });
        }
    }

    private void CollectDisks(DeviceInventory inventory)
    {
        var json = RunProcess("lsblk", "-J -b -d -o NAME,MODEL,SERIAL,SIZE,TRAN,ROTA,TYPE,VENDOR,REV", 15);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("blockdevices", out var devices))
        {
            return;
        }

        foreach (var device in devices.EnumerateArray())
        {
            var type = GetJsonString(device, "type");
            var name = GetJsonString(device, "name") ?? string.Empty;
            if (type != "disk" || name.StartsWith("loop", StringComparison.Ordinal) ||
                name.StartsWith("zram", StringComparison.Ordinal) || name.StartsWith("ram", StringComparison.Ordinal))
            {
                continue;
            }

            var transport = GetJsonString(device, "tran");
            var rotational = device.TryGetProperty("rota", out var rota) &&
                (rota.ValueKind == JsonValueKind.True ||
                 rota.ValueKind == JsonValueKind.String && rota.GetString() == "1" ||
                 rota.ValueKind == JsonValueKind.Number && rota.GetInt32() == 1);

            inventory.Disks.Add(new InventoryDisk
            {
                Model = Clean(GetJsonString(device, "model")) ?? name,
                Manufacturer = Clean(GetJsonString(device, "vendor")) is { } vendor && !vendor.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? vendor : null,
                SerialNumber = Clean(GetJsonString(device, "serial")),
                Firmware = Clean(GetJsonString(device, "rev")),
                SizeBytes = device.TryGetProperty("size", out var size)
                    ? size.ValueKind == JsonValueKind.Number ? size.GetInt64() : ToLong(size.GetString())
                    : null,
                Interface = transport?.ToUpperInvariant() switch
                {
                    null => name.StartsWith("nvme", StringComparison.Ordinal) ? "NVMe" : null,
                    "NVME" => "NVMe",
                    "VIRTIO" => "VirtIO",
                    var other => other
                },
                MediaType = transport == "usb" ? "Removível" : rotational ? "HD" : "SSD"
            });
        }
    }

    /// <summary>
    /// Todos os dispositivos PCI (lista "Controladora" do GLPI), mais placas de vídeo,
    /// som e rede. O servidor completa os nomes pela base pci.ids.
    /// </summary>
    private void CollectPciDevices(DeviceInventory inventory)
    {
        const string pciRoot = "/sys/bus/pci/devices";
        if (!Directory.Exists(pciRoot))
        {
            return;
        }

        // Nomes que o lspci já conhece (quando a pci.ids local está instalada).
        var lspci = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var quoted = new Regex("\"([^\"]*)\"");
        foreach (var line in (RunProcess("lspci", "-mm -D", 15) ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var slot = line.Split(' ', 2)[0];
            lspci[slot] = quoted.Matches(line).Select(x => x.Groups[1].Value).ToList();
        }

        // Interfaces de rede físicas por dispositivo PCI (para o MAC).
        var netBySlot = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists("/sys/class/net"))
        {
            foreach (var netDir in Directory.GetDirectories("/sys/class/net"))
            {
                try
                {
                    var deviceLink = new DirectoryInfo(Path.Combine(netDir, "device")).ResolveLinkTarget(true);
                    if (deviceLink is null)
                    {
                        continue;
                    }
                    var slot = Path.GetFileName(deviceLink.FullName);
                    if (!netBySlot.TryGetValue(slot, out var list))
                    {
                        netBySlot[slot] = list = [];
                    }
                    list.Add(Path.GetFileName(netDir));
                }
                catch
                {
                    // Interface virtual, sem dispositivo.
                }
            }
        }

        foreach (var dir in Directory.GetDirectories(pciRoot).OrderBy(x => x, StringComparer.Ordinal))
        {
            var slot = Path.GetFileName(dir);
            var vendor = ReadFile(Path.Combine(dir, "vendor"))?.Replace("0x", string.Empty).ToUpperInvariant();
            var device = ReadFile(Path.Combine(dir, "device"))?.Replace("0x", string.Empty).ToUpperInvariant();
            var classCode = ReadFile(Path.Combine(dir, "class"))?.Replace("0x", string.Empty);
            if (vendor is null || device is null)
            {
                continue;
            }

            lspci.TryGetValue(slot, out var names);
            var className = names is { Count: > 0 } ? names[0] : PciClassName(classCode);
            var name = names is { Count: > 2 } ? names[2] : className;
            var manufacturer = names is { Count: > 1 } ? names[1] : null;
            var pciId = $"{vendor}:{device}";

            var controller = new InventoryComponent
            {
                Category = "Controladora",
                Name = name,
                Manufacturer = manufacturer,
                PciId = pciId
            };
            if (className is not null)
            {
                controller.Details["Interface"] = className;
            }
            inventory.Components.Add(controller);

            var baseClass = classCode is { Length: >= 2 } ? classCode[..2] : null;
            var subClass = classCode is { Length: >= 4 } ? classCode[2..4] : null;

            var category = baseClass switch
            {
                "03" => "Placa de vídeo",
                "04" when subClass is "01" or "03" => "Placa de som",
                "02" => "Placa de rede",
                _ => null
            };
            if (category is null)
            {
                continue;
            }

            var component = new InventoryComponent
            {
                Category = category,
                Name = name,
                Manufacturer = manufacturer,
                PciId = pciId
            };

            if (category == "Placa de rede" && netBySlot.TryGetValue(slot, out var interfaces))
            {
                var mac = FormatMac(ReadFile(Path.Combine("/sys/class/net", interfaces[0], "address")));
                if (mac is not null)
                {
                    component.Details["Endereço MAC"] = mac;
                }
                component.Details["Interface"] = string.Join(", ", interfaces);
            }

            inventory.Components.Add(component);
        }
    }

    private static string? PciClassName(string? classCode) => classCode is { Length: >= 2 } ? classCode[..2] switch
    {
        "01" => "Controladora de armazenamento",
        "02" => "Controladora de rede",
        "03" => "Controladora de vídeo",
        "04" => "Controladora multimídia",
        "05" => "Controladora de memória",
        "06" => "Ponte",
        "07" => "Controladora de comunicação",
        "08" => "Periférico do sistema",
        "0c" or "0C" => "Controladora de barramento serial",
        "0d" or "0D" => "Controladora sem fio",
        _ => null
    } : null;

    private static void CollectBatteries(DeviceInventory inventory)
    {
        const string powerSupply = "/sys/class/power_supply";
        if (!Directory.Exists(powerSupply))
        {
            return;
        }

        foreach (var dir in Directory.GetDirectories(powerSupply))
        {
            if (ReadFile(Path.Combine(dir, "type")) != "Battery" ||
                ReadFile(Path.Combine(dir, "scope")) == "Device")
            {
                continue;
            }

            var component = new InventoryComponent
            {
                Category = "Bateria",
                Name = Clean(ReadFile(Path.Combine(dir, "model_name"))) ?? Path.GetFileName(dir),
                Manufacturer = Clean(ReadFile(Path.Combine(dir, "manufacturer"))),
                SerialNumber = Clean(ReadFile(Path.Combine(dir, "serial_number")))
            };

            var technology = Clean(ReadFile(Path.Combine(dir, "technology")));
            if (technology is not null)
            {
                component.Details["Química"] = technology;
            }

            // energy_* em µWh (vira mWh); charge_* em µAh (só o percentual faz sentido).
            var hasEnergy = File.Exists(Path.Combine(dir, "energy_full_design"));
            var design = ToLong(ReadFile(Path.Combine(dir, "energy_full_design"))) ?? ToLong(ReadFile(Path.Combine(dir, "charge_full_design")));
            var full = ToLong(ReadFile(Path.Combine(dir, "energy_full"))) ?? ToLong(ReadFile(Path.Combine(dir, "charge_full")));
            var voltage = ToLong(ReadFile(Path.Combine(dir, "voltage_min_design")));

            if (voltage is > 0)
            {
                component.Details["Voltagem"] = $"{voltage.Value / 1000} mV";
            }
            if (hasEnergy && design is > 0)
            {
                component.Details["Capacidade de projeto"] = $"{design.Value / 1000} mWh";
            }
            if (design is > 0 && full is > 0)
            {
                var percent = Math.Min(100, 100.0 * full.Value / design.Value);
                component.Details["Capacidade real"] = hasEnergy
                    ? $"{full.Value / 1000} mWh ({percent:0}%)"
                    : $"{percent:0}%";
            }

            inventory.Components.Add(component);
        }
    }

    private void CollectSoftware(DeviceInventory inventory)
    {
        var dpkg = RunProcess("dpkg-query", "-W -f=${Package}\\t${Version}\\t${Maintainer}\\t${Installed-Size}\\t${Architecture}\\t${db:Status-Abbrev}\\n", 60);
        if (!string.IsNullOrWhiteSpace(dpkg))
        {
            foreach (var line in dpkg.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('\t');
                if (parts.Length < 5 || parts.Length >= 6 && !parts[5].StartsWith("ii", StringComparison.Ordinal))
                {
                    continue;
                }

                inventory.Software.Add(new InventorySoftware
                {
                    Name = parts[0],
                    Version = Clean(parts[1]),
                    Publisher = Clean(Regex.Replace(parts[2], @"\s*<[^>]*>", string.Empty)),
                    SizeBytes = ToLong(parts[3]) is { } kb ? kb * 1024 : null,
                    Architecture = Clean(parts[4]),
                    Source = "dpkg"
                });
            }
            return;
        }

        var rpm = RunProcess("rpm", "-qa --qf %{NAME}\\t%{VERSION}-%{RELEASE}\\t%{VENDOR}\\t%{SIZE}\\t%{ARCH}\\t%{INSTALLTIME}\\n", 60);
        if (string.IsNullOrWhiteSpace(rpm))
        {
            return;
        }

        foreach (var line in rpm.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length < 5 || parts[0].StartsWith("gpg-pubkey", StringComparison.Ordinal))
            {
                continue;
            }

            DateTimeOffset? installed = null;
            if (parts.Length >= 6 && long.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch) && epoch > 0)
            {
                installed = DateTimeOffset.FromUnixTimeSeconds(epoch).ToLocalTime();
            }

            inventory.Software.Add(new InventorySoftware
            {
                Name = parts[0],
                Version = Clean(parts[1]),
                Publisher = Clean(parts[2] == "(none)" ? null : parts[2]),
                SizeBytes = ToLong(parts[3]),
                Architecture = Clean(parts[4] == "(none)" ? null : parts[4]),
                InstallDate = installed,
                Source = "rpm"
            });
        }
    }

    private static void CollectAntivirus(DeviceInventory inventory)
    {
        // Linux não tem uma central de segurança; detecta os produtos mais comuns pelos pacotes.
        (string Package, string Name, string Vendor)[] known =
        [
            ("clamav", "ClamAV", "Cisco"),
            ("falcon-sensor", "CrowdStrike Falcon", "CrowdStrike"),
            ("sentinelagent", "SentinelOne", "SentinelOne"),
            ("mdatp", "Microsoft Defender for Endpoint", "Microsoft"),
            ("esets", "ESET Server Security", "ESET"),
            ("eset-endpoint", "ESET Endpoint Antivirus", "ESET"),
            ("kesl", "Kaspersky Endpoint Security", "Kaspersky"),
            ("sophos-spl", "Sophos Protection", "Sophos"),
            ("ds-agent", "Trend Micro Deep Security", "Trend Micro"),
            ("bitdefender-security-tools", "Bitdefender GravityZone", "Bitdefender")
        ];

        foreach (var (package, name, vendor) in known)
        {
            var software = inventory.Software.FirstOrDefault(x => x.Name.Equals(package, StringComparison.OrdinalIgnoreCase));
            if (software is null)
            {
                continue;
            }

            inventory.Antivirus.Add(new InventoryAntivirus
            {
                Name = name,
                Manufacturer = vendor,
                Version = software.Version
            });
        }
    }

    // ---------------------------------------------------------------------

    private static Dictionary<string, string> ReadKeyValueFile(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var text = ReadFile(path);
        if (text is null)
        {
            return result;
        }

        foreach (var line in text.Split('\n'))
        {
            var index = line.IndexOf('=');
            if (index <= 0)
            {
                continue;
            }
            result[line[..index].Trim()] = line[(index + 1)..].Trim().Trim('"');
        }
        return result;
    }

    private static Dictionary<string, string> ParseColonBlock(string block)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in block.Split('\n'))
        {
            var index = line.IndexOf(':');
            if (index <= 0)
            {
                continue;
            }
            var key = line[..index].Trim();
            if (!result.ContainsKey(key))
            {
                result[key] = line[(index + 1)..].Trim();
            }
        }
        return result;
    }

    private static long? ParseDmiSize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = Regex.Match(value, @"(\d+)\s*(KB|MB|GB|TB|kB)", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        var number = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        return match.Groups[2].Value.ToUpperInvariant() switch
        {
            "KB" => number * 1024,
            "MB" => number * 1024 * 1024,
            "GB" => number * 1024 * 1024 * 1024,
            "TB" => number * 1024 * 1024 * 1024 * 1024,
            _ => null
        };
    }

    private static int? ParseLeadingInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var match = Regex.Match(value, @"^\d+");
        return match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) && result > 0
            ? result
            : null;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return DateTime.TryParseExact(value, ["MM/dd/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date)
            ? new DateTimeOffset(date)
            : null;
    }

    private static string? GetJsonString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "1",
            JsonValueKind.False => "0",
            _ => null
        };
    }
}
