namespace Remotely.Server.Options;

public class ApplicationOptions
{
    public const string SectionKey = "ApplicationOptions";
    public string DbProvider { get; set; } = "SQLite";
    public string? DockerGatewayIp { get; set; }

    /// <summary>
    /// De quem aceitar X-Forwarded-For/X-Forwarded-Proto.
    /// Sem valor (padrão): no Docker, só das redes internas do próprio contêiner (por onde
    /// chega o proxy reverso). true: de qualquer rede privada (10.x, 172.16–31.x, 192.168.x),
    /// para proxies em outra máquina da rede. false: só loopback e "Proxies conhecidos".
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
