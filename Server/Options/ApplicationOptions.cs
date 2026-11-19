namespace Remotely.Server.Options;

public class ApplicationOptions
{
    public const string SectionKey = "ApplicationOptions";
    public string DbProvider { get; set; } = "SQLite";
    public string? DockerGatewayIp { get; set; }

    /// <summary>
    /// Publica a documentação da API em /swagger. Só usuários logados no painel
    /// conseguem abrir a página e o documento JSON.
    /// </summary>
    public bool EnableSwagger { get; set; } = true;

    /// <summary>
    /// Space-separated list of origins that are allowed to embed Remotely pages
    /// in an iframe (e.g. "https://glpi.example.com").  When empty, pages can only
    /// be framed by Remotely itself (clickjacking protection).
    /// </summary>
    public string? AllowedFrameAncestors { get; set; }

    /// <summary>
    /// Maximum number of authentication attempts (login, 2FA, registration,
    /// password reset, legacy API login) per client IP per minute.
    /// </summary>
    public int AuthRateLimitPerMinute { get; set; } = 20;
}
