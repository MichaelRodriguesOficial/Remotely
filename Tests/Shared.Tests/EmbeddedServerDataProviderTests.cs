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

    [TestMethod]
    [DataRow("https://suporte.empresa.com.br", "")]
    [DataRow("http://10.0.0.5:5000", "0ca90fd7-1943-4970-a3d1-04f33d56de5f")]
    public void Trailer_AppendedToExecutable_RoundTrips(string serverUrl, string orgId)
    {
        var dir = Directory.CreateTempSubdirectory("remotely-trailer");
        try
        {
            // Nome simples, sem nenhum dado do servidor.
            var filePath = Path.Combine(dir.FullName, "Remotely_Desktop_x64.exe");
            var fakeExe = new byte[4096];
            Random.Shared.NextBytes(fakeExe);

            var data = new EmbeddedServerData(new Uri(serverUrl), orgId);
            File.WriteAllBytes(filePath, [.. fakeExe, .. _provider.CreateTrailer(data)]);

            var result = _provider.TryGetEmbeddedData(filePath);
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(new Uri(serverUrl), result.Value.ServerUrl);
            Assert.AreEqual(orgId, result.Value.OrganizationId);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void Trailer_Missing_FallsBackToFileName()
    {
        var dir = Directory.CreateTempSubdirectory("remotely-trailer");
        try
        {
            var filePath = Path.Combine(dir.FullName, "Remotely_Desktop_x64 (suporte.empresa.com.br).exe");
            File.WriteAllBytes(filePath, new byte[2048]);

            var result = _provider.TryGetEmbeddedData(filePath);
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(new Uri("https://suporte.empresa.com.br"), result.Value.ServerUrl);

            var plain = Path.Combine(dir.FullName, "Remotely_Desktop_x64.exe");
            File.WriteAllBytes(plain, new byte[2048]);
            Assert.IsFalse(_provider.TryGetEmbeddedData(plain).IsSuccess);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
