using Remotely.Shared.Models.Diagnostics;

namespace Remotely.Shared.Tests;

[TestClass]
public class DiagnosticSerializerTests
{
    [TestMethod]
    public void Compress_RoundTrips()
    {
        var report = new DiagnosticReport
        {
            RunId = "run-1",
            Completed = true,
            Tests =
            [
                new DiagnosticTestResult
                {
                    Id = DiagnosticTests.Disks,
                    Name = "Saúde dos discos",
                    Status = DiagnosticStatus.Warning,
                    Items = [new DiagnosticItem { Label = "Espaço livre em C:", Value = "8%", Status = DiagnosticStatus.Warning, Group = "Volumes" }]
                },
                new DiagnosticTestResult { Id = DiagnosticTests.Battery, Name = "Bateria", Status = DiagnosticStatus.Ok }
            ]
        };

        Assert.IsTrue(DiagnosticSerializer.TryDecompress(DiagnosticSerializer.Compress(report), out var result));
        Assert.AreEqual("run-1", result!.RunId);
        Assert.AreEqual(DiagnosticStatus.Warning, result.OverallStatus);
        Assert.AreEqual("Volumes", result.Tests[0].Items[0].Group);
    }

    [TestMethod]
    public void TryDecompress_RejectsReportWithoutRunId()
    {
        var data = DiagnosticSerializer.Compress(new DiagnosticReport());
        Assert.IsFalse(DiagnosticSerializer.TryDecompress(data, out _));
        Assert.IsFalse(DiagnosticSerializer.TryDecompress([0, 1, 2], out _));
    }

    [TestMethod]
    public void OverallStatus_ErrorWins()
    {
        var report = new DiagnosticReport
        {
            Tests =
            [
                new DiagnosticTestResult { Status = DiagnosticStatus.Ok },
                new DiagnosticTestResult { Status = DiagnosticStatus.Error },
                new DiagnosticTestResult { Status = DiagnosticStatus.Warning }
            ]
        };
        Assert.AreEqual(DiagnosticStatus.Error, report.OverallStatus);
    }
}
