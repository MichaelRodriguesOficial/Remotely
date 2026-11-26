using Remotely.Shared.Models;

namespace Remotely.Shared.Tests;

[TestClass]
public class FileTransferDestinationsTests
{
    [TestMethod]
    [DataRow("downloads", "downloads")]
    [DataRow("desktop", "desktop")]
    [DataRow("public-desktop", "public-desktop")]
    [DataRow("temp", "temp")]
    [DataRow("", "downloads")]
    public void TryParse_KnownDestinations(string destination, string expected)
    {
        Assert.IsTrue(FileTransferDestinations.TryParse(destination, isWindows: true, out var kind, out var path, out _));
        Assert.AreEqual(expected, kind);
        Assert.IsNull(path);
    }

    [TestMethod]
    public void TryParse_PublicDesktop_OnlyOnWindows()
    {
        Assert.IsFalse(FileTransferDestinations.TryParse("public-desktop", isWindows: false, out _, out _, out var error));
        Assert.IsFalse(string.IsNullOrWhiteSpace(error));
    }

    [TestMethod]
    public void TryParse_UnknownDestination_Fails()
    {
        Assert.IsFalse(FileTransferDestinations.TryParse("system32", isWindows: true, out _, out _, out _));
    }

    [TestMethod]
    [DataRow(@"C:\Suporte", true)]
    [DataRow(@"d:/Instaladores/VPN", true)]
    [DataRow(@"C:\Users\Public\Desktop", true)]
    [DataRow(@"Suporte", false)]
    [DataRow(@"C:Suporte", false)]
    [DataRow(@"\\servidor\share", false)]
    [DataRow(@"C:\..\Windows", false)]
    [DataRow(@"C:\Suporte\.\x", false)]
    [DataRow(@"C:\Sup*rte", false)]
    [DataRow("C:\\Suporte\nD:\\", false)]
    [DataRow(@"C:\a:b", false)]
    [DataRow("", false)]
    public void IsValidCustomPath_Windows(string path, bool expected)
    {
        Assert.AreEqual(expected, FileTransferDestinations.IsValidCustomPath(path, isWindows: true, out _));
    }

    [TestMethod]
    [DataRow("/opt/suporte", true)]
    [DataRow("/home/joao/Downloads", true)]
    [DataRow("opt/suporte", false)]
    [DataRow("/opt/../etc", false)]
    [DataRow(@"C:\Suporte", false)]
    public void IsValidCustomPath_Linux(string path, bool expected)
    {
        Assert.AreEqual(expected, FileTransferDestinations.IsValidCustomPath(path, isWindows: false, out _));
    }

    [TestMethod]
    public void TryParse_CustomPath_ReturnsTrimmedPath()
    {
        Assert.IsTrue(FileTransferDestinations.TryParse(@"path:  C:\Suporte ", isWindows: true, out var kind, out var path, out _));
        Assert.AreEqual(FileTransferDestinations.PathPrefix, kind);
        Assert.AreEqual(@"C:\Suporte", path);
    }

    [TestMethod]
    public void TryParse_CustomPath_TooLong_Fails()
    {
        var path = @"C:\" + new string('a', FileTransferDestinations.MaxPathLength);
        Assert.IsFalse(FileTransferDestinations.TryParse(FileTransferDestinations.ForPath(path), isWindows: true, out _, out _, out _));
    }

    [TestMethod]
    public void Describe_ShowsFriendlyNames()
    {
        Assert.AreEqual("Downloads do usuário", FileTransferDestinations.Describe("downloads"));
        Assert.AreEqual(@"C:\Suporte", FileTransferDestinations.Describe(@"path:C:\Suporte"));
    }
}
