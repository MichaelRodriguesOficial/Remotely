using Microsoft.VisualStudio.TestTools.UnitTesting;
using Remotely.Server.Services;

namespace Remotely.Server.Tests;

[TestClass]
public class RemoteSessionRecordTests
{
    [TestMethod]
    [DataRow("Michael Rodrigues", "Michael_Rodrigues")]
    [DataRow("../../etc/passwd", "etc_passwd")]
    [DataRow("C:\\Windows\\x", "C__Windows_x")]
    [DataRow("", "tecnico")]
    [DataRow(null, "tecnico")]
    public void SafeFilePart_KeepsNameInsideFolder(string? value, string expected)
    {
        Assert.AreEqual(expected, SessionRecordingSink.SafeFilePart(value));
    }

    [TestMethod]
    public void ResolveRecordingPath_RejectsPathsOutsideRecordings()
    {
        Assert.IsNull(SessionRecordingSink.ResolveRecordingPath(null));
        Assert.IsNull(SessionRecordingSink.ResolveRecordingPath("../appsettings.json"));
        Assert.IsNull(SessionRecordingSink.ResolveRecordingPath("../../../etc/passwd"));
        Assert.IsNull(SessionRecordingSink.ResolveRecordingPath("/etc/passwd"));

        var ok = SessionRecordingSink.ResolveRecordingPath("2026-11-27/sessao.webm");
        Assert.IsNotNull(ok);
        Assert.IsTrue(ok!.StartsWith(Path.GetFullPath(SessionRecordingSink.RecordingsDirectory)));
    }

    [TestMethod]
    public void GlpiHtml_EscapesAndKeepsLineBreaks()
    {
        var html = GlpiClient.ToHtml("Troquei <toner> & testei\nImprimiu\n\nOk");
        Assert.AreEqual("<p>Troquei &lt;toner&gt; &amp; testei<br>Imprimiu</p><p>Ok</p>", html);
    }
}
