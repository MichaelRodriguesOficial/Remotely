using Remotely.Shared.Models.Diagnostics;
using System.Buffers.Binary;
using System.Text.Json;

namespace Remotely.Shared.Tests;

[TestClass]
public class DiskHealthTests
{
    private static byte[] NvmeLog(
        byte criticalWarning = 0,
        int celsius = 38,
        byte spare = 100,
        byte spareThreshold = 10,
        byte percentUsed = 4,
        ulong unitsWritten = 35_000_000,
        ulong powerCycles = 1200,
        ulong powerOnHours = 3412,
        ulong unsafeShutdowns = 40,
        ulong mediaErrors = 0,
        uint warningTempMinutes = 0)
    {
        var log = new byte[512];
        log[0] = criticalWarning;
        BinaryPrimitives.WriteUInt16LittleEndian(log.AsSpan(1), (ushort)(celsius + 273));
        log[3] = spare;
        log[4] = spareThreshold;
        log[5] = percentUsed;
        BinaryPrimitives.WriteUInt64LittleEndian(log.AsSpan(48), unitsWritten);
        BinaryPrimitives.WriteUInt64LittleEndian(log.AsSpan(112), powerCycles);
        BinaryPrimitives.WriteUInt64LittleEndian(log.AsSpan(128), powerOnHours);
        BinaryPrimitives.WriteUInt64LittleEndian(log.AsSpan(144), unsafeShutdowns);
        BinaryPrimitives.WriteUInt64LittleEndian(log.AsSpan(160), mediaErrors);
        BinaryPrimitives.WriteUInt32LittleEndian(log.AsSpan(192), warningTempMinutes);
        return log;
    }

    private static (byte[] Data, byte[] Thresholds) AtaTables(params (int Id, int Current, int Threshold, long Raw)[] attributes)
    {
        var data = new byte[512];
        var thresholds = new byte[512];
        for (var i = 0; i < attributes.Length; i++)
        {
            var offset = 2 + i * 12;
            var (id, current, threshold, raw) = attributes[i];
            data[offset] = (byte)id;
            data[offset + 3] = (byte)current;
            data[offset + 4] = (byte)current;
            for (var b = 0; b < 6; b++)
            {
                data[offset + 5 + b] = (byte)(raw >> (8 * b));
            }
            thresholds[offset] = (byte)id;
            thresholds[offset + 1] = (byte)threshold;
        }
        return (data, thresholds);
    }

    private static DiagnosticItem Verdict(List<DiagnosticItem> items) =>
        items.Single(x => x.Label == DiskHealth.VerdictLabel);

    [TestMethod]
    public void Nvme_HealthyLog_IsGood()
    {
        var data = new DiskHealthData { Group = "Samsung 980 (NVMe)", IsNvme = true, WindowsHealth = 0 };
        Assert.IsTrue(DiskHealth.TryParseNvmeHealthLog(NvmeLog(), data));

        Assert.AreEqual(38, data.TemperatureC);
        Assert.AreEqual(4, data.PercentUsed);
        Assert.AreEqual(3412L, data.PowerOnHours);
        Assert.AreEqual(40L, data.UnsafeShutdowns);
        Assert.AreEqual(17.92, data.TerabytesWritten!.Value, 0.01);

        var items = DiskHealth.Evaluate(data);
        var verdict = Verdict(items);
        Assert.AreEqual(DiagnosticStatus.Ok, verdict.Status);
        StringAssert.StartsWith(verdict.Value!, "Bom");
        Assert.AreSame(verdict, items[0]);
        Assert.IsFalse(items.Any(x => x.Status is DiagnosticStatus.Warning or DiagnosticStatus.Error));
    }

    [TestMethod]
    public void Nvme_ReliabilityDegraded_IsBad()
    {
        var data = new DiskHealthData { IsNvme = true };
        DiskHealth.TryParseNvmeHealthLog(NvmeLog(criticalWarning: 0x04), data);

        var verdict = Verdict(DiskHealth.Evaluate(data));
        Assert.AreEqual(DiagnosticStatus.Error, verdict.Status);
        StringAssert.Contains(verdict.Value!, "confiabilidade degradada");
    }

