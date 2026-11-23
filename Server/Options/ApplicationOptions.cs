namespace Remotely.Server.Options;

public class ApplicationOptions
{
    public const string SectionKey = "ApplicationOptions";
    public string DbProvider { get; set; } = "SQLite";
    public string? DockerGatewayIp { get; set; }

    /// <summary>
    /// Aceita os cabeçalhos X-Forwarded-* de proxies em redes privadas
    /// (10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16 e fc00::/7).
    /// Sem valor, fica ligado quando o servidor roda em container (Docker), onde o proxy
    /// reverso (Nginx, aaPanel, Traefik) chega por uma rede interna do Docker.
    /// </summary>
    public bool? TrustPrivateNetworkProxies { get; set; }

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
