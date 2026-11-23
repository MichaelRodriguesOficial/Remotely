using Microsoft.Extensions.Logging;
using Remotely.Shared.Models.Inventory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

namespace Remotely.Agent.Services.Inventory;

/// <summary>
/// Inventário básico do macOS via system_profiler e sw_vers.
/// </summary>
public class InventoryCollectorMac : InventoryCollectorBase
{
    public InventoryCollectorMac(ILogger<InventoryCollectorMac> logger)
        : base(logger)
    {
    }

    protected override void CollectCore(DeviceInventory inventory, CancellationToken cancellationToken)
    {
        Run(inventory, "Hardware", () =>
        {
            var hardware = ParseProfiler(RunProcess("system_profiler", "SPHardwareDataType", 60));

            inventory.Manufacturer = "Apple";
            inventory.Model = Clean(hardware.GetValueOrDefault("Model Name"));
            var identifier = Clean(hardware.GetValueOrDefault("Model Identifier"));
            if (identifier is not null)
            {
                inventory.Model = inventory.Model is null ? identifier : $"{inventory.Model} ({identifier})";
            }
            inventory.SerialNumber = Clean(hardware.GetValueOrDefault("Serial Number (system)"));
            inventory.Uuid = Clean(hardware.GetValueOrDefault("Hardware UUID"));

            var model = inventory.Model ?? string.Empty;
            inventory.DeviceType = model.Contains("Book", StringComparison.OrdinalIgnoreCase)
                ? "Notebook"
                : model.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ? "Máquina virtual" : "Desktop";

            var chip = Clean(hardware.GetValueOrDefault("Chip")) ?? Clean(hardware.GetValueOrDefault("Processor Name"));
            if (chip is not null)
            {
                inventory.Processors.Add(new InventoryProcessor
                {
                    Name = chip,
                    Manufacturer = chip.Contains("Intel", StringComparison.OrdinalIgnoreCase) ? "Intel" : "Apple",
                    Cores = ToInt(Regex.Match(hardware.GetValueOrDefault("Total Number of Cores") ?? string.Empty, @"\d+").Value),
                    Threads = Environment.ProcessorCount,
                    Architecture = RuntimeInformation.ProcessArchitecture.ToString()
                });
            }

            var memory = Regex.Match(hardware.GetValueOrDefault("Memory") ?? string.Empty, @"(\d+)\s*GB");
            if (memory.Success)
            {
                inventory.Memory.Add(new InventoryMemoryModule
                {
                    Slot = "Integrada",
                    CapacityBytes = long.Parse(memory.Groups[1].Value) * 1024 * 1024 * 1024
                });
            }

            inventory.Bios = new InventoryBios
            {
                Manufacturer = "Apple",
                Version = Clean(hardware.GetValueOrDefault("System Firmware Version")) ??
                    Clean(hardware.GetValueOrDefault("Boot ROM Version")),
                Mode = "UEFI"
            };
        });

        Run(inventory, "Sistema operacional", () =>
        {
            var name = Clean(RunProcess("sw_vers", "-productName", 10));
            var version = Clean(RunProcess("sw_vers", "-productVersion", 10));
            var build = Clean(RunProcess("sw_vers", "-buildVersion", 10));

            inventory.OperatingSystem = new InventoryOperatingSystem
            {
                Name = string.Join(' ', new[] { name, version }.Where(x => x is not null)),
                Version = version,
                Build = build,
                Architecture = GetArchitectureName(RuntimeInformation.OSArchitecture),
                KernelVersion = Clean(RunProcess("uname", "-r", 10)),
                TimeZone = TimeZoneInfo.Local.Id
            };
        });

        Run(inventory, "Volumes", () => inventory.Volumes = CollectVolumesFromDriveInfo());
    }

    private static Dictionary<string, string> ParseProfiler(string? output)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(output))
        {
            return result;
        }

        foreach (var line in output.Split('\n'))
        {
            var index = line.IndexOf(':');
            if (index <= 0)
            {
                continue;
            }
            var key = line[..index].Trim();
            var value = line[(index + 1)..].Trim();
            if (value.Length > 0 && !result.ContainsKey(key))
            {
                result[key] = value;
            }
        }
        return result;
    }
}