    [TestMethod]
    public void Nvme_OnlyTemperatureWarning_IsCaution()
    {
        var data = new DiskHealthData { IsNvme = true };
        DiskHealth.TryParseNvmeHealthLog(NvmeLog(criticalWarning: 0x02, celsius: 82, warningTempMinutes: 300), data);

        var items = DiskHealth.Evaluate(data);
        Assert.AreEqual(DiagnosticStatus.Warning, Verdict(items).Status);
        StringAssert.Contains(items.Single(x => x.Label == "Temperatura").Value!, "5 h acima");
    }

    [TestMethod]
    public void Nvme_SpareBelowThreshold_And_WornOut_IsBad()
    {
        var data = new DiskHealthData { IsNvme = true };
        DiskHealth.TryParseNvmeHealthLog(NvmeLog(spare: 5, spareThreshold: 10, percentUsed: 104), data);

        var items = DiskHealth.Evaluate(data);
        Assert.AreEqual(DiagnosticStatus.Error, Verdict(items).Status);
        StringAssert.Contains(items.Single(x => x.Label == "Vida útil").Value!, "0% restante");
    }

    [TestMethod]
    public void Nvme_ShortLog_IsRejected()
    {
        Assert.IsFalse(DiskHealth.TryParseNvmeHealthLog(new byte[100], new DiskHealthData()));
    }

    [TestMethod]
    public void Ata_ParsesRawValuesAndThresholds()
    {
        var (data, thresholds) = AtaTables((5, 100, 10, 8), (9, 95, 0, 0x0000_0000_1234), (194, 70, 0, 0x0028_0014_0021));
        var attributes = DiskHealth.ParseAtaAttributes(data, thresholds);

        Assert.AreEqual(3, attributes.Count);
        Assert.AreEqual(8L, attributes.Single(x => x.Id == 5).Raw);
        Assert.AreEqual(10, attributes.Single(x => x.Id == 5).Threshold);
        Assert.AreEqual(0x1234L, attributes.Single(x => x.Id == 9).Raw);

        var health = new DiskHealthData { IsHdd = true };
        DiskHealth.ApplyAtaAttributes(attributes, health);
        Assert.AreEqual(33, health.TemperatureC);
        Assert.AreEqual(8L, health.ReallocatedSectors);
        Assert.AreEqual(0x1234L, health.PowerOnHours);
    }

    [TestMethod]
    public void Ata_ReallocatedSectors_IsCaution()
    {
        var (data, thresholds) = AtaTables((5, 100, 10, 8), (197, 100, 0, 0), (198, 100, 0, 0));
        var health = new DiskHealthData { IsHdd = true, WindowsHealth = 0 };
        DiskHealth.ApplyAtaAttributes(DiskHealth.ParseAtaAttributes(data, thresholds), health);

        var verdict = Verdict(DiskHealth.Evaluate(health));
        Assert.AreEqual(DiagnosticStatus.Warning, verdict.Status);
        StringAssert.Contains(verdict.Value!, "8 setores realocados");
    }

    [TestMethod]
    public void Ata_PendingAndUncorrectable_IsBad()
    {
        var (data, thresholds) = AtaTables((197, 100, 0, 24), (198, 100, 0, 3));
        var health = new DiskHealthData { IsHdd = true };
        DiskHealth.ApplyAtaAttributes(DiskHealth.ParseAtaAttributes(data, thresholds), health);

        Assert.AreEqual(DiagnosticStatus.Error, Verdict(DiskHealth.Evaluate(health)).Status);
    }

    [TestMethod]
    public void Ata_AttributeAtThreshold_IsBad()
    {
        var (data, thresholds) = AtaTables((5, 36, 36, 1500));
        var health = new DiskHealthData { IsHdd = true };
        DiskHealth.ApplyAtaAttributes(DiskHealth.ParseAtaAttributes(data, thresholds), health);

        Assert.AreEqual(1, health.FailingAttributes.Count);
        Assert.AreEqual(DiagnosticStatus.Error, Verdict(DiskHealth.Evaluate(health)).Status);
    }

