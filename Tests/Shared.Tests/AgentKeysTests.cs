using Remotely.Shared.Helpers;

namespace Remotely.Shared.Tests;

[TestClass]
public class AgentKeysTests
{
    [TestMethod]
    public void Generate_IsLongRandomAndUrlSafe()
    {
        var a = AgentKeys.Generate();
        var b = AgentKeys.Generate();
        Assert.AreNotEqual(a, b);
        Assert.IsTrue(a.Length >= 43);
        Assert.IsTrue(a.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'));
    }

    [TestMethod]
    public void Matches_OnlyTheSameKey()
    {
        var key = AgentKeys.Generate();
        var hash = AgentKeys.Hash(key);
        Assert.IsNotNull(hash);
        Assert.AreEqual(64, hash!.Length);
        Assert.IsTrue(AgentKeys.Matches(hash, key));
        Assert.IsFalse(AgentKeys.Matches(hash, AgentKeys.Generate()));
        Assert.IsFalse(AgentKeys.Matches(hash, null));
        Assert.IsFalse(AgentKeys.Matches(null, key));
        Assert.IsFalse(AgentKeys.Matches("", key));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("curta")]
    public void Hash_RejectsEmptyOrShortKeys(string? key)
    {
        Assert.IsNull(AgentKeys.Hash(key));
    }
}
