using Remotely.Server.Models;
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

    public static (string UserName, string? FullName) GetUser(
        Device device,
        UserNameDisplayFormat format = UserNameDisplayFormat.FirstLast)
    {
        var value = device.CurrentUser?.Trim() ?? string.Empty;
        var match = _userRegex.Match(value);
        var userName = match.Success ? match.Groups["user"].Value.Trim() : value;
        var displayName = match.Success ? match.Groups["name"].Value.Trim() : null;

        if (format == UserNameDisplayFormat.Original)
        {
            return (userName, displayName);
        }

        var (given, surname) = GetNameParts(device.CurrentUserGivenName, device.CurrentUserSurname, displayName);
        return (userName, FormatName(given, surname, format) ?? displayName);
    }

    /// <summary>
    /// Separa nome e sobrenome. Usa os campos enviados pelo agente (vindos do
    /// AD/Entra ID) e, se não houver, interpreta o nome de exibição:
    /// "Silva, Michael" (formato do AD) ou "Michael Silva".
    /// </summary>
    public static (string? GivenName, string? Surname) GetNameParts(
        string? givenName,
        string? surname,
        string? displayName)
    {
        if (!string.IsNullOrWhiteSpace(givenName) || !string.IsNullOrWhiteSpace(surname))
        {
            return (Clean(givenName), Clean(surname));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            return (null, null);
        }

        var commaIndex = displayName.IndexOf(',');
        if (commaIndex > 0)
        {
            return (Clean(displayName[(commaIndex + 1)..]), Clean(displayName[..commaIndex]));
        }

        var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => (null, null),
            1 => (parts[0], null),
            _ => (parts[0], string.Join(' ', parts[1..]))
        };
    }

    public static string? FormatName(string? givenName, string? surname, UserNameDisplayFormat format)
    {
        givenName = Clean(givenName);
        surname = Clean(surname);

        if (givenName is null && surname is null)
        {
            return null;
        }
        if (givenName is null || surname is null)
        {
            return givenName ?? surname;
        }

        return format == UserNameDisplayFormat.LastCommaFirst
            ? $"{surname}, {givenName}"
            : $"{givenName} {surname}";
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