    [TestMethod]
    public void Ata_SsdLifeFromNormalizedValue()
    {
        var (data, thresholds) = AtaTables((177, 7, 0, 3000), (241, 100, 0, 40_000_000_000));
        var health = new DiskHealthData();
        DiskHealth.ApplyAtaAttributes(DiskHealth.ParseAtaAttributes(data, thresholds), health);

        Assert.AreEqual(93, health.PercentUsed);
        var items = DiskHealth.Evaluate(health);
        Assert.AreEqual(DiagnosticStatus.Warning, Verdict(items).Status);
        StringAssert.Contains(items.Single(x => x.Label == "Vida útil").Value!, "TB gravados");
    }

    [TestMethod]
    public void Ata_CrcErrorsAlone_AreOnlyInformational()
    {
        var (data, thresholds) = AtaTables((199, 200, 0, 12));
        var health = new DiskHealthData { IsHdd = true, WindowsHealth = 0 };
        DiskHealth.ApplyAtaAttributes(DiskHealth.ParseAtaAttributes(data, thresholds), health);

        Assert.AreEqual(DiagnosticStatus.Ok, Verdict(DiskHealth.Evaluate(health)).Status);
    }

    [TestMethod]
    public void WindowsUnhealthy_IsBad_EvenWithoutSmart()
    {
        var verdict = Verdict(DiskHealth.Evaluate(new DiskHealthData { WindowsHealth = 2 }));
        Assert.AreEqual(DiagnosticStatus.Error, verdict.Status);
    }

    [TestMethod]
    public void NoData_IsInformational()
    {
        var verdict = Verdict(DiskHealth.Evaluate(new DiskHealthData { IsExternal = true }));
        Assert.AreEqual(DiagnosticStatus.Info, verdict.Status);
        StringAssert.Contains(verdict.Hint!, "USB");
    }

    [TestMethod]
    public void Smartctl_Json_Nvme()
    {
        const string json = """
            {
              "device": { "name": "/dev/nvme0", "type": "nvme", "protocol": "NVMe" },
              "model_name": "WD Blue SN570",
              "smart_status": { "passed": true },
              "temperature": { "current": 41 },
              "power_on_time": { "hours": 9000 },
              "nvme_smart_health_information_log": {
                "critical_warning": 0, "available_spare": 100, "available_spare_threshold": 10,
                "percentage_used": 12, "data_units_written": 60000000, "unsafe_shutdowns": 22, "media_errors": 0
              }
            }
            """;
        using var doc = JsonDocument.Parse(json);
        var data = DiskHealth.FromSmartctlJson(doc.RootElement, "/dev/nvme0");

        Assert.IsTrue(data.IsNvme);
        Assert.AreEqual("WD Blue SN570 (NVMe, /dev/nvme0)", data.Group);
        Assert.AreEqual(12, data.PercentUsed);
        Assert.AreEqual(DiagnosticStatus.Ok, Verdict(DiskHealth.Evaluate(data)).Status);
    }

    [TestMethod]
    public void Smartctl_Json_FailedHdd()
    {
        const string json = """
            {
              "device": { "name": "/dev/sda", "type": "sat", "protocol": "ATA" },
              "model_name": "ST1000LM035",
              "rotation_rate": 5400,
              "smart_status": { "passed": false },
              "ata_smart_attributes": { "table": [
                { "id": 5, "value": 90, "worst": 90, "thresh": 36, "raw": { "value": 312 } },
                { "id": 197, "value": 100, "worst": 100, "thresh": 0, "raw": { "value": 16 } }
              ] }
            }
            """;
        using var doc = JsonDocument.Parse(json);
        var data = DiskHealth.FromSmartctlJson(doc.RootElement, "/dev/sda");

        Assert.IsTrue(data.IsHdd);
        Assert.IsTrue(data.PredictFailure);
        Assert.AreEqual(312L, data.ReallocatedSectors);
        Assert.AreEqual(DiagnosticStatus.Error, Verdict(DiskHealth.Evaluate(data)).Status);
    }

    [TestMethod]
    public void Summarize_CountsVerdicts()
    {
        var items = new List<DiagnosticItem>();
        items.AddRange(DiskHealth.Evaluate(new DiskHealthData { Group = "A", WindowsHealth = 0 }));
        items.AddRange(DiskHealth.Evaluate(new DiskHealthData { Group = "B", WindowsHealth = 2 }));

        Assert.AreEqual("2 disco(s): 1 ruim, 1 bom.", DiskHealth.Summarize(items));
    }
}
