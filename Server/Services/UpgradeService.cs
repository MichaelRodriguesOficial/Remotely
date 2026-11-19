namespace Remotely.Server.Services;

public interface IUpgradeService
{
    /// <summary>
    /// Versão dos agentes distribuídos por este servidor (wwwroot/Content/AgentVersion.txt).
    /// Usada para indicar quais dispositivos estão com o agente desatualizado.
    /// Sem o arquivo, cai para a versão do próprio servidor.
    /// </summary>
    Version GetCurrentVersion();
    Task<bool> IsNewVersionAvailable();
}

public class UpgradeService : IUpgradeService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IWebHostEnvironment _hostEnv;
    private readonly ILogger<UpgradeService> _logger;
    private Version? _currentVersion;

    public UpgradeService(
        IHttpClientFactory httpClientFactory,
        IWebHostEnvironment hostEnv,
        ILogger<UpgradeService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _hostEnv = hostEnv;
        _logger = logger;
    }

    public Version GetCurrentVersion()
    {
        try
        {
            if (_currentVersion is not null)
            {
                return _currentVersion;
            }

            var agentVersionFile = Path.Combine(_hostEnv.WebRootPath ?? string.Empty, "Content", "AgentVersion.txt");
            if (File.Exists(agentVersionFile) &&
                Version.TryParse(File.ReadAllText(agentVersionFile).Trim(), out var agentVersion))
            {
                _currentVersion = agentVersion;
                return _currentVersion;
            }

            var asmVersion = typeof(UpgradeService).Assembly.GetName().Version;
            if (asmVersion is not null)
            {
                _currentVersion = asmVersion;
                return _currentVersion;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while getting current version.");
        }

        _currentVersion = new(1, 0);
        return _currentVersion;
    }
    public async Task<bool> IsNewVersionAvailable()
    {

        try
        {
            using var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync("https://github.com/MichaelRodriguesOficial/Remotely/releases/latest");
            var versionTag = $"{response.RequestMessage?.RequestUri}".Split("/").LastOrDefault();
            if (string.IsNullOrWhiteSpace(versionTag))
            {
                return false;
            }
            var versionString = versionTag.TrimStart('v', 'V');
            if (!Version.TryParse(versionString, out var remoteVersion))
            {
                return false;
            }

            var filePath = Directory.GetFiles(Directory.GetCurrentDirectory(), "Remotely_Server.dll", SearchOption.AllDirectories).First();
            var fileVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(filePath).FileVersion;
            if (string.IsNullOrWhiteSpace(fileVersion))
            {
                return false;
            }
            var localVersion = Version.Parse(fileVersion);

            if (remoteVersion > localVersion)
            {
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve latest version info.");
        }

        return false;
    }
}
