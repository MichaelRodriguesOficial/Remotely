using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Remotely.Server.Models;
using Remotely.Server.Services;
using Remotely.Shared.Entities;
using Remotely.Shared.Models.Diagnostics;
using Remotely.Shared.Models.Inventory;

namespace Remotely.Server.Tests;

[TestClass]
public class ReportBuilderTests
{
    [TestMethod]
    public void Csv_PrefixesFormulasAndQuotesSeparators()
    {
        var bytes = ReportBuilder.Csv(
            ["A", "B", "C"],
            [new List<string?> { "=HYPERLINK(\"http://x\")", "a;b", "@SUM(1)" }]);

        var text = Encoding.UTF8.GetString(bytes).TrimStart('﻿');
        var lines = text.Split(Environment.NewLine);

        Assert.AreEqual("A;B;C", lines[0]);
        Assert.AreEqual("\"'=HYPERLINK(\"\"http://x\"\")\";\"a;b\";'@SUM(1)", lines[1]);
    }

    [TestMethod]
    public void Csv_StartsWithUtf8Bom()
    {
        var bytes = ReportBuilder.Csv(["Nome"], []);
        CollectionAssert.AreEqual(Encoding.UTF8.GetPreamble(), bytes[..3]);
    }

    [TestMethod]
    public void DiagnosticsHtml_EncodesAgentData()
    {
        var device = new Device { ID = "d1", DeviceName = "<script>alert(1)</script>" };
        var run = new DeviceDiagnosticRun { DeviceID = "d1", StartedAt = DateTimeOffset.Now, RequestedBy = "tecnico" };
        var report = new DiagnosticReport
        {
            Tests =
            [
                new DiagnosticTestResult
                {
                    Id = DiagnosticTests.Disks,
                    Name = "Saúde dos discos",
                    Status = DiagnosticStatus.Warning,
                    Items = [new DiagnosticItem { Label = "<img src=x onerror=alert(1)>", Value = "\"><b>x</b>" }]
                }
            ]
        };

        var html = ReportBuilder.DiagnosticsHtml(device, run, report, UserNameDisplayFormat.FirstLast);

        Assert.IsFalse(html.Contains("<script>alert(1)</script>"));
        Assert.IsFalse(html.Contains("<img src=x"));
        Assert.IsFalse(html.Contains("<b>x</b>"));
        Assert.IsTrue(html.Contains("&lt;script&gt;"));
    }

    [TestMethod]
    public void InventoryCsv_MasksProductKeyUnlessAllowed()
    {
        var device = new Device { ID = "d1", DeviceName = "PC-01" };
        var inventory = new DeviceInventory
        {
            OperatingSystem = new InventoryOperatingSystem { Name = "Windows 11", SerialNumber = "T4KWN-8RC2Q-VM7XD-PJ6HY-3G9FB" }
        };

        var masked = Encoding.UTF8.GetString(ReportBuilder.InventoryCsv(device, inventory, showSensitive: false));
        var full = Encoding.UTF8.GetString(ReportBuilder.InventoryCsv(device, inventory, showSensitive: true));

        Assert.IsFalse(masked.Contains("T4KWN-8RC2Q"));
        Assert.IsTrue(masked.Contains("3G9FB"));
        Assert.IsTrue(full.Contains("T4KWN-8RC2Q-VM7XD-PJ6HY-3G9FB"));
    }
}
