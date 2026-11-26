using Microsoft.Win32;
using Remotely.Shared.Models.Diagnostics;
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using static Remotely.Agent.Services.Diagnostics.DiagnosticHelpers;

namespace Remotely.Agent.Services.Diagnostics;

/// <summary>Testes que dependem do Windows (WMI, registro e log de eventos).</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsDiagnostics
{
    private const string StorageScope = @"root\Microsoft\Windows\Storage";

    // -----------------------------------------------------------------
    // Discos (SMART)
    // -----------------------------------------------------------------

    public static void Disks(DiagnosticTestResult test)
    {
        var disks = Wmi("SELECT DeviceId, ObjectId, FriendlyName, MediaType, BusType, HealthStatus, SpindleSpeed FROM MSFT_PhysicalDisk", StorageScope);
        var counters = Wmi("SELECT DeviceId, Temperature, TemperatureMax, Wear, ReadErrorsUncorrected, WriteErrorsUncorrected, ReadErrorsCorrected, WriteErrorsCorrected, PowerOnHours, StartStopCycleCount FROM MSFT_StorageReliabilityCounter", StorageScope);

        // SMART dos discos SATA pelo driver (bytes crus de atributos e limites), ligados ao índice do disco.
        var drives = Wmi("SELECT Index, PNPDeviceID FROM Win32_DiskDrive");
        var ataData = Wmi("SELECT InstanceName, VendorSpecific FROM MSStorageDriver_ATAPISmartData", @"root\wmi");
        var ataThresholds = Wmi("SELECT InstanceName, VendorSpecific FROM MSStorageDriver_FailurePredictThresholds", @"root\wmi");
        var predicts = Wmi("SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus", @"root\wmi");

        foreach (var disk in disks)
        {
            var name = ToText(Get(disk, "FriendlyName")) ?? "Disco";
            var deviceId = ToText(Get(disk, "DeviceId"));
            var mediaType = ToLong(Get(disk, "MediaType"));
            var busType = ToLong(Get(disk, "BusType"));
            var isNvme = busType == 17;
            var isHdd = mediaType == 3 || ToLong(Get(disk, "SpindleSpeed")) is > 0 and < uint.MaxValue;
            var kind = isNvme ? "NVMe" : isHdd ? "HD" : mediaType == 4 ? "SSD" : null;

            var data = new DiskHealthData
            {
                Group = kind is null ? name : $"{name} ({kind})",
                IsHdd = isHdd,
                IsNvme = isNvme,
                IsExternal = busType == 7
            };

            if (ToLong(Get(disk, "HealthStatus")) is >= 0 and <= 2 and var health)
            {
                data.WindowsHealth = (int)health;
            }

            var diskIndex = int.TryParse(deviceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ? index : -1;

            // 1) Leitura direta: é a fonte mais precisa e vale mais que o resumo do Windows.
            if (isNvme && diskIndex >= 0 && WindowsSmart.TryReadNvmeHealthLog(diskIndex) is { } nvmeLog)
            {
                DiskHealth.TryParseNvmeHealthLog(nvmeLog, data);
            }
            else if (diskIndex >= 0 && FindByDrive(drives, ataData, diskIndex) is { } ataRow &&
                     Get(ataRow, "VendorSpecific") is byte[] attributes)
            {
                var thresholds = FindByDrive(drives, ataThresholds, diskIndex) is { } thresholdRow &&
                                 Get(thresholdRow, "VendorSpecific") is byte[] limits
                    ? limits
                    : Array.Empty<byte>();
                DiskHealth.ApplyAtaAttributes(DiskHealth.ParseAtaAttributes(attributes, thresholds), data);
            }

            if (diskIndex >= 0 && FindByDrive(drives, predicts, diskIndex) is { } predict &&
                ToBool(Get(predict, "PredictFailure")) == true)
            {
                data.PredictFailure = true;
            }

            // 2) Contadores do Windows completam o que faltou.
            var counter = counters.FirstOrDefault(x => ToText(Get(x, "DeviceId")) == deviceId)
                ?? GetReliabilityCounter(ToText(Get(disk, "ObjectId")));
            if (counter is not null)
            {
                data.Sources.Add("contadores do Windows");
                data.TemperatureC ??= ToLong(Get(counter, "Temperature")) is > 0 and < 120 and var temperature ? (int)temperature : null;
                data.TemperatureLimitC = ToLong(Get(counter, "TemperatureMax")) is > 0 and < 120 and var max ? (int)max : null;
                if (!isHdd)
                {
                    data.PercentUsed ??= ToLong(Get(counter, "Wear")) is >= 0 and <= 255 and var wear ? (int)wear : null;
                }
                data.ReadErrorsUncorrected = ToLong(Get(counter, "ReadErrorsUncorrected"));
                data.WriteErrorsUncorrected = ToLong(Get(counter, "WriteErrorsUncorrected"));
                var corrected = (ToLong(Get(counter, "ReadErrorsCorrected")) ?? 0) + (ToLong(Get(counter, "WriteErrorsCorrected")) ?? 0);
                data.ReadWriteErrorsCorrected = corrected > 0 ? corrected : null;
                data.PowerOnHours ??= ToLong(Get(counter, "PowerOnHours")) is > 0 and var hours ? hours : null;
                data.PowerCycles ??= ToLong(Get(counter, "StartStopCycleCount")) is > 0 and var cycles ? cycles : null;
            }

            test.Items.AddRange(DiskHealth.Evaluate(data));
        }
    }

    /// <summary>Linha de uma classe MSStorageDriver_* (InstanceName = PNPDeviceID + "_0") do disco N.</summary>
    private static Dictionary<string, object?>? FindByDrive(
        List<Dictionary<string, object?>> drives,
        List<Dictionary<string, object?>> rows,
        int diskIndex)
    {
        var pnp = drives.FirstOrDefault(x => ToLong(Get(x, "Index")) == diskIndex) is { } drive
            ? ToText(Get(drive, "PNPDeviceID"))
            : null;
        if (string.IsNullOrWhiteSpace(pnp))
        {
            return null;
        }

        return rows.FirstOrDefault(x =>
            ToText(Get(x, "InstanceName")) is { } instance &&
            instance.StartsWith(pnp, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, object?>? GetReliabilityCounter(string? objectId)
    {
        if (string.IsNullOrWhiteSpace(objectId))
        {
            return null;
        }

        var escaped = objectId.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return Wmi(
            $"ASSOCIATORS OF {{MSFT_PhysicalDisk.ObjectId=\"{escaped}\"}} WHERE AssocClass = MSFT_PhysicalDiskToStorageReliabilityCounter",
            StorageScope).FirstOrDefault();
    }

    // -----------------------------------------------------------------
    // Bateria
    // -----------------------------------------------------------------

    public static bool Battery(DiagnosticTestResult test)
    {
        var staticData = Wmi("SELECT DeviceName, DesignedCapacity FROM BatteryStaticData", @"root\wmi");
        var full = Wmi("SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", @"root\wmi");
        var cycles = Wmi("SELECT CycleCount FROM BatteryCycleCount", @"root\wmi");
        var status = Wmi("SELECT RemainingCapacity, Charging, PowerOnline FROM BatteryStatus", @"root\wmi");

        // O relatório do powercfg traz fabricante, química, histórico de capacidade e autonomia estimada.
        var batteries = ReadPowercfgBatteryReport();

        var count = Math.Max(staticData.Count, batteries.Count);
        if (count == 0)
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            var data = i < batteries.Count ? batteries[i] : new BatteryHealthData { Group = $"Bateria {i + 1}" };

            if (i < staticData.Count)
            {
                if (data.Group.StartsWith("Bateria ", StringComparison.Ordinal) && ToText(Get(staticData[i], "DeviceName")) is { } deviceName)
                {
                    data.Group = deviceName;
                }
                // WMI é a leitura do momento; o relatório pode ser de alguns minutos atrás.
                if (ToLong(Get(staticData[i], "DesignedCapacity")) is > 0 and var design)
                {
                    data.DesignCapacity = design;
                    data.RelativeUnits = false;
                }
                if (i < full.Count && ToLong(Get(full[i], "FullChargedCapacity")) is > 0 and var fullCharge)
                {
                    data.FullChargeCapacity = fullCharge;
                }
                if (i < cycles.Count && ToLong(Get(cycles[i], "CycleCount")) is > 0 and var cycleCount)
                {
                    data.CycleCount = cycleCount;
                }
                data.Sources.Add("WMI");
            }

            if (i < status.Count)
            {
                var remaining = ToLong(Get(status[i], "RemainingCapacity"));
                var charging = ToBool(Get(status[i], "Charging"));
                var online = ToBool(Get(status[i], "PowerOnline"));
                if (remaining is > 0 && data.FullChargeCapacity is > 0)
                {
                    data.ChargePercent = (int)Math.Round(Math.Min(100, 100.0 * remaining.Value / data.FullChargeCapacity.Value));
                }
                data.PowerState = charging == true ? "carregando" : online == true ? "na tomada" : "na bateria";
            }

            test.Items.AddRange(BatteryHealth.Evaluate(data));
        }

        test.Summary = BatteryHealth.Summarize(test.Items);
        return true;
    }

    private static List<BatteryHealthData> ReadPowercfgBatteryReport()
    {
        var path = Path.Combine(Path.GetTempPath(), $"remotely-battery-{Guid.NewGuid():N}.xml");
        try
        {
            var powercfg = Path.Combine(Environment.SystemDirectory, "powercfg.exe");
            var (exitCode, _) = Run(powercfg, $"/batteryreport /xml /output \"{path}\"", 45);
            if (exitCode != 0 || !File.Exists(path))
            {
                return new();
            }
            return BatteryHealth.ParsePowercfgXml(File.ReadAllText(path));
        }
        catch
        {
            return new();
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // Arquivo temporário; o Windows limpa depois.
            }
        }
    }

    // -----------------------------------------------------------------
    // Dispositivos com erro e eventos
    // -----------------------------------------------------------------

    public static void Devices(DiagnosticTestResult test)
    {
        var problems = Wmi("SELECT Name, ConfigManagerErrorCode, PNPClass, PNPDeviceID FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0");
        foreach (var device in problems)
        {
            var code = ToLong(Get(device, "ConfigManagerErrorCode")) ?? 0;
            var (description, status) = code switch
            {
                22 => ("Desativado", DiagnosticStatus.Info),
                28 => ("Driver não instalado", DiagnosticStatus.Warning),
                1 or 3 or 18 or 19 or 31 or 32 or 37 or 39 or 41 or 48 or 52 => ("Problema no driver", DiagnosticStatus.Error),
                10 => ("O dispositivo não pode ser iniciado", DiagnosticStatus.Error),
                12 => ("Conflito de recursos", DiagnosticStatus.Error),
                14 or 21 => ("Precisa reiniciar o computador", DiagnosticStatus.Warning),
                24 or 45 => ("Não está presente ou desconectado", DiagnosticStatus.Info),
                43 => ("O Windows parou o dispositivo (falha relatada)", DiagnosticStatus.Error),
                _ => ($"Erro {code}", DiagnosticStatus.Warning)
            };

            var item = Item(
                ToText(Get(device, "Name")) ?? "Dispositivo",
                $"{description} (código {code})",
                status,
                status == DiagnosticStatus.Error ? "Reinstale ou atualize o driver; se continuar, pode ser defeito de hardware." : null,
                "Dispositivos com problema");
            var instanceId = ToText(Get(device, "PNPDeviceID"));
            if (code is 14 or 21)
            {
                item.WithRepair(DiagnosticRepairs.Reboot);
            }
            else if (code == 28)
            {
                item.WithRepair(DiagnosticRepairs.DeviceRescan);
            }
            else if (status != DiagnosticStatus.Info && instanceId is not null &&
                     DiagnosticRepairs.NormalizeTarget(DiagnosticRepairs.Get(DiagnosticRepairs.DeviceRestart)!, instanceId) is { } target)
            {
                item.WithRepair(DiagnosticRepairs.DeviceRestart, target);
            }
            test.Items.Add(item);
        }

        if (!test.Items.Any(x => x.Group == "Dispositivos com problema"))
        {
            test.Items.Add(Item("Gerenciador de Dispositivos", "Nenhum dispositivo com erro", DiagnosticStatus.Ok, group: "Dispositivos com problema"));
        }

        const string group = "Log de eventos (últimos 30 dias)";
        AddEventCount(test, group, "Telas azuis", "System",
            "*[System[(Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] or Provider[@Name='BugCheck']) and (EventID=1001) and TimeCreated[timediff(@SystemTime) <= 2592000000]]]",
            warnAt: 1, errorAt: 3, "Telas azuis indicam driver ou hardware com defeito (memória, disco, superaquecimento).");

        AddEventCount(test, group, "Desligamentos inesperados", "System",
            "*[System[Provider[@Name='Microsoft-Windows-Kernel-Power'] and (EventID=41) and TimeCreated[timediff(@SystemTime) <= 2592000000]]]",
            warnAt: 2, errorAt: 5, "Quedas de energia, bateria ou fonte com problema, ou travamentos.");

        AddEventCount(test, group, "Erros de hardware (WHEA)", "System",
            "*[System[Provider[@Name='Microsoft-Windows-WHEA-Logger'] and (Level=1 or Level=2 or Level=3) and TimeCreated[timediff(@SystemTime) <= 2592000000]]]",
            warnAt: 1, errorAt: 5, "O processador, a memória ou o barramento PCIe relataram erros de hardware.");

        DiskErrors(test, group);

        AddEventCount(test, group, "Eventos críticos (7 dias)", "System",
            "*[System[(Level=1) and TimeCreated[timediff(@SystemTime) <= 604800000]]]",
            warnAt: 1, errorAt: 5, null);
    }

    /// <summary>
    /// Erros de disco do log do sistema, separados por unidade. Pen drive, HD externo ou cartão
    /// retirados sem "Ejetar" geram centenas de eventos e não indicam problema no computador.
    /// </summary>
    private static void DiskErrors(DiagnosticTestResult test, string group)
    {
        const int cap = 2000;
        var events = new List<(int? Disk, char? Letter, int Id, DateTime Time)>();
        try
        {
            var query = new EventLogQuery("System", PathType.LogName,
                "*[System[(Provider[@Name='disk'] or Provider[@Name='Ntfs'] or Provider[@Name='Microsoft-Windows-Ntfs'] or Provider[@Name='stornvme'] or Provider[@Name='storahci']) and (Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) <= 2592000000]]]")
            {
                ReverseDirection = true
            };
            using var reader = new EventLogReader(query);
            for (var record = reader.ReadEvent(); record is not null && events.Count < cap; record = reader.ReadEvent())
            {
                using (record)
                {
                    var text = string.Join(" ", record.Properties.Select(x => x.Value?.ToString()));
                    var diskMatch = Regex.Match(text, @"Harddisk(\d+)\\DR", RegexOptions.IgnoreCase);
                    var letterMatch = Regex.Match(text, @"(?:^|[\s,:])([A-Z]):(?:$|[\s,\\])");
                    events.Add((
                        diskMatch.Success ? int.Parse(diskMatch.Groups[1].Value, CultureInfo.InvariantCulture) : null,
                        letterMatch.Success ? letterMatch.Groups[1].Value[0] : null,
                        record.Id,
                        record.TimeCreated ?? DateTime.MinValue));
                }
            }
        }
        catch (Exception ex)
        {
            test.Items.Add(Item("Erros de disco", $"Não foi possível ler o log ({ex.GetType().Name})", DiagnosticStatus.Info, group: group));
            return;
        }

        if (events.Count == 0)
        {
            test.Items.Add(Item("Erros de disco", "Nenhum", DiagnosticStatus.Ok, group: group));
            return;
        }

        // Discos atuais e a letra de cada partição.
        var disks = Wmi("SELECT Number, FriendlyName, BusType, IsBoot, IsSystem FROM MSFT_Disk", StorageScope)
            .Where(x => ToLong(Get(x, "Number")) is not null)
            .ToDictionary(x => (int)ToLong(Get(x, "Number"))!.Value);
        var letters = new Dictionary<char, int>();
        foreach (var partition in Wmi("SELECT DiskNumber, DriveLetter FROM MSFT_Partition", StorageScope))
        {
            var letter = Get(partition, "DriveLetter") switch
            {
                char c => c,
                ushort u => (char)u,
                int i => (char)i,
                string { Length: > 0 } text => text[0],
                _ => '\0'
            };
            if (char.IsAsciiLetter(letter) && ToLong(Get(partition, "DiskNumber")) is { } number)
            {
                letters[char.ToUpperInvariant(letter)] = (int)number;
            }
        }

        var systemLetter = (Path.GetPathRoot(Environment.SystemDirectory) ?? "C:")[0];
        var recentLimit = DateTime.Now.AddDays(-7);

        var groups = events
            .GroupBy(x => x.Disk ?? (x.Letter is { } l && letters.TryGetValue(l, out var n) ? n : x.Letter is null ? -1 : -2 - x.Letter.Value))
            .OrderByDescending(x => x.Count());

        foreach (var diskEvents in groups)
        {
            var count = diskEvents.Count();
            var recent = diskEvents.Count(x => x.Time >= recentLimit);
            var last = diskEvents.Max(x => x.Time);
            var ids = string.Join(", ", diskEvents.Select(x => x.Id).Distinct().OrderBy(x => x).Take(4));
            var value = $"{count}{(events.Count >= cap ? "+" : "")} (último em {last:dd/MM/yyyy HH:mm}; {recent} nos últimos 7 dias)";

            disks.TryGetValue(diskEvents.Key, out var disk);
            var bus = ToLong(Get(disk, "BusType"));
            var isSystem = ToBool(Get(disk, "IsBoot")) == true || ToBool(Get(disk, "IsSystem")) == true;
            var removedLetter = diskEvents.Key <= -2 ? (char?)(-2 - diskEvents.Key) : null;

            // 7 = USB, 12 = SD, 13 = MMC. Unidade que nem existe mais também foi removível.
            var external = bus is 7 or 12 or 13 || (diskEvents.Key >= 0 && disk is null) || removedLetter is not null;
            if (external)
            {
                var name = ToText(Get(disk, "FriendlyName")) ?? (removedLetter is { } r ? $"unidade {r}: (removida)" : "unidade removida");
                // Só informativo: não afeta o Windows e não há o que reparar no computador.
                test.Items.Add(Item(
                    $"Erros de disco: {name} (externa)",
                    value,
                    DiagnosticStatus.Info,
                    $"Pen drive, HD externo ou cartão (eventos {ids}). Normalmente é retirada sem \"Ejetar\" ou mídia com defeito. Não afeta o computador; se a unidade for importante, teste-a em outra máquina.",
                    group));
                continue;
            }

            var diskName = ToText(Get(disk, "FriendlyName")) ?? (diskEvents.Key == -1 ? "controladora/sem unidade identificada" : $"disco {diskEvents.Key}");
            var driveLetter = letters.Where(x => x.Value == diskEvents.Key).Select(x => (char?)x.Key)
                .OrderByDescending(x => x == systemLetter).FirstOrDefault() ?? (diskEvents.Key == -1 ? systemLetter : null);

            var status = recent >= 10 && (isSystem || diskEvents.Key == -1 && driveLetter == systemLetter)
                ? DiagnosticStatus.Error
                : recent > 0 || count >= 10 ? DiagnosticStatus.Warning : DiagnosticStatus.Info;
            var item = Item(
                $"Erros de disco: {diskName}{(isSystem ? " (sistema)" : "")}",
                value,
                status,
                status == DiagnosticStatus.Info
                    ? $"Poucos eventos antigos (eventos {ids}). Acompanhe."
                    : $"Eventos {ids}: blocos defeituosos, cabo/conexão ou controladora. Verifique o disco e faça backup se continuar.",
                group);
            if (driveLetter is { } letterToScan && status != DiagnosticStatus.Info)
            {
                item.WithRepair(DiagnosticRepairs.DiskScan, $"{letterToScan}:");
            }
            test.Items.Add(item);
        }
    }

    private static void AddEventCount(
        DiagnosticTestResult test,
        string group,
        string label,
        string logName,
        string xpath,
        int warnAt,
        int errorAt,
        string? hint)
    {
        try
        {
            var count = 0;
            DateTime? last = null;
            var query = new EventLogQuery(logName, PathType.LogName, xpath) { ReverseDirection = true };
            using var reader = new EventLogReader(query);
            for (var record = reader.ReadEvent(); record is not null && count < 500; record = reader.ReadEvent())
            {
                using (record)
                {
                    count++;
                    last ??= record.TimeCreated;
                }
            }

            var status = count >= errorAt ? DiagnosticStatus.Error : count >= warnAt ? DiagnosticStatus.Warning : DiagnosticStatus.Ok;
            test.Items.Add(Item(
                label,
                count == 0 ? "Nenhum" : $"{count}{(last is { } date ? $" (último em {date:dd/MM/yyyy HH:mm})" : "")}",
                status,
                status != DiagnosticStatus.Ok ? hint : null,
                group));
        }
        catch (Exception ex)
        {
            test.Items.Add(Item(label, $"Não foi possível ler o log ({ex.GetType().Name})", DiagnosticStatus.Info, group: group));
        }
    }

    // -----------------------------------------------------------------
    // Sistema
    // -----------------------------------------------------------------

    public static void SystemHealth(DiagnosticTestResult test)
    {
        var pending = new List<string>();
        using (var cbs = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending"))
        {
            if (cbs is not null) pending.Add("componentes do Windows");
        }
        using (var wu = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"))
        {
            if (wu is not null) pending.Add("Windows Update");
        }
        string? fileDetails = null;
        var onlyTempCleanup = false;
        using (var session = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager"))
        {
            if (session?.GetValue("PendingFileRenameOperations") is string[] { Length: > 0 } operations)
            {
                pending.Add("arquivos a substituir");

                // Pares (origem, destino); destino vazio = apagar no próximo início.
                var sources = new List<(string Path, bool Delete)>();
                for (var i = 0; i < operations.Length; i += 2)
                {
                    var source = operations[i].Replace(@"\??\", string.Empty).Trim();
                    if (source.Length == 0)
                    {
                        continue;
                    }
                    var destination = i + 1 < operations.Length ? operations[i + 1].Trim() : string.Empty;
                    sources.Add((source, destination.Length == 0));
                }

                // Só arquivos para apagar no próximo início (sobras de instaladores e agentes):
                // não muda nada no sistema, então não é motivo para reiniciar.
                onlyTempCleanup = sources.Count > 0 && sources.All(x => x.Delete);
                var names = sources.Select(x => Path.GetFileName(x.Path.TrimEnd('\\')))
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                fileDetails = $"{sources.Count} arquivo(s){(onlyTempCleanup ? " só para apagar" : "")}: " +
                    string.Join(", ", names.Take(5)) + (names.Count > 5 ? $" e mais {names.Count - 5}" : "");
            }
        }

        // Só temporários para apagar no próximo início: não é motivo para reiniciar agora.
        var rebootStatus = pending.Count == 0 ? DiagnosticStatus.Ok
            : pending.Count == 1 && onlyTempCleanup ? DiagnosticStatus.Info
            : DiagnosticStatus.Warning;
        var rebootItem = Item(
            "Reinício pendente",
            pending.Count == 0 ? "Não"
                : rebootStatus == DiagnosticStatus.Info ? "Não (só arquivos para apagar no próximo início)"
                : $"Sim ({string.Join(", ", pending)})",
            rebootStatus,
            pending.Count == 0 ? null
                : rebootStatus == DiagnosticStatus.Info ? $"Sobras de instaladores ou agentes; o Windows apaga sozinho no próximo reinício normal. {fileDetails}"
                : $"Reinicie para concluir atualizações e instalações.{(fileDetails is null ? "" : $" {fileDetails}")}");
        if (rebootStatus == DiagnosticStatus.Warning)
        {
            rebootItem.WithRepair(DiagnosticRepairs.Reboot);
        }
        test.Items.Add(rebootItem);

        // Última atualização instalada.
        var hotfixDates = Wmi("SELECT InstalledOn FROM Win32_QuickFixEngineering")
            .Select(x => ToText(Get(x, "InstalledOn")))
            .Select(ParseHotfixDate)
            .Where(x => x is not null)
            .Select(x => x!.Value)
            .ToList();
        if (hotfixDates.Count > 0)
        {
            var last = hotfixDates.Max();
            var days = (DateTime.Now - last).TotalDays;
            var updateItem = Item(
                "Última atualização do Windows",
                $"{last:dd/MM/yyyy} ({(int)days} dias)",
                days > 90 ? DiagnosticStatus.Error : days > 45 ? DiagnosticStatus.Warning : DiagnosticStatus.Ok,
                days > 45 ? "O Windows está sem atualizações há muito tempo. Verifique o Windows Update." : null);
            if (days > 45)
            {
                updateItem.WithRepair(DiagnosticRepairs.WindowsUpdateReset);
            }
            test.Items.Add(updateItem);
        }

        // Antivírus.
        var products = Wmi("SELECT displayName, productState FROM AntiVirusProduct", @"root\SecurityCenter2");
        var active = products
            .Select(x => (Name: ToText(Get(x, "displayName")), State: ToLong(Get(x, "productState")) ?? 0))
            .Where(x => ((x.State >> 12) & 0xF) == 1)
            .ToList();
        if (active.Count > 0)
        {
            foreach (var (name, state) in active)
            {
                var upToDate = ((state >> 4) & 0xF) == 0;
                test.Items.Add(Item("Antivírus", $"{name} — {(upToDate ? "ativo e atualizado" : "ativo, mas desatualizado")}",
                    upToDate ? DiagnosticStatus.Ok : DiagnosticStatus.Warning,
                    upToDate ? null : "Atualize as definições do antivírus."));
            }
        }
        else
        {
            var defender = Wmi("SELECT RealTimeProtectionEnabled, AntivirusSignatureAge FROM MSFT_MpComputerStatus", @"root\Microsoft\Windows\Defender").FirstOrDefault();
            var enabled = ToBool(Get(defender, "RealTimeProtectionEnabled"));
            var age = ToLong(Get(defender, "AntivirusSignatureAge"));
            if (enabled == true)
            {
                var defenderItem = Item("Antivírus", $"Microsoft Defender — assinaturas de {age ?? 0} dia(s)",
                    age > 7 ? DiagnosticStatus.Warning : DiagnosticStatus.Ok,
                    age > 7 ? "Definições do antivírus antigas." : null);
                if (age > 7)
                {
                    defenderItem.WithRepair(DiagnosticRepairs.DefenderUpdate);
                }
                test.Items.Add(defenderItem);
            }
            else
            {
                test.Items.Add(Item("Antivírus", products.Count > 0 || defender is not null ? "Nenhum antivírus ativo" : "Não identificado",
                    products.Count > 0 || defender is not null ? DiagnosticStatus.Error : DiagnosticStatus.Info,
                    "Sem proteção em tempo real."));
            }
        }

        // Firewall.
        var profiles = Wmi("SELECT Name, Enabled FROM MSFT_NetFirewallProfile", @"root\StandardCimv2");
        if (profiles.Count > 0)
        {
            var disabled = profiles.Where(x => ToLong(Get(x, "Enabled")) is 0).Select(x => ToText(Get(x, "Name"))).ToList();
            var firewallItem = Item("Firewall do Windows",
                disabled.Count == 0 ? "Ativo em todos os perfis" : $"Desativado: {string.Join(", ", disabled)}",
                disabled.Count == 0 ? DiagnosticStatus.Ok : DiagnosticStatus.Warning);
            if (disabled.Count > 0)
            {
                firewallItem.WithRepair(DiagnosticRepairs.FirewallEnable);
            }
            test.Items.Add(firewallItem);
        }

        // BitLocker no disco do sistema.
        var systemDrive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";
        var volume = Wmi($"SELECT DriveLetter, ProtectionStatus FROM Win32_EncryptableVolume WHERE DriveLetter = '{systemDrive}'",
            @"root\cimv2\Security\MicrosoftVolumeEncryption").FirstOrDefault();
        if (volume is not null)
        {
            var protection = ToLong(Get(volume, "ProtectionStatus"));
            test.Items.Add(Item($"BitLocker ({systemDrive})", protection == 1 ? "Ativo" : "Desligado",
                protection == 1 ? DiagnosticStatus.Ok : DiagnosticStatus.Info));
        }

        // Ativação.
        var license = Wmi("SELECT LicenseStatus FROM SoftwareLicensingProduct WHERE ApplicationID = '55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL").FirstOrDefault();
        if (ToLong(Get(license, "LicenseStatus")) is { } licenseStatus)
        {
            test.Items.Add(Item("Ativação do Windows", licenseStatus == 1 ? "Ativado" : "Não ativado",
                licenseStatus == 1 ? DiagnosticStatus.Ok : DiagnosticStatus.Warning));
        }

        // Resultado do último Diagnóstico de Memória.
        try
        {
            var query = new EventLogQuery("System", PathType.LogName,
                "*[System[Provider[@Name='Microsoft-Windows-MemoryDiagnostics-Results']]]") { ReverseDirection = true };
            using var reader = new EventLogReader(query);
            using var record = reader.ReadEvent();
            if (record is null)
            {
                test.Items.Add(Item("Último teste de memória", "Nunca executado"));
            }
            else
            {
                var hasErrors = record.Id is 1102 or 1202;
                test.Items.Add(Item(
                    "Último teste de memória",
                    $"{(hasErrors ? "Encontrou erros" : "Sem erros")} em {record.TimeCreated:dd/MM/yyyy HH:mm}",
                    hasErrors ? DiagnosticStatus.Error : DiagnosticStatus.Ok,
                    hasErrors ? "Memória RAM com defeito: troque o módulo." : null));
            }
        }
        catch
        {
            // Sem acesso ao log.
        }
    }

    private static DateTime? ParseHotfixDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string[] formats = ["M/d/yyyy", "MM/dd/yyyy", "d/M/yyyy", "yyyyMMdd", "yyyy-MM-dd"];
        if (DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) &&
            date <= DateTime.Now.AddDays(1))
        {
            return date;
        }

        // Alguns sistemas gravam um valor hexadecimal (FILETIME).
        if (long.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var fileTime) && fileTime > 0)
        {
            try
            {
                return DateTime.FromFileTime(fileTime);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    // -----------------------------------------------------------------
    // CPU: frequência efetiva e temperatura (usadas no estresse)
    // -----------------------------------------------------------------

    public static (double? EffectiveMhz, double? BaseMhz) CpuFrequency()
    {
        var info = Wmi("SELECT PercentProcessorPerformance, ProcessorFrequency FROM Win32_PerfFormattedData_Counters_ProcessorInformation WHERE Name = '_Total'").FirstOrDefault();
        var baseMhz = ToLong(Get(info, "ProcessorFrequency"));
        var percent = ToLong(Get(info, "PercentProcessorPerformance"));
        if (baseMhz is > 0 && percent is > 0)
        {
            return (baseMhz.Value * percent.Value / 100.0, baseMhz);
        }

        var cpu = Wmi("SELECT CurrentClockSpeed, MaxClockSpeed FROM Win32_Processor").FirstOrDefault();
        return (ToLong(Get(cpu, "CurrentClockSpeed")), ToLong(Get(cpu, "MaxClockSpeed")));
    }

    public static double? CpuTemperature()
    {
        // Em décimos de Kelvin. Muitos equipamentos não expõem esta classe.
        var zones = Wmi("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", @"root\wmi");
        var values = zones
            .Select(x => ToLong(Get(x, "CurrentTemperature")))
            .Where(x => x is > 2732 and < 4000)
            .Select(x => (x!.Value - 2732) / 10.0)
            .ToList();
        return values.Count == 0 ? null : values.Max();
    }

    public static string? WifiSignal()
    {
        var (exitCode, output) = Run("netsh", "wlan show interfaces", 10);
        if (exitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        // "Signal : 85%" (inglês) ou "Sinal : 85%" (português).
        var match = Regex.Match(output, @"(?:Signal|Sinal)\s*:\s*(\d+)\s*%", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    // -----------------------------------------------------------------
    // Teste de memória
    // -----------------------------------------------------------------

    /// <summary>Agenda o teste e reinicia em 2 minutos. Devolve true se o reinício foi agendado.</summary>
    public static bool ScheduleMemoryTest(DiagnosticTestResult test)
    {
        var system32 = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess ? "Sysnative" : "System32");

        var (bcdExit, bcdOutput) = Run(Path.Combine(system32, "bcdedit.exe"), "/bootsequence {memdiag}", 30);
        if (bcdExit != 0)
        {
            test.Status = DiagnosticStatus.Error;
            test.Summary = "Não foi possível agendar o teste de memória.";
            test.Items.Add(Item("bcdedit", bcdOutput.Trim(), DiagnosticStatus.Error));
            return false;
        }

        var (shutdownExit, shutdownOutput) = Run(
            Path.Combine(system32, "shutdown.exe"),
            "/r /t 120 /c \"O suporte de TI agendou um teste de memória. O computador vai reiniciar em 2 minutos. Salve seu trabalho.\"",
            30);

        if (shutdownExit != 0)
        {
            test.Status = DiagnosticStatus.Warning;
            test.Summary = "Teste agendado, mas não foi possível reiniciar. Ele roda no próximo reinício e o resultado aparece aqui.";
            test.Items.Add(Item("Reinício", shutdownOutput.Trim(), DiagnosticStatus.Warning));
            return true;
        }

        test.Status = DiagnosticStatus.Running;
        test.Summary = "Agendado. O computador reinicia em 2 minutos e testa a memória (10 a 20 minutos). O resultado aparece aqui quando ele voltar.";
        test.Items.Add(Item("Agendamento", "Diagnóstico de Memória do Windows no próximo início"));
        test.Items.Add(Item("Reinício", "Em 2 minutos (o usuário foi avisado)"));
        return true;
    }

    /// <summary>Resultado do Diagnóstico de Memória do Windows registrado depois de <paramref name="after"/>.</summary>
    public static (bool HasErrors, DateTime Time)? ReadMemoryResult(DateTime after)
    {
        try
        {
            var query = new EventLogQuery("System", PathType.LogName,
                "*[System[Provider[@Name='Microsoft-Windows-MemoryDiagnostics-Results']]]") { ReverseDirection = true };
            using var reader = new EventLogReader(query);
            using var record = reader.ReadEvent();
            if (record?.TimeCreated is { } time && time > after)
            {
                return (record.Id is 1102 or 1202, time);
            }
        }
        catch
        {
            // Sem acesso ao log.
        }
        return null;
    }
}
