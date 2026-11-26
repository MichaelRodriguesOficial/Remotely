using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Remotely.Server.Services;

namespace Remotely.Server.Tests;

[TestClass]
public class DiagnosticRunRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 10, 0, 0, TimeSpan.FromHours(-3));

    [TestMethod]
    public void Finished_IsNotActive()
    {
        Assert.IsFalse(DiagnosticRunRules.IsActive("disks", true, Now.AddMinutes(-1), Now, false, Now));
    }

    [TestMethod]
    public void Superseded_IsNotActive()
    {
        // Execução antiga presa no teste de memória não pode segurar a bolha "Executando".
        Assert.IsFalse(DiagnosticRunRules.IsActive("memory-test", true, Now.AddHours(-1), null, true, Now));
    }

    [TestMethod]
    [DataRow("disks,battery", false, 4, true)]
    [DataRow("disks,battery", false, 6, false)]
    [DataRow("disks,battery", true, 19, true)]
    [DataRow("disks,battery", true, 21, false)]
    [DataRow("repair:cleanup", false, 2, true)]
    [DataRow("repair:cleanup", false, 4, false)]
    [DataRow("repair:cleanup", true, 90, true)]
    [DataRow("memory-test,system", true, 600, true)]
    [DataRow("memory-test,system", true, 900, false)]
    public void Timeouts(string tests, bool hasData, int minutesAgo, bool expected)
    {
        Assert.AreEqual(expected, DiagnosticRunRules.IsActive(tests, hasData, Now.AddMinutes(-minutesAgo), null, false, Now));
    }
}
