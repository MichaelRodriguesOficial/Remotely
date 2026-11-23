using Remotely.Shared.Entities;
using System.Text.RegularExpressions;

namespace Remotely.Server.Components.Devices;

/// <summary>
/// Formata dados do dispositivo para exibição.
/// Os agentes enviam o usuário como "usuario (Nome Sobrenome)" e o sistema como
/// "Microsoft Windows 11 Enterprise (10.0.26300 · 25H2)". Agentes antigos enviam só
/// "usuario" e "Microsoft Windows 10.0.26300"; esses casos também são tratados.
/// </summary>
public static class DeviceDisplay
{
    private static readonly Regex _userRegex = new(@"^(?<user>[^()]+?)\s+\((?<name>[^()]+)\)$", RegexOptions.Compiled);
    private static readonly Regex _osRegex = new(@"^(?<name>.+?)\s*\((?<version>[^()]+)\)$", RegexOptions.Compiled);
    private static readonly Regex _legacyWindowsRegex = new(
        @"^Microsoft Windows (?<version>(?<major>\d+)\.(?<minor>\d+)\.(?<build>\d+)(?:\.\d+)?)\s*$",
        RegexOptions.Compiled);

    public static (string UserName, string? FullName) GetUser(Device device)
    {
        var value = device.CurrentUser?.Trim() ?? string.Empty;
        var match = _userRegex.Match(value);
        return match.Success
            ? (match.Groups["user"].Value.Trim(), match.Groups["name"].Value.Trim())
            : (value, null);
    }

    public static (string Name, string? Version) GetOperatingSystem(Device device)
    {
        var description = device.OSDescription?.Trim();
        if (string.IsNullOrWhiteSpace(description))
        {
            return (device.Platform ?? string.Empty, null);
        }

        var legacy = _legacyWindowsRegex.Match(description);
        if (legacy.Success)
        {
            var build = int.Parse(legacy.Groups["build"].Value);
            var name = build >= 22000 ? "Microsoft Windows 11" : "Microsoft Windows 10";
            var version = $"{legacy.Groups["major"].Value}.{legacy.Groups["minor"].Value}.{legacy.Groups["build"].Value}";
            return (name, version);
        }

        var match = _osRegex.Match(description);
        return match.Success
            ? (match.Groups["name"].Value.Trim(), match.Groups["version"].Value.Trim())
            : (description, null);
    }
}
