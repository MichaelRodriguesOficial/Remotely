using Remotely.Shared.Models.Diagnostics;

namespace Remotely.Shared.Tests;

[TestClass]
public class BatteryHealthTests
{
    private const string PowercfgXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <BatteryReport xmlns="http://schemas.microsoft.com/battery/2012">
          <Batteries>
            <Battery>
              <Id>DELL 02HHX54</Id>
              <Manufacturer>SMP</Manufacturer>
              <SerialNumber>1234</SerialNumber>
              <ManufactureDate>2023-03-14</ManufactureDate>
              <Chemistry>LiP</Chemistry>
              <LongTerm>1</LongTerm>
              <RelativeCapacity>0</RelativeCapacity>
              <DesignCapacity>42009</DesignCapacity>
              <FullChargeCapacity>33516</FullChargeCapacity>
              <CycleCount>312</CycleCount>
            </Battery>
          </Batteries>
          <RuntimeEstimates>
            <FullChargeCapacity><ActiveRuntime>PT3H12M</ActiveRuntime></FullChargeCapacity>
            <DesignCapacity><ActiveRuntime>PT4H1M</ActiveRuntime></DesignCapacity>
          </RuntimeEstimates>
          <History>
            <HistoryEntry LocalStartDate="2026-05-01T00:00:00" FullChargeCapacity="40329" DesignCapacity="42009" />
            <HistoryEntry LocalStartDate="2026-08-01T00:00:00" FullChargeCapacity="37000" DesignCapacity="42009" />
            <HistoryEntry LocalStartDate="2026-11-01T00:00:00" FullChargeCapacity="33516" DesignCapacity="42009" />
          </History>
        </BatteryReport>
        """;

    private static DiagnosticItem Verdict(List<DiagnosticItem> items) =>
        items.Single(x => x.Label == BatteryHealth.VerdictLabel);

    [TestMethod]
    public void ParsePowercfgXml_ReadsBatteryRuntimeAndHistory()
    {
        var batteries = BatteryHealth.ParsePowercfgXml(PowercfgXml);

        Assert.AreEqual(1, batteries.Count);
        var battery = batteries[0];
        Assert.AreEqual("DELL 02HHX54", battery.Group);
        Assert.AreEqual("Polímero de lítio", battery.Chemistry);
        Assert.AreEqual(42009L, battery.DesignCapacity);
        Assert.AreEqual(33516L, battery.FullChargeCapacity);
        Assert.AreEqual(312L, battery.CycleCount);
        Assert.AreEqual(TimeSpan.FromMinutes(192), battery.RuntimeAtFullCharge);
        Assert.AreEqual(3, battery.History.Count);
        Assert.AreEqual(79.8, battery.HealthPercent!.Value, 0.1);
    }

    [TestMethod]
    public void Evaluate_ReportsCautionAndFastWear()
    {
        var battery = BatteryHealth.ParsePowercfgXml(PowercfgXml)[0];
        var items = BatteryHealth.Evaluate(battery);

        var verdict = Verdict(items);
        Assert.AreEqual(DiagnosticStatus.Warning, verdict.Status);
        StringAssert.Contains(verdict.Value!, "80% da capacidade original");
        StringAssert.Contains(verdict.Value!, "desgaste acelerado");
        StringAssert.Contains(items.Single(x => x.Label == "Autonomia estimada").Value!, "3 h 12 min");
        Assert.AreSame(verdict, items[0]);
    }

    [TestMethod]
    [DataRow(40000L, 42000L, DiagnosticStatus.Ok)]
    [DataRow(30000L, 42000L, DiagnosticStatus.Warning)]
    [DataRow(20000L, 42000L, DiagnosticStatus.Error)]
    [DataRow(45000L, 42000L, DiagnosticStatus.Ok)]
    public void Evaluate_CapacityThresholds(long full, long design, DiagnosticStatus expected)
    {
        var items = BatteryHealth.Evaluate(new BatteryHealthData { FullChargeCapacity = full, DesignCapacity = design });
        Assert.AreEqual(expected, Verdict(items).Status);
    }

    [TestMethod]
    public void Evaluate_NewBatteryAboveDesign_ShowsHundred()
    {
        var items = BatteryHealth.Evaluate(new BatteryHealthData { FullChargeCapacity = 45000, DesignCapacity = 42000 });
        StringAssert.StartsWith(items.Single(x => x.Label == "Capacidade").Value!, "100%");
    }

    [TestMethod]
    public void Evaluate_WithoutCapacity_IsInformational()
    {
        var items = BatteryHealth.Evaluate(new BatteryHealthData { ChargePercent = 50, PowerState = "na bateria" });
        Assert.AreEqual(DiagnosticStatus.Info, Verdict(items).Status);
    }

    [TestMethod]
    public void ParsePowercfgXml_InvalidXml_ReturnsEmpty()
    {
        Assert.AreEqual(0, BatteryHealth.ParsePowercfgXml("<not xml").Count);
    }

    [TestMethod]
    public void Summarize_SingleBattery()
    {
        var items = BatteryHealth.Evaluate(new BatteryHealthData { FullChargeCapacity = 40000, DesignCapacity = 42000 });
        Assert.AreEqual("Bom.", BatteryHealth.Summarize(items));
    }
}
