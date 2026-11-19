using Remotely.Shared.Helpers;

namespace Remotely.Shared.Tests;

[TestClass]
public class RandomGeneratorTests
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    [TestMethod]
    public void GenerateString_ReturnsRequestedLength()
    {
        foreach (var length in new[] { 1, 16, 36, 64, 200 })
        {
            Assert.AreEqual(length, RandomGenerator.GenerateString(length).Length);
        }
    }

    [TestMethod]
    public void GenerateString_UsesOnlyAlphanumericCharacters()
    {
        var value = RandomGenerator.GenerateString(10_000);
        Assert.IsTrue(value.All(Alphabet.Contains));
    }

    [TestMethod]
    public void GenerateString_CoversWholeAlphabet()
    {
        // The previous alphabet had a typo that repeated "G" and never produced "J".
        var value = RandomGenerator.GenerateString(20_000);
        foreach (var c in Alphabet)
        {
            Assert.IsTrue(value.Contains(c), $"Character '{c}' was never generated.");
        }
    }

    [TestMethod]
    public void GenerateAccessKey_IsUnique()
    {
        var keys = Enumerable.Range(0, 1_000)
            .Select(_ => RandomGenerator.GenerateAccessKey())
            .ToHashSet();

        Assert.AreEqual(1_000, keys.Count);
        Assert.IsTrue(keys.All(x => x.Length == 64));
    }
}
