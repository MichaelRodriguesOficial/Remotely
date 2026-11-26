using Remotely.Shared.Models.Diagnostics;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using static Remotely.Agent.Services.Diagnostics.DiagnosticHelpers;

namespace Remotely.Agent.Services.Diagnostics;

/// <summary>Testes do Linux (smartctl, /sys, /proc).</summary>
internal static class LinuxDiagnostics
{
    public static void Disks(DiagnosticTestResult test)
    {
        var (scanExit, scanOutput) = Run("smartctl", "--scan -j", 15);
        if (scanExit < 0 || string.IsNullOrWhiteSpace(scanOutput))
        {
            test.Items.Add(Item("SMART", "smartctl não instalado (pacote smartmontools)", DiagnosticStatus.Info,
                "Instale o smartmontools para ver a saúde dos discos."));
            return;
        }

        using var scan = JsonDocument.Parse(scanOutput);
        if (!scan.RootElement.TryGetProperty("devices", out var devices))
        {
            return;
        }

        foreach (var device in devices.EnumerateArray())
        {
            var name = device.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var (_, output) = Run("smartctl", $"-a -j {name}", 30);
            if (string.IsNullOrWhiteSpace(output))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(output);
            var health = DiskHealth.FromSmartctlJson(doc.RootElement, name);
            test.Items.AddRange(DiskHealth.Evaluate(health));
        }
    }

    public static bool Battery(DiagnosticTestResult test)
    {
        const string root = "/sys/class/power_supply";
        if (!Directory.Exists(root))
        {
            return false;
        }

        var found = false;
        foreach (var dir in Directory.GetDirectories(root))
        {
            if (Read(dir, "type") != "Battery" || Read(dir, "scope") == "Device")
            {
                continue;
            }

            found = true;
            var energy = Read(dir, "energy_full_design") is not null;
            var data = new BatteryHealthData
            {
                Group = Read(dir, "model_name") ?? Path.GetFileName(dir),
                Manufacturer = Read(dir, "manufacturer"),
                Chemistry = Read(dir, "technology"),
                // energy_* vem em µWh e charge_* em µAh: divide por 1000.
                DesignCapacity = (ToLong(Read(dir, "energy_full_design")) ?? ToLong(Read(dir, "charge_full_design"))) / 1000,
                FullChargeCapacity = (ToLong(Read(dir, "energy_full")) ?? ToLong(Read(dir, "charge_full"))) / 1000,
                CycleCount = ToLong(Read(dir, "cycle_count")),
                ChargePercent = (int?)ToLong(Read(dir, "capacity")),
                PowerState = Read(dir, "status") switch
                {
                    "Charging" => "carregando",
                    "Full" or "Not charging" => "na tomada",
                    "Discharging" => "na bateria",
                    _ => null
                }
            };
            if (!energy)
            {
                data.CapacityUnit = " mAh";
            }
            data.Sources.Add("/sys/class/power_supply");
            test.Items.AddRange(BatteryHealth.Evaluate(data));
        }

        if (found)
        {
            test.Summary = BatteryHealth.Summarize(test.Items);
        }
        return found;
    }

    public static void Devices(DiagnosticTestResult test)
    {
        var (exit, output) = Run("journalctl", "-k -p 3 -b --no-pager -q -o cat", 20);
        if (exit < 0)
        {
            test.Items.Add(Item("Erros do kernel", "journalctl indisponível", DiagnosticStatus.Info));
            return;
        }

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        test.Items.Add(Item("Erros do kernel desde o último boot", lines.Length == 0 ? "Nenhum" : lines.Length.ToString(CultureInfo.InvariantCulture),
            lines.Length > 20 ? DiagnosticStatus.Warning : DiagnosticStatus.Ok));

        foreach (var line in lines.Where(x =>
                     x.Contains("I/O error", StringComparison.OrdinalIgnoreCase) ||
                     x.Contains("Hardware Error", StringComparison.OrdinalIgnoreCase) ||
                     x.Contains("mce", StringComparison.OrdinalIgnoreCase))
                 .Distinct()
                 .Take(10))
        {
            test.Items.Add(Item("Erro de hardware", line.Length > 160 ? line[..160] : line, DiagnosticStatus.Error, group: "Mensagens"));
        }
    }

    public static void SystemHealth(DiagnosticTestResult test)
    {
        var rebootRequired = File.Exists("/var/run/reboot-required");
        var rebootItem = Item("Reinício pendente", rebootRequired ? "Sim" : "Não",
            rebootRequired ? DiagnosticStatus.Warning : DiagnosticStatus.Ok,
            rebootRequired ? "Reinicie para concluir atualizações do kernel ou bibliotecas." : null);
        if (rebootRequired)
        {
            rebootItem.WithRepair(DiagnosticRepairs.Reboot);
        }
        test.Items.Add(rebootItem);

        var updates = Read("/var/lib/update-notifier", "updates-available");
        if (!string.IsNullOrWhiteSpace(updates))
        {
            var first = updates.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            if (first is not null)
            {
                test.Items.Add(Item("Atualizações", first, first.StartsWith("0 ") ? DiagnosticStatus.Ok : DiagnosticStatus.Warning));
            }
        }
    }

    public static (double? EffectiveMhz, double? BaseMhz) CpuFrequency()
    {
        var cpuInfo = Read("/proc", "cpuinfo");
        if (cpuInfo is null)
        {
            return (null, null);
        }

        var values = cpuInfo.Split('\n')
            .Where(x => x.StartsWith("cpu MHz", StringComparison.Ordinal))
            .Select(x => double.TryParse(x.Split(':').Last().Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0)
            .Where(x => x > 0)
            .ToList();

        var baseKhz = ToLong(Read("/sys/devices/system/cpu/cpu0/cpufreq", "base_frequency")) ??
            ToLong(Read("/sys/devices/system/cpu/cpu0/cpufreq", "cpuinfo_max_freq"));

        return (values.Count == 0 ? null : values.Average(), baseKhz is > 0 ? baseKhz / 1000.0 : null);
    }

    public static double? CpuTemperature()
    {
        const string root = "/sys/class/thermal";
        if (!Directory.Exists(root))
        {
            return null;
        }

        double? max = null;
        foreach (var zone in Directory.GetDirectories(root, "thermal_zone*"))
        {
            var type = Read(zone, "type") ?? string.Empty;
            if (ToLong(Read(zone, "temp")) is > 0 and var milli &&
                (type.Contains("pkg", StringComparison.OrdinalIgnoreCase) ||
                 type.Contains("cpu", StringComparison.OrdinalIgnoreCase) ||
                 type.Contains("acpitz", StringComparison.OrdinalIgnoreCase)))
            {
                var celsius = milli / 1000.0;
                max = max is null ? celsius : Math.Max(max.Value, celsius);
            }
        }
        return max;
    }

    public static string? WifiSignal()
    {
        // /proc/net/wireless: "wlp0s20f3: 0000   58.  -52.  -256 ..." (qualidade de 0 a 70).
        var text = Read("/proc/net", "wireless");
        var line = text?.Split('\n').Skip(2).FirstOrDefault(x => x.Contains(':'));
        if (line is null)
        {
            return null;
        }
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 2 && double.TryParse(parts[2].TrimEnd('.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var quality)
            ? $"{Math.Min(100, quality * 100 / 70):0}"
            : null;
    }

    private static string? Read(string dir, string file)
    {
        try
        {
            var path = Path.Combine(dir, file);
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}
