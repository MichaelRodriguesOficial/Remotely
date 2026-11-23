using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.AspNetCore.Http;
using Remotely.Server.Components.Devices;
using Remotely.Server.Extensions;
using Remotely.Server.Models;
using Remotely.Shared.Entities;

namespace Remotely.Server.Tests;

[TestClass]
public class DeviceDisplayTests
{
    [TestMethod]
    [DataRow("ums112 (Silva, Michael)", UserNameDisplayFormat.FirstLast, "Michael Silva")]
    [DataRow("ums112 (Silva, Michael)", UserNameDisplayFormat.LastCommaFirst, "Silva, Michael")]
    [DataRow("ums112 (Silva, Michael)", UserNameDisplayFormat.Original, "Silva, Michael")]
    [DataRow("michael (Michael Rodrigues da Silva)", UserNameDisplayFormat.FirstLast, "Michael Rodrigues da Silva")]
    [DataRow("michael (Michael Rodrigues da Silva)", UserNameDisplayFormat.LastCommaFirst, "Rodrigues da Silva, Michael")]
    [DataRow("admin (Administrador)", UserNameDisplayFormat.LastCommaFirst, "Administrador")]
    public void GetUser_FormatsDisplayName(string currentUser, UserNameDisplayFormat format, string expected)
    {
        var device = new Device { CurrentUser = currentUser };

        var (userName, fullName) = DeviceDisplay.GetUser(device, format);

        Assert.AreEqual(currentUser.Split(' ')[0], userName);
        Assert.AreEqual(expected, fullName);
    }

    [TestMethod]
    public void GetUser_PrefersSeparateFieldsFromTheAgent()
    {
        var device = new Device
        {
            CurrentUser = "ums112 (Silva, Michael)",
            CurrentUserGivenName = "Michael",
            CurrentUserSurname = "Rodrigues Silva"
        };

        Assert.AreEqual("Michael Rodrigues Silva", DeviceDisplay.GetUser(device, UserNameDisplayFormat.FirstLast).FullName);
        Assert.AreEqual("Rodrigues Silva, Michael", DeviceDisplay.GetUser(device, UserNameDisplayFormat.LastCommaFirst).FullName);
    }

    [TestMethod]
    public void GetUser_WithoutDisplayName_ReturnsOnlyUser()
    {
        var device = new Device { CurrentUser = "carla.m" };
        var (userName, fullName) = DeviceDisplay.GetUser(device, UserNameDisplayFormat.FirstLast);
        Assert.AreEqual("carla.m", userName);
        Assert.IsNull(fullName);
    }

    [TestMethod]
    public void EffectiveScheme_UsesHttpsHintsBehindProxy()
    {
        static HttpRequest Request(Action<HttpRequest> configure)
        {
            var context = new DefaultHttpContext();
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("suporte.empresa.com.br");
            configure(context.Request);
            return context.Request;
        }

        Assert.AreEqual("http", Request(_ => { }).GetEffectiveScheme(false));
        Assert.AreEqual("https", Request(_ => { }).GetEffectiveScheme(true));
        Assert.AreEqual("https", Request(r => r.Headers["X-Forwarded-Proto"] = "https").GetEffectiveScheme(false));
        Assert.AreEqual("https", Request(r => r.QueryString = new QueryString("?scheme=https")).GetEffectiveScheme(false));
        Assert.AreEqual("https", Request(r => r.Headers.Referer = "https://suporte.empresa.com.br/Account/Login").GetEffectiveScheme(false));

        // Referer de outro site não vale, e a dica nunca rebaixa para http.
        Assert.AreEqual("http", Request(r => r.Headers.Referer = "https://outro.com/").GetEffectiveScheme(false));
        Assert.AreEqual("http", Request(r => r.QueryString = new QueryString("?scheme=http")).GetEffectiveScheme(false));
    }
}
