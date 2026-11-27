using Remotely.Server.Models;
using Remotely.Shared.Entities;

namespace Remotely.Server.Services;

/// <summary>Regras de aceite do usuário no controle remoto (servidor e grupos de dispositivos).</summary>
public static class RemoteControlPolicy
{
    public const string AutoAccept = "AutoAccept";
    public const string Ask = "Ask";
    public const string Never = "Never";

    public static IReadOnlyList<(string Value, string Label)> ConsentOptions { get; } =
    [
        (AutoAccept, "Perguntar e aceitar sozinho"),
        (Ask, "Sempre exigir o aceite"),
        (Never, "Não perguntar")
    ];

    public static string Normalize(string? mode) => mode switch
    {
        Ask or Never or AutoAccept => mode,
        _ => AutoAccept
    };

    /// <summary>Modo do servidor. A opção antiga "Pedir consentimento" ligada vale como "Ask".</summary>
    public static string ServerMode(SettingsModel settings) =>
        settings.EnforceAttendedAccess ? Ask : Normalize(settings.RemoteControlConsent);

    /// <summary>
    /// Aceite efetivo para um computador: o do grupo, se definido, senão o do servidor.
    /// AutoAcceptSeconds &gt; 0 só no modo AutoAccept.
    /// </summary>
    public static (bool RequireConsent, int AutoAcceptSeconds, string Mode) Resolve(SettingsModel settings, DeviceGroup? group)
    {
        var mode = string.IsNullOrWhiteSpace(group?.RemoteControlConsent)
            ? ServerMode(settings)
            : Normalize(group!.RemoteControlConsent);

        return mode switch
        {
            Never => (false, 0, mode),
            Ask => (true, 0, mode),
            _ => (true, Math.Clamp(settings.RemoteControlConsentSeconds, 5, 300), mode)
        };
    }

    public static string Describe(string? mode) =>
        ConsentOptions.FirstOrDefault(x => x.Value == mode).Label ?? "Padrão do servidor";

    public const string PrivacyAllow = "Allow";
    public const string PrivacyAuto = "Auto";
    public const string PrivacyDeny = "Deny";

    /// <summary>Opções da tela preta para o grupo.</summary>
    public static readonly (string Value, string Label)[] PrivacyOptions =
    [
        (PrivacyAllow, "O técnico pode ligar"),
        (PrivacyAuto, "Liga ao conectar"),
        (PrivacyDeny, "Desativada")
    ];

    public static string NormalizePrivacy(string? value) =>
        string.Equals(value, PrivacyAuto, StringComparison.OrdinalIgnoreCase) ? PrivacyAuto :
        string.Equals(value, PrivacyDeny, StringComparison.OrdinalIgnoreCase) ? PrivacyDeny :
        PrivacyAllow;
}
