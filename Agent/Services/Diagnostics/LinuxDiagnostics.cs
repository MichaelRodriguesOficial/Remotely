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
            var root = doc.RootElement;
            var group = root.TryGetProperty("model_name", out var model) ? $"{model.GetString()} ({name})" : name;

            if (root.TryGetProperty("smart_status", out var status) && status.TryGetProperty("passed", out var passed))
            {
                test.Items.Add(passed.GetBoolean()
                    ? Item("Saúde (SMART)", "Aprovado", DiagnosticStatus.Ok, group: group)
                    : Item("Saúde (SMART)", "Reprovado", DiagnosticStatus.Error, "Faça backup e troque o disco.", group));
            }

            if (root.TryGetProperty("temperature", out var temp) && temp.TryGetProperty("current", out var current))
            {
                var celsius = current.GetInt32();
                test.Items.Add(Item("Temperatura", $"{celsius} °C",
                    celsius >= 70 ? DiagnosticStatus.Error : celsius >= 60 ? DiagnosticStatus.Warning : DiagnosticStatus.Ok, group: group));
            }

            if (root.TryGetProperty("power_on_time", out var power) && power.TryGetProperty("hours", out var hours))
            {
                test.Items.Add(Item("Tempo ligado", $"{hours.GetInt64()} horas", group: group));
            }

            if (root.TryGetProperty("nvme_smart_health_information_log", out var nvme))
            {
                if (nvme.TryGetProperty("percentage_used", out var used))
                {
                    var wear = used.GetInt32();
                    test.Items.Add(Item("Desgaste", $"{wear}% da vida útil usada",
                        wear >= 95 ? DiagnosticStatus.Error : wear >= 80 ? DiagnosticStatus.Warning : DiagnosticStatus.Ok, group: group));
                }
                if (nvme.TryGetProperty("media_errors", out var mediaErrors))
                {
                    var errors = mediaErrors.GetInt64();
                    test.Items.Add(Item("Erros de mídia", errors.ToString(CultureInfo.InvariantCulture),
                        errors > 0 ? DiagnosticStatus.Error : DiagnosticStatus.Ok, group: group));
                }
            }

            if (root.TryGetProperty("ata_smart_attributes", out var ata) && ata.TryGetProperty("table", out var table))
            {
                foreach (var attribute in table.EnumerateArray())
                {
                    var id = attribute.TryGetProperty("id", out var idValue) ? idValue.GetInt32() : 0;
                    if (id is not (5 or 187 or 197 or 198))
                    {
                        continue;
                    }
                    var raw = attribute.TryGetProperty("raw", out var rawValue) && rawValue.TryGetProperty("value", out var v) ? v.GetInt64() : 0;
                    var label = id switch
                    {
                        5 => "Setores realocados",
                        187 => "Erros não corrigidos",
                        197 => "Setores pendentes",
                        _ => "Setores irrecuperáveis"
                    };
                    test.Items.Add(Item(label, raw.ToString(CultureInfo.InvariantCulture),
                        raw > 0 ? DiagnosticStatus.Error : DiagnosticStatus.Ok, raw > 0 ? "Disco com setores defeituosos." : null, group));
                }
            }
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
            var group = Read(dir, "model_name") ?? Path.GetFileName(dir);
            var design = ToLong(Read(dir, "energy_full_design")) ?? ToLong(Read(dir, "charge_full_design"));
            var full = ToLong(Read(dir, "energy_full")) ?? ToLong(Read(dir, "charge_full"));
            if (design is > 0 && full is > 0)
            {
                var health = Math.Min(100, 100.0 * full.Value / design.Value);
                test.Items.Add(Item("Saúde", $"{health:0}%",
                    health < 50 ? DiagnosticStatus.Error : health < 70 ? DiagnosticStatus.Warning : DiagnosticStatus.Ok,
                    health < 70 ? "Bateria perdendo capacidade." : null, group));
            }
            if (ToLong(Read(dir, "cycle_count")) is > 0 and var cycles)
            {
                test.Items.Add(Item("Ciclos de carga", cycles.ToString(CultureInfo.InvariantCulture), group: group));
            }
            if (Read(dir, "capacity") is { } capacity)
            {
                test.Items.Add(Item("Carga atual", $"{capacity}% ({Read(dir, "status")})", group: group));
            }
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
