using Microsoft.Extensions.Logging;
using Remotely.Agent.Interfaces;
using Remotely.Shared.Models.Inventory;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Remotely.Agent.Services.Inventory;

/// <summary>
/// Partes do inventário que funcionam igual em todos os sistemas (rede, volumes)
/// e regras comuns (tipo do equipamento, máquinas virtuais, limpeza de valores).
/// </summary>
public abstract class InventoryCollectorBase : IInventoryCollector
{
    // Valores de preenchimento que os fabricantes deixam no SMBIOS.
    private static readonly HashSet<string> _junkValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "To be filled by O.E.M.", "To Be Filled By O.E.M.", "Default string", "System Serial Number",
        "System Product Name", "System manufacturer", "System Manufacturer", "System Version",
        "Not Specified", "Not Applicable", "None", "N/A", "NA", "Unknown", "Undefined", "Invalid",
        "0", "00000000", "0000000000", "123456789", "1234567890", "Chassis Serial Number",
        "Base Board Serial Number", "Type1ProductConfigId", "SKU", "Default", "OEM", "O.E.M.",
        "03000200-0400-0500-0006-000700080009", "00000000-0000-0000-0000-000000000000",
        "FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF", "Not Available", "Unknow"
    };

    protected readonly ILogger _logger;

    protected InventoryCollectorBase(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<DeviceInventory> Collect(CancellationToken cancellationToken)
    {
        var inventory = new DeviceInventory
        {
            CollectedAt = DateTimeOffset.Now
        };

        // A coleta usa chamadas síncronas (WMI, arquivos, processos), então roda
        // fora da thread do SignalR.
        await Task.Run(() => CollectCore(inventory, cancellationToken), cancellationToken);

        Run(inventory, "Rede", () => inventory.NetworkPorts = CollectNetworkPorts(inventory.NetworkPorts));

        inventory.DeviceType ??= ClassifyDeviceType(
            chassisCodes: [],
            isServerOs: false,
            hypervisor: inventory.Hypervisor,
            model: inventory.Model);

        inventory.Software = inventory.Software
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .GroupBy(x => $"{x.Name}|{x.Version}|{x.Architecture}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return inventory;
    }

    protected abstract void CollectCore(DeviceInventory inventory, CancellationToken cancellationToken);

    /// <summary>
    /// Executa uma parte da coleta. Se ela falhar, registra o aviso e segue com o resto.
    /// </summary>
    protected void Run(DeviceInventory inventory, string part, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Inventory: failed to collect {part}.", part);
            inventory.Warnings.Add($"{part}: {ex.GetType().Name} - {ex.Message}");
        }
    }

    /// <summary>
    /// Completa processadores, memórias e baterias com a tabela SMBIOS (as mesmas
    /// informações do dmidecode/GLPI): frequência máxima, núcleos e threads, frequência
    /// nominal das memórias, posição no barramento, química, voltagem e data da bateria.
    /// </summary>
    protected void ApplySmbios(DeviceInventory inventory, byte[]? table)
    {
        if (table is null || table.Length < 8)
        {
            return;
        }

        var smbios = SmbiosReader.Parse(table);

        Run(inventory, "SMBIOS: processadores", () =>
        {
            var processors = smbios.GetProcessors().ToList();
            for (var i = 0; i < processors.Count; i++)
            {
                var source = processors[i];
                var target = i < inventory.Processors.Count ? inventory.Processors[i] : null;
                if (target is null)
                {
                    target = new InventoryProcessor { Name = Clean(source.Version) };
                    inventory.Processors.Add(target);
                }

                target.Name ??= Clean(source.Version);
                target.Manufacturer = Clean(source.Manufacturer) ?? target.Manufacturer;
                target.Cores = source.Cores ?? target.Cores;
                target.Threads = source.Threads ?? target.Threads;
                target.MaxClockMhz = source.MaxSpeedMhz ?? target.MaxClockMhz;
                target.CurrentClockMhz = source.CurrentSpeedMhz ?? target.CurrentClockMhz;
                target.Socket = Clean(source.Socket) ?? target.Socket;
                target.SerialNumber = Clean(source.SerialNumber) ?? target.SerialNumber;
            }
        });

        Run(inventory, "SMBIOS: memórias", () =>
        {
            var modules = smbios.GetMemoryDevices().ToList();
            if (modules.Count == 0)
            {
                return;
            }

            var previous = inventory.Memory;
            inventory.Memory = [];
            for (var i = 0; i < modules.Count; i++)
            {
                var source = modules[i];
                var match = previous.FirstOrDefault(x =>
                    string.Equals(x.Slot, source.Locator, StringComparison.OrdinalIgnoreCase) ||
                    (x.SerialNumber is not null && string.Equals(x.SerialNumber, Clean(source.SerialNumber), StringComparison.OrdinalIgnoreCase)));

                inventory.Memory.Add(new InventoryMemoryModule
                {
                    Slot = Clean(source.Locator) ?? Clean(source.BankLocator) ?? match?.Slot,
                    CapacityBytes = source.SizeBytes > 0 ? source.SizeBytes : match?.CapacityBytes,
                    SpeedMhz = source.SpeedMhz ?? match?.SpeedMhz,
                    ConfiguredSpeedMhz = source.ConfiguredSpeedMhz ?? match?.ConfiguredSpeedMhz,
                    Type = source.Type ?? match?.Type,
                    FormFactor = source.FormFactor ?? match?.FormFactor,
                    Manufacturer = Clean(source.Manufacturer) ?? match?.Manufacturer,
                    PartNumber = Clean(source.PartNumber) ?? match?.PartNumber,
                    SerialNumber = Clean(source.SerialNumber) ?? match?.SerialNumber,
                    BusPosition = i + 1
                });
            }
        });

        Run(inventory, "SMBIOS: baterias", () =>
        {
            var batteries = smbios.GetBatteries().ToList();
            var existing = inventory.Components.Where(x => x.Category == "Bateria").ToList();

            for (var i = 0; i < batteries.Count; i++)
            {
                var source = batteries[i];

                // Uma bateria física: junta o que o SMBIOS informa (data, química, voltagem)
                // com o que o driver informa (capacidade atual e saúde).
                var target = i < existing.Count ? existing[i] : null;
                if (target is null)
                {
                    target = new InventoryComponent { Category = "Bateria" };
                    inventory.Components.Add(target);
                }

                target.Name = Clean(source.Name) ?? target.Name ?? "Bateria";
                target.Manufacturer = Clean(source.Manufacturer) ?? target.Manufacturer;
                target.SerialNumber ??= Clean(source.SerialNumber);

                if (Clean(source.Chemistry) is { } chemistry)
                {
                    target.Details["Química"] = chemistry;
                }
                if (source.DesignVoltageMv is { } voltage)
                {
                    target.Details["Voltagem"] = $"{voltage} mV";
                }
                if (source.DesignCapacityMwh is { } capacity && !target.Details.ContainsKey("Capacidade de projeto"))
                {
                    target.Details["Capacidade de projeto"] = $"{capacity} mWh";
                }
                if (Clean(source.ManufactureDate) is { } date)
                {
                    target.Details["Data de fabricação"] = date;
                }
            }
        });
    }

    /// <summary>Remove espaços e valores genéricos de fábrica ("To be filled by O.E.M." etc.).</summary>
    protected static string? Clean(object? value)
    {
        var text = value switch
        {
            null => null,
            string s => s,
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = text.Replace("\0", string.Empty).Trim();
        if (text.Length == 0 || _junkValues.Contains(text))
        {
            return null;
        }

        // Números de série feitos só de zeros, F ou espaços.
        if (text.All(c => c == '0' || c == ' ' || c == '-') || text.All(c => c == 'F' || c == 'f' || c == '-'))
        {
            return null;
        }

        return text;
    }

    protected static long? ToLong(object? value)
    {
        try
        {
            return value switch
            {
                null => null,
                string s when long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
                string => null,
                _ => Convert.ToInt64(value, CultureInfo.InvariantCulture)
            };
        }
        catch
        {
            return null;
        }
    }

    protected static int? ToInt(object? value)
    {
        var result = ToLong(value);
        return result is null or > int.MaxValue or < int.MinValue ? null : (int)result.Value;
    }

    protected static bool? ToBool(object? value)
    {
        try
        {
            return value switch
            {
                null => null,
                bool b => b,
                string s when bool.TryParse(s, out var parsed) => parsed,
                string s when s == "1" => true,
                string s when s == "0" => false,
                string => null,
                _ => Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Fabricante "Microsoft Corporation" / modelo "Virtual Machine", "VMware7,1"...
    /// </summary>
    protected static string? DetectHypervisor(params string?[] values)
    {
        var text = string.Join(' ', values.Where(x => !string.IsNullOrWhiteSpace(x))).ToLowerInvariant();
        if (text.Length == 0)
        {
            return null;
        }

        if (text.Contains("vmware")) return "VMware";
        if (text.Contains("virtualbox") || text.Contains("innotek")) return "VirtualBox";
        if (text.Contains("qemu")) return "QEMU/KVM";
        if (text.Contains("kvm")) return "KVM";
        if (text.Contains("xen")) return "Xen";
        if (text.Contains("parallels")) return "Parallels";
        if (text.Contains("bochs")) return "Bochs";
        if (text.Contains("proxmox")) return "Proxmox";
        if (text.Contains("amazon ec2")) return "Amazon EC2";
        if (text.Contains("google compute engine")) return "Google Cloud";
        if (text.Contains("openstack")) return "OpenStack";
        if (text.Contains("nutanix") || text.Contains("ahv")) return "Nutanix AHV";
        if (text.Contains("microsoft corporation") && text.Contains("virtual machine")) return "Hyper-V";
        if (text.Contains("hyper-v")) return "Hyper-V";
        if (text.Contains("virtual machine")) return "Máquina virtual";
        return null;
    }

    /// <summary>
    /// Desktop, Notebook, Servidor, Máquina virtual... a partir dos códigos de
    /// gabinete do SMBIOS (os mesmos no Windows e no Linux).
    /// </summary>
    protected static string ClassifyDeviceType(
        IReadOnlyCollection<int> chassisCodes,
        bool isServerOs,
        string? hypervisor,
        string? model)
    {
        if (!string.IsNullOrWhiteSpace(hypervisor))
        {
            return "Máquina virtual";
        }

        if (chassisCodes.Any(x => x is 17 or 23 or 25 or 28 or 29))
        {
            return "Servidor";
        }

        if (chassisCodes.Any(x => x is 8 or 9 or 10 or 14 or 31 or 32))
        {
            return "Notebook";
        }

        if (chassisCodes.Any(x => x is 30))
        {
            return "Tablet";
        }

        if (chassisCodes.Any(x => x is 11))
        {
            return "Portátil";
        }

        if (chassisCodes.Any(x => x is 13))
        {
            return "Tudo em um";
        }

        if (chassisCodes.Any(x => x is 35 or 36))
        {
            return "Mini PC";
        }

        if (isServerOs)
        {
            return "Servidor";
        }

        if (chassisCodes.Any(x => x is 3 or 4 or 5 or 6 or 7 or 15 or 16 or 24))
        {
            return "Desktop";
        }

        if (!string.IsNullOrWhiteSpace(model) &&
            (model.Contains("book", StringComparison.OrdinalIgnoreCase) ||
             model.Contains("laptop", StringComparison.OrdinalIgnoreCase) ||
             model.Contains("latitude", StringComparison.OrdinalIgnoreCase) ||
             model.Contains("thinkpad", StringComparison.OrdinalIgnoreCase)))
        {
            return "Notebook";
        }

        return "Desktop";
    }

    protected static string ChassisName(int code) => code switch
    {
        1 => "Outro",
        2 => "Desconhecido",
        3 => "Desktop",
        4 => "Desktop compacto",
        5 => "Pizza box",
        6 => "Minitorre",
        7 => "Torre",
        8 => "Portátil",
        9 => "Laptop",
        10 => "Notebook",
        11 => "Portátil de mão",
        12 => "Docking station",
        13 => "Tudo em um",
        14 => "Subnotebook",
        15 => "Compacto",
        16 => "Lunch box",
        17 => "Servidor (gabinete principal)",
        18 => "Chassi de expansão",
        19 => "Subchassi",
        20 => "Chassi de expansão de barramento",
        21 => "Chassi periférico",
        22 => "Chassi de armazenamento",
        23 => "Rack",
        24 => "Gabinete selado",
        25 => "Multissistema",
        28 => "Blade",
        29 => "Gabinete de blades",
        30 => "Tablet",
        31 => "Conversível",
        32 => "Destacável",
        33 => "Gateway IoT",
        34 => "PC embarcado",
        35 => "Mini PC",
        36 => "Stick PC",
        _ => $"Código {code}"
    };

    /// <summary>Portas de rede via .NET (igual em Windows, Linux e macOS).</summary>
    protected virtual List<InventoryNetworkPort> CollectNetworkPorts(List<InventoryNetworkPort> existing)
    {
        var ports = new List<InventoryNetworkPort>();

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            var mac = FormatMac(nic.GetPhysicalAddress());
            var port = existing.FirstOrDefault(x =>
                    !string.IsNullOrWhiteSpace(mac) &&
                    string.Equals(x.MacAddress, mac, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.Description, nic.Description, StringComparison.OrdinalIgnoreCase))
                ?? new InventoryNetworkPort();

            port.Name = nic.Name;
            port.Description ??= nic.Description;
            port.MacAddress = mac;
            port.Type = nic.NetworkInterfaceType switch
            {
                NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or
                NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx or
                NetworkInterfaceType.Ethernet3Megabit => "Ethernet",
                NetworkInterfaceType.Wireless80211 => "Wi-Fi",
                NetworkInterfaceType.Ppp => "PPP",
                NetworkInterfaceType.Tunnel => "Túnel",
                NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2 => "Banda larga móvel",
                _ => nic.NetworkInterfaceType.ToString()
            };
            port.Connected = nic.OperationalStatus == OperationalStatus.Up;

            try
            {
                if (port.Connected == true && nic.Speed > 0)
                {
                    port.SpeedMbps = nic.Speed / 1_000_000;
                }
            }
            catch
            {
                // Algumas interfaces não informam a velocidade.
            }

            port.Virtual ??= LooksVirtual(nic.Name, nic.Description) ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel;

            try
            {
                var props = nic.GetIPProperties();

                port.IPv4.Clear();
                port.SubnetMasks.Clear();
                port.IPv6.Clear();

                foreach (var address in props.UnicastAddresses)
                {
                    if (address.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        port.IPv4.Add(address.Address.ToString());
                        try
                        {
                            port.SubnetMasks.Add(address.IPv4Mask.ToString());
                        }
                        catch
                        {
                            // Máscara indisponível neste sistema.
                        }
                    }
                    else if (address.Address.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        port.IPv6.Add(address.Address.ToString());
                    }
                }

                port.Gateways = props.GatewayAddresses
                    .Select(x => x.Address.ToString())
                    .Where(x => x != "0.0.0.0" && x != "::")
                    .Distinct()
                    .ToList();

                port.DnsServers = props.DnsAddresses
                    .Select(x => x.ToString())
                    .Distinct()
                    .ToList();

                if (OperatingSystem.IsWindows())
                {
                    try
                    {
                        port.Dhcp ??= props.GetIPv4Properties()?.IsDhcpEnabled;
                    }
                    catch
                    {
                        // Interface sem IPv4.
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Inventory: unable to read IP properties of {nic}.", nic.Name);
            }

            ports.Add(port);
        }

        // Adaptadores que o WMI conhece mas o .NET não lista (desativados, por exemplo).
        foreach (var port in existing.Where(x => !ports.Contains(x)))
        {
            port.Connected ??= false;
            ports.Add(port);
        }

        return ports
            .OrderBy(x => x.Virtual == true)
            .ThenByDescending(x => x.Connected == true)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    protected static bool LooksVirtual(params string?[] values)
    {
        var text = string.Join(' ', values.Where(x => !string.IsNullOrWhiteSpace(x))).ToLowerInvariant();
        string[] markers =
        [
            "virtual", "vmware", "virtualbox", "hyper-v", "vethernet", "loopback", "tap-", "tap ",
            "tun", "wan miniport", "bluetooth", "docker", "veth", "br-", "virbr", "vpn", "fortinet",
            "wireguard", "zerotier", "tailscale", "teredo", "isatap", "pseudo", "npcap", "kernel debug"
        ];
        return markers.Any(text.Contains);
    }

    protected static string? FormatMac(PhysicalAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        var bytes = address.GetAddressBytes();
        if (bytes.Length != 6 || bytes.All(x => x == 0))
        {
            return null;
        }

        return string.Join(':', bytes.Select(x => x.ToString("X2", CultureInfo.InvariantCulture)));
    }

    protected static string? FormatMac(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac))
        {
            return null;
        }
        var hex = new string(mac.Where(Uri.IsHexDigit).ToArray());
        if (hex.Length != 12)
        {
            return mac.Trim().ToUpperInvariant();
        }
        return string.Join(':', Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2))).ToUpperInvariant();
    }

    /// <summary>Volumes via .NET (usado no Linux/macOS e como reserva no Windows).</summary>
    protected static List<InventoryVolume> CollectVolumesFromDriveInfo()
    {
        string[] ignoredFormats =
        [
            "proc", "sysfs", "tmpfs", "devtmpfs", "devpts", "cgroup", "cgroup2", "overlay", "squashfs",
            "securityfs", "pstore", "debugfs", "tracefs", "configfs", "fusectl", "mqueue", "hugetlbfs",
            "autofs", "binfmt_misc", "bpf", "efivarfs", "nsfs", "ramfs", "rpc_pipefs", "fuse.gvfsd-fuse",
            "fuse.portal", "devfs", "autofs", "nullfs", "selinuxfs", "tracefs"
        ];

        var volumes = new List<InventoryVolume>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady ||
                    ignoredFormats.Contains(drive.DriveFormat, StringComparer.OrdinalIgnoreCase) ||
                    drive.TotalSize <= 0)
                {
                    continue;
                }

                var mount = drive.RootDirectory.FullName;
                if (!OperatingSystem.IsWindows() &&
                    (mount.StartsWith("/snap/", StringComparison.Ordinal) ||
                     mount.StartsWith("/run/", StringComparison.Ordinal) ||
                     mount.StartsWith("/sys/", StringComparison.Ordinal) ||
                     mount.StartsWith("/proc/", StringComparison.Ordinal) ||
                     mount.StartsWith("/dev/", StringComparison.Ordinal) ||
                     mount.StartsWith("/var/lib/docker/", StringComparison.Ordinal) ||
                     mount.StartsWith("/System/Volumes/", StringComparison.Ordinal) && mount != "/System/Volumes/Data"))
                {
                    continue;
                }

                volumes.Add(new InventoryVolume
                {
                    MountPoint = OperatingSystem.IsWindows() ? mount.TrimEnd('\\') : mount,
                    Label = Clean(drive.VolumeLabel == mount ? null : drive.VolumeLabel),
                    FileSystem = drive.DriveFormat,
                    TotalBytes = drive.TotalSize,
                    FreeBytes = drive.AvailableFreeSpace,
                    Type = drive.DriveType switch
                    {
                        DriveType.Fixed => "Local",
                        DriveType.Removable => "Removível",
                        DriveType.Network => "Rede",
                        DriveType.CDRom => "CD/DVD",
                        DriveType.Ram => "Memória",
                        _ => "Outro"
                    }
                });
            }
            catch
            {
                // Unidade sumiu ou sem permissão; ignora.
            }
        }
        return volumes;
    }

    /// <summary>
    /// Executa um programa e devolve a saída. Lê a saída antes de esperar o fim
    /// (evita travar quando a saída é grande) e respeita um tempo limite.
    /// </summary>
    protected string? RunProcess(string fileName, string arguments, int timeoutSeconds = 60)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            psi.Environment["LC_ALL"] = "C";
            psi.Environment["LANG"] = "C";

            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(TimeSpan.FromSeconds(timeoutSeconds)))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Já terminou.
                }
                return null;
            }

            return outputTask.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Inventory: unable to run {fileName}.", fileName);
            return null;
        }
    }

    protected static string? ReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    protected static string GetArchitectureName(Architecture architecture) => architecture switch
    {
        Architecture.X64 => "64 bits (x64)",
        Architecture.X86 => "32 bits (x86)",
        Architecture.Arm64 => "ARM 64 bits",
        Architecture.Arm => "ARM 32 bits",
        _ => architecture.ToString()
    };
}
