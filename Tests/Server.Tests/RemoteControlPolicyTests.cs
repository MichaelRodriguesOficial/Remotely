using Microsoft.VisualStudio.TestTools.UnitTesting;
using Remotely.Server.Hubs;
using Remotely.Server.Models;
using Remotely.Server.Services;
using Remotely.Shared.Entities;
using Remotely.Shared.Models.Dtos;

namespace Remotely.Server.Tests;

[TestClass]
public class RemoteControlPolicyTests
{
    [TestMethod]
    public void Resolve_UsesServerDefault_AutoAccept()
    {
        var settings = new SettingsModel();
        var (require, seconds, mode) = RemoteControlPolicy.Resolve(settings, null);
        Assert.IsTrue(require);
        Assert.AreEqual(30, seconds);
        Assert.AreEqual(RemoteControlPolicy.AutoAccept, mode);
    }

    [TestMethod]
    public void Resolve_LegacyEnforceAttendedAccess_IsAsk()
    {
        var settings = new SettingsModel { EnforceAttendedAccess = true };
        var (require, seconds, _) = RemoteControlPolicy.Resolve(settings, null);
        Assert.IsTrue(require);
        Assert.AreEqual(0, seconds);
    }

    [TestMethod]
    public void Resolve_GroupOverridesServer()
    {
        var settings = new SettingsModel { RemoteControlConsent = RemoteControlPolicy.Ask };
        var group = new DeviceGroup { Name = "Servidores", RemoteControlConsent = RemoteControlPolicy.Never };
        var (require, seconds, mode) = RemoteControlPolicy.Resolve(settings, group);
        Assert.IsFalse(require);
        Assert.AreEqual(0, seconds);
        Assert.AreEqual(RemoteControlPolicy.Never, mode);
    }

    [TestMethod]
    public void Resolve_ClampsAutoAcceptSeconds()
    {
        var settings = new SettingsModel { RemoteControlConsentSeconds = 1 };
        Assert.AreEqual(5, RemoteControlPolicy.Resolve(settings, null).AutoAcceptSeconds);
    }

    [TestMethod]
    public void Normalize_UnknownValue_IsAutoAccept()
    {
        Assert.AreEqual(RemoteControlPolicy.AutoAccept, RemoteControlPolicy.Normalize("qualquer"));
    }

    [TestMethod]
    [DataRow(DtoType.MouseMove)]
    [DataRow(DtoType.KeyDown)]
    [DataRow(DtoType.CtrlAltDel)]
    [DataRow(DtoType.ClipboardText)]
    public void ViewOnly_BlocksInput(DtoType type)
    {
        Assert.IsFalse(ViewerHub.IsDtoAllowed(type, RemoteControlPermissions.Full, viewOnly: true));
    }

    [TestMethod]
    public void Permissions_BlockEachKind()
    {
        var noFiles = RemoteControlPermissions.Full with { Files = false };
        Assert.IsFalse(ViewerHub.IsDtoAllowed(DtoType.File, noFiles, false));
        Assert.IsFalse(ViewerHub.IsDtoAllowed(DtoType.FolderListRequest, noFiles, false));
        Assert.IsTrue(ViewerHub.IsDtoAllowed(DtoType.MouseMove, noFiles, false));

        var noClipboard = RemoteControlPermissions.Full with { Clipboard = false };
        Assert.IsFalse(ViewerHub.IsDtoAllowed(DtoType.TextTransfer, noClipboard, false));

        var noSystem = RemoteControlPermissions.Full with { SystemActions = false };
        Assert.IsFalse(ViewerHub.IsDtoAllowed(DtoType.ToggleBlockInput, noSystem, false));
        Assert.IsTrue(ViewerHub.IsDtoAllowed(DtoType.KeyDown, noSystem, false));
    }

    [TestMethod]
    public void AlwaysAllowed_ScreenAndMetrics()
    {
        Assert.IsTrue(ViewerHub.IsDtoAllowed(DtoType.FrameReceived, RemoteControlPermissions.None, true));
        Assert.IsTrue(ViewerHub.IsDtoAllowed(DtoType.SelectScreen, RemoteControlPermissions.None, true));
    }
}
