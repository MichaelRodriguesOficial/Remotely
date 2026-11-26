using Remotely.Shared.Models;

namespace Remotely.Shared.Tests;

[TestClass]
public class DeviceEventQueryTests
{
    [TestMethod]
    public void Normalize_ClampsValuesFromTheBrowser()
    {
        var query = new DeviceEventQuery
        {
            LogName = "security",
            Levels = [9, 0, 2, 2, 1],
            Hours = 99999,
            MaxEvents = 1_000_000,
            Search = "   "
        }.Normalize();

        Assert.AreEqual("Security", query.LogName);
        CollectionAssert.AreEqual(new List<int> { 1, 2 }, query.Levels);
        Assert.AreEqual(720, query.Hours);
        Assert.AreEqual(500, query.MaxEvents);
        Assert.IsNull(query.Search);
    }

    [TestMethod]
    public void Normalize_UnknownLogAndNoLevels_UsesDefaults()
    {
        var query = new DeviceEventQuery { LogName = "../../etc", Levels = [], Hours = 0, MaxEvents = 0 }.Normalize();

        Assert.AreEqual("System", query.LogName);
        CollectionAssert.AreEqual(new List<int> { 1, 2, 3 }, query.Levels);
        Assert.AreEqual(1, query.Hours);
        Assert.AreEqual(10, query.MaxEvents);
    }

    [TestMethod]
    public void Matches_SearchesMessageSourceAndId()
    {
        var entry = new DeviceEventEntry { Source = "Kernel-Power", EventId = 41, Message = "O sistema foi reinicializado" };

        Assert.IsTrue(new DeviceEventQuery { Search = "41" }.Matches(entry));
        Assert.IsTrue(new DeviceEventQuery { Search = "kernel" }.Matches(entry));
        Assert.IsTrue(new DeviceEventQuery { Search = "REINICIALIZADO" }.Matches(entry));
        Assert.IsFalse(new DeviceEventQuery { Search = "4" }.Matches(entry));
        Assert.IsTrue(new DeviceEventQuery { Search = null }.Matches(entry));
    }

    [TestMethod]
    public void Trim_LimitsLongMessages()
    {
        var text = new string('a', DeviceEventEntry.MaxMessageLength + 50);
        var trimmed = DeviceEventEntry.Trim(text);

        Assert.AreEqual(DeviceEventEntry.MaxMessageLength + 1, trimmed.Length);
        Assert.IsTrue(trimmed.EndsWith('…'));
    }
}
