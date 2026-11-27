using Remotely.Shared.Models.Dtos;

namespace Remotely.Shared.Tests;

[TestClass]
public class ChatLimitsTests
{
    [TestMethod]
    [DataRow("foto.PNG", "image/png")]
    [DataRow("relatorio.pdf", "application/pdf")]
    [DataRow("planilha.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [DataRow("script.ps1", "application/octet-stream")]
    [DataRow("pagina.html", "application/octet-stream")]
    public void ContentTypeFor_KnownTypesOnly(string fileName, string expected)
    {
        Assert.AreEqual(expected, ChatLimits.ContentTypeFor(fileName));
    }

    [TestMethod]
    public void IsImage_NeverSvg()
    {
        Assert.IsTrue(ChatLimits.IsImage("image/png", "a.png"));
        Assert.IsTrue(ChatLimits.IsImage(null, "a.jpg"));
        Assert.IsFalse(ChatLimits.IsImage("image/svg+xml", "a.svg"));
        Assert.IsFalse(ChatLimits.IsImage("text/html", "a.html"));
    }

    [TestMethod]
    public void FormatSize_Readable()
    {
        Assert.AreEqual("500 B", ChatLimits.FormatSize(500));
        Assert.AreEqual("2 KB", ChatLimits.FormatSize(2048));
        Assert.IsTrue(ChatLimits.FormatSize(3 * 1024 * 1024).EndsWith("MB"));
    }
}
