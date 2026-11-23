using Remotely.Shared.Models;
using Remotely.Shared.Services;

namespace Remotely.Shared.Tests;

[TestClass]
public class EmbeddedServerDataProviderTests
{
    private readonly EmbeddedServerDataProvider _provider = EmbeddedServerDataProvider.Instance;

    [TestMethod]
    [DataRow("https://suporte.empresa.com.br", "Remotely_Desktop_x64 (suporte.empresa.com.br).exe")]
    [DataRow("https://suporte.empresa.com.br:8443", "Remotely_Desktop_x64 (suporte.empresa.com.br_8443).exe")]
    [DataRow("http://10.0.0.5:5000", "Remotely_Desktop_x64 (http~10.0.0.5_5000).exe")]
    public void FriendlyFileName_RoundTrips(string serverUrl, string expectedName)
    {
        var data = new EmbeddedServerData(new Uri(serverUrl), null);

        var fileName = _provider.GetFriendlyFileName("Remotely_Desktop_x64", ".exe", data);
        Assert.AreEqual(expectedName, fileName);

        var result = _provider.TryGetEmbeddedData(Path.Combine("C:", "Downloads", fileName));
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(new Uri(serverUrl), result.Value.ServerUrl);
        Assert.AreEqual(string.Empty, result.Value.OrganizationId);
    }

    [TestMethod]
    public void FriendlyFileName_ToleratesBrowserDuplicateSuffix()
    {
        var result = _provider.TryGetEmbeddedData("Remotely_Desktop_x64 (suporte.empresa.com.br) (1).exe");

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(new Uri("https://suporte.empresa.com.br"), result.Value.ServerUrl);
    }

    [TestMethod]
    public void FriendlyFileName_WithOrganization_FallsBackToEncodedName()
    {
        var orgId = Guid.NewGuid().ToString();
        var data = new EmbeddedServerData(new Uri("https://suporte.empresa.com.br"), orgId);

        var fileName = _provider.GetFriendlyFileName("Remotely_Desktop_x64", ".exe", data);
        StringAssert.StartsWith(fileName, "Remotely_Desktop_x64[");

        var result = _provider.TryGetEmbeddedData(fileName);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(orgId, result.Value.OrganizationId);
    }

    [TestMethod]
    public void LegacyEncodedName_StillParses()
    {
        var data = new EmbeddedServerData(new Uri("https://suporte.empresa.com.br"), null);
        var fileName = _provider.GetEncodedFileName("Remotely_Desktop.exe", data);

        var result = _provider.TryGetEmbeddedData(fileName);
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(new Uri("https://suporte.empresa.com.br"), result.Value.ServerUrl);
    }
}
