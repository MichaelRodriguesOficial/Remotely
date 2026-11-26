using Remotely.Shared.Enums;

namespace Remotely.Server.Models;

public class SettingsModel
{
    public static Guid DbKey { get; } = Guid.Parse("a35d6212-c0b7-49b2-89e1-7ba497f94a35");

    public bool AllowApiLogin { get; set; }
    public List<string> BannedDevices { get; set; } = [];
    public double DataRetentionInDays { get; set; } = 90;
    public string DbProvider { get; set; } = "SQLite";
    public bool EnableRemoteControlRecording { get; set; }
    public bool EnableWindowsEventLog { get; set; }
    public bool EnforceAttendedAccess { get; set; }
    public bool ForceClientHttps { get; set; }
    public List<string> KnownProxies { get; set; } = [];
    public int MaxConcurrentUpdates { get; set; } = 10;
    public int MaxOrganizationCount { get; set; } = 1;
    public string MessageOfTheDay { get; set; } = string.Empty;
    public bool RedirectToHttps { get; set; } = true;
    public bool RemoteControlNotifyUser { get; set; } = true;

    /// <summary>Aceite do usuário: "AutoAccept" (pergunta e aceita sozinho), "Ask" (exige) ou "Never".</summary>
    public string RemoteControlConsent { get; set; } = "AutoAccept";

    /// <summary>Segundos até o aceite automático (modo AutoAccept).</summary>
    public int RemoteControlConsentSeconds { get; set; } = 30;

    /// <summary>Encerra a sessão sem mouse/teclado do técnico por este tempo (0 = nunca).</summary>
    public int RemoteControlIdleMinutes { get; set; } = 15;

    /// <summary>URL (ex.: webhook do n8n) chamada quando uma sessão começa em grupo com aviso ligado.</summary>
    public string RemoteControlNotifyWebhook { get; set; } = string.Empty;

    /// <summary>E-mails (separados por vírgula ou ;) avisados quando uma sessão começa em grupo com aviso ligado.</summary>
    public string RemoteControlNotifyEmails { get; set; } = string.Empty;
    public bool RemoteControlRequiresAuthentication { get; set; } = true;
    public bool Require2FA { get; set; }
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>Roda os testes rápidos de diagnóstico sozinho nos dispositivos online.</summary>
    public bool ScheduledDiagnosticsEnabled { get; set; }

    /// <summary>Intervalo entre os diagnósticos automáticos de cada dispositivo (1 a 30 dias).</summary>
    public int ScheduledDiagnosticsIntervalDays { get; set; } = 7;

    /// <summary>Só dispara de segunda a sexta, das 8h às 18h (horário do servidor).</summary>
    public bool ScheduledDiagnosticsBusinessHoursOnly { get; set; } = true;
    public bool SmtpCheckCertificateRevocation { get; set; } = true;
    public string SmtpDisplayName { get; set; } = string.Empty;
    public string SmtpEmail { get; set; } = string.Empty;
    public string SmtpHost { get; set; } = string.Empty;
    public string SmtpLocalDomain { get; set; } = string.Empty;
    public string SmtpPassword { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string SmtpUserName { get; set; } = string.Empty;
    public Theme Theme { get; set; } = Theme.Dark;
    public List<string> TrustedCorsOrigins { get; set; } = [];
    public bool UseHsts { get; set; }
    public bool UseHttpLogging { get; set; }
    public UserNameDisplayFormat UserNameDisplayFormat { get; set; } = UserNameDisplayFormat.FirstLast;
}

/// <summary>
/// Como o nome do usuário conectado aparece nos cartões e detalhes dos dispositivos.
/// </summary>
public enum UserNameDisplayFormat
{
    /// <summary>"Michael Silva" (padrão no Brasil).</summary>
    FirstLast = 0,
    /// <summary>"Silva, Michael" (comum nos EUA).</summary>
    LastCommaFirst = 1,
    /// <summary>Exatamente como o Windows/Linux informa.</summary>
    Original = 2
}
