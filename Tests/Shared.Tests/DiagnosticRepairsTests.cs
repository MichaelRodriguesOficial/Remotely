using Remotely.Shared.Models.Diagnostics;

namespace Remotely.Shared.Tests;

[TestClass]
public class DiagnosticRepairsTests
{
    [TestMethod]
    [DataRow("C:", "C:")]
    [DataRow("d:", "D:")]
    [DataRow(" E: ", "E:")]
    [DataRow("C", null)]
    [DataRow("C:\\", null)]
    [DataRow("C: & del *", null)]
    [DataRow("C:\nD:", null)]
    [DataRow("", null)]
    public void NormalizeTarget_Drive(string target, string? expected)
    {
        var repair = DiagnosticRepairs.Get(DiagnosticRepairs.DiskScan)!;
        Assert.AreEqual(expected, DiagnosticRepairs.NormalizeTarget(repair, target));
    }

    [TestMethod]
    [DataRow(@"PCI\VEN_8086&DEV_A0F0&SUBSYS_00748086&REV_20\3&11583659&0&A3", true)]
    [DataRow(@"USB\VID_27C6&PID_538D\6&2C9A1D52&0&3", true)]
    [DataRow(@"ACPI\PNP0C0A\{1}", true)]
    [DataRow("dispositivo\" & shutdown /s", false)]
    [DataRow("a|b", false)]
    [DataRow("ab", false)]
    public void NormalizeTarget_Device(string target, bool valid)
    {
        var repair = DiagnosticRepairs.Get(DiagnosticRepairs.DeviceRestart)!;
        Assert.AreEqual(valid, DiagnosticRepairs.NormalizeTarget(repair, target) is not null);
    }

    [TestMethod]
    [DataRow("5", "5")]
    [DataRow("60", "60")]
    [DataRow("22:00", "22:00")]
    [DataRow("0", null)]
    [DataRow("1441", null)]
    [DataRow("-5", null)]
    [DataRow("25:00", null)]
    [DataRow("agora", null)]
    [DataRow(null, null)]
    public void NormalizeTarget_RebootTime(string? target, string? expected)
    {
        var repair = DiagnosticRepairs.Get(DiagnosticRepairs.Reboot)!;
        Assert.AreEqual(expected, DiagnosticRepairs.NormalizeTarget(repair, target));
    }

    [TestMethod]
    public void NormalizeTarget_NoTargetRepairIgnoresInput()
    {
        var repair = DiagnosticRepairs.Get(DiagnosticRepairs.Cleanup)!;
        Assert.AreEqual(string.Empty, DiagnosticRepairs.NormalizeTarget(repair, "qualquer coisa"));
    }

    [TestMethod]
    public void RebootDelaySeconds_MinutesAndTime()
    {
        var now = new DateTime(2026, 11, 23, 19, 10, 0);
        Assert.AreEqual(300, DiagnosticRepairs.RebootDelaySeconds("5", now));
        Assert.AreEqual((int)TimeSpan.FromMinutes(170).TotalSeconds, DiagnosticRepairs.RebootDelaySeconds("22:00", now));

        // Horário que já passou hoje: amanhã.
        Assert.AreEqual((int)TimeSpan.FromMinutes(22 * 60 + 50).TotalSeconds, DiagnosticRepairs.RebootDelaySeconds("18:00", now));
    }

    [TestMethod]
    public void TestIds_RoundTrip()
    {
        var testId = DiagnosticRepairs.ToTestId(DiagnosticRepairs.Cleanup);
        Assert.IsTrue(DiagnosticRepairs.IsRepairRun(testId));
        Assert.AreEqual(DiagnosticRepairs.Cleanup, DiagnosticRepairs.FromTestId(testId)?.Id);
        Assert.IsNull(DiagnosticRepairs.FromTestId(DiagnosticTests.Disks));
        Assert.IsFalse(DiagnosticRepairs.IsRepairRun("disks,network"));
    }

    [TestMethod]
    public void IsAvailable_ByPlatform()
    {
        Assert.IsTrue(DiagnosticRepairs.IsAvailable(DiagnosticRepairs.Get(DiagnosticRepairs.SystemFiles)!, "Windows"));
        Assert.IsFalse(DiagnosticRepairs.IsAvailable(DiagnosticRepairs.Get(DiagnosticRepairs.SystemFiles)!, "Linux"));
        Assert.IsTrue(DiagnosticRepairs.IsAvailable(DiagnosticRepairs.Get(DiagnosticRepairs.Reboot)!, "Linux"));
        Assert.IsFalse(DiagnosticRepairs.IsAvailable(DiagnosticRepairs.Get(DiagnosticRepairs.Reboot)!, "MacOS"));
    }

    [TestMethod]
    public void Catalog_IdsAreUniqueAndRetestsExist()
    {
        Assert.AreEqual(DiagnosticRepairs.All.Count, DiagnosticRepairs.All.Select(x => x.Id).Distinct().Count());
        foreach (var repair in DiagnosticRepairs.All)
        {
            foreach (var test in repair.RetestTests)
            {
                Assert.IsNotNull(DiagnosticTests.Get(test), $"{repair.Id} -> {test}");
            }
        }
    }
}
