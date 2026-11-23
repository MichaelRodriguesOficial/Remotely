using Microsoft.AspNetCore.Mvc;
using Remotely.Server.Auth;
using Remotely.Server.Extensions;
using Remotely.Server.Services;
using Remotely.Shared.Extensions;
using Remotely.Shared.Models;
using Remotely.Shared.Services;
using System.Text;
using System.Text.RegularExpressions;
using FileIO = System.IO.File;

namespace Remotely.Server.API;

[Route("api/[controller]")]
[ApiController]
public class ClientDownloadsController : ControllerBase
{
    // Controllers are created per request, so the lock must be static to
    // actually serialize access to the installer files.
    private static readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly IDataService _dataService;
    private readonly IEmbeddedServerDataProvider _embeddedDataSearcher;
    private readonly IWebHostEnvironment _hostEnv;
    private readonly ILogger<ClientDownloadsController> _logger;

    public ClientDownloadsController(
        IWebHostEnvironment hostEnv,
        IEmbeddedServerDataProvider embeddedDataSearcher,
        IDataService dataService,
        ILogger<ClientDownloadsController> logger)
    {
        _hostEnv = hostEnv;
        _embeddedDataSearcher = embeddedDataSearcher;
        _dataService = dataService;
        _logger = logger;
    }

    [HttpGet("desktop/{platformID}")]
    public async Task<IActionResult> GetDesktop(string platformID)
    {
        switch (platformID)
        {
            case "WindowsDesktop-x64":
                {
                    var filePath = Path.Combine("Content", "Win-x64", "Remotely_Desktop.exe");
                    return await GetDesktopFile(filePath);
                }
            case "WindowsDesktop-x86":
                {
                    var filePath = Path.Combine("Content", "Win-x86", "Remotely_Desktop.exe");
                    return await GetDesktopFile(filePath);
                }
            case "UbuntuDesktop":
                {
                    var filePath = Path.Combine("Content", "Linux-x64", "Remotely_Desktop");
                    return await GetDesktopFile(filePath);
                }
            case "MacOS-x64":
                {
                    var filePath = Path.Combine("Content", "MacOS-x64", "Remotely_Desktop");
                    return await GetDesktopFile(filePath);
                }
            case "MacOS-arm64":
                {
                    var filePath = Path.Combine("Content", "MacOS-arm64", "Remotely_Desktop");
                    return await GetDesktopFile(filePath);
                }
            default:
                return NotFound();
        }
    }


    [HttpGet("desktop/{platformId}/{organizationId}")]
    public async Task<IActionResult> GetDesktop(string platformId, string organizationId)
    {
        if (!await IsValidOrganizationId(organizationId))
        {
            return NotFound();
        }

        switch (platformId)
        {
            case "WindowsDesktop-x64":
                {
                    var filePath = Path.Combine("Content", "Win-x64", "Remotely_Desktop.exe");
                    return await GetDesktopFile(filePath, organizationId);
                }
            case "WindowsDesktop-x86":
                {
                    var filePath = Path.Combine("Content", "Win-x86", "Remotely_Desktop.exe");
                    return await GetDesktopFile(filePath, organizationId);
                }
            case "UbuntuDesktop":
                {
                    var filePath = Path.Combine("Content", "Linux-x64", "Remotely_Desktop");
                    return await GetDesktopFile(filePath, organizationId);
                }
            case "MacOS-x64":
                {
                    var filePath = Path.Combine("Content", "MacOS-x64", "Remotely_Desktop");
                    return await GetDesktopFile(filePath, organizationId);
                }
            case "MacOS-arm64":
                {
                    var filePath = Path.Combine("Content", "MacOS-arm64", "Remotely_Desktop");
                    return await GetDesktopFile(filePath, organizationId);
                }
            default:
                return NotFound();
        }
    }

    [ServiceFilter(typeof(ApiAuthorizationFilter))]
    [HttpGet("{platformID}")]
    public async Task<IActionResult> GetInstaller(string platformID)
    {
        if (!Request.Headers.TryGetOrganizationId(out var orgId))
        {
            return Unauthorized();
        }
        return await GetInstallFile(orgId, platformID);
    }

    [HttpGet("{platformId}/{organizationId}")]
    public async Task<IActionResult> GetInstaller(string platformId, string organizationId)
    {
        // The organization ID is written verbatim into the installer scripts,
        // so it must be validated to prevent script injection via crafted links.
        if (!await IsValidOrganizationId(organizationId))
        {
            return NotFound();
        }

        return await GetInstallFile(organizationId, platformId);
    }

    private async Task<IActionResult> GetBashInstaller(string fileName, string organizationId)
    {
        var fileContents = new List<string>();
        fileContents.AddRange(await FileIO.ReadAllLinesAsync(Path.Combine(_hostEnv.WebRootPath, "Content", fileName)));

        var hostIndex = fileContents.IndexOf("HostName=");
        var orgIndex = fileContents.IndexOf("Organization=");

        if (hostIndex < 0 || orgIndex < 0)
        {
            _logger.LogError("Installer template {fileName} is missing HostName/Organization placeholders.", fileName);
            return NotFound();
        }

        var settings = await _dataService.GetSettings();
        var effectiveScheme = settings.ForceClientHttps ? "https" : Request.Scheme;

        fileContents[hostIndex] = $"HostName=\"{effectiveScheme}://{Request.Host}\"";
        fileContents[orgIndex] = $"Organization=\"{organizationId}\"";
        var fileBytes = Encoding.UTF8.GetBytes(string.Join("\n", fileContents));
        return File(fileBytes, "application/octet-stream", fileName);
    }

    private async Task<IActionResult> GetDesktopFile(string relativeFilePath, string? organizationId = null)
    {
        await LogRequest(nameof(GetDesktopFile));
        var defaultOrg = await _dataService.GetDefaultOrganization();

        // The default org will be used if unspecified, so might as well save the
        // space in the file name.
        if (defaultOrg.IsSuccess && 
            defaultOrg.Value.ID.Equals(organizationId, StringComparison.OrdinalIgnoreCase))
        {
            organizationId = null;
        }

        var settings = await _dataService.GetSettings();
        var effectiveScheme = settings.ForceClientHttps ? "https" : Request.Scheme;
        var serverUrl = $"{effectiveScheme}://{Request.Host}";

        var embeddedData = new EmbeddedServerData(new Uri(serverUrl), organizationId);

        // Nome curto, com o endereço do servidor que o cliente lê ao abrir:
        // "Remotely_Desktop_x64 (suporte.empresa.com.br).exe".
        var normalizedPath = relativeFilePath.Replace('\\', '/');
        var displayName = normalizedPath switch
        {
            _ when normalizedPath.Contains("/Win-x64/") => "Remotely_Desktop_x64",
            _ when normalizedPath.Contains("/Win-x86/") => "Remotely_Desktop_x86",
            _ => "Remotely_Desktop"
        };
        var fileName = _embeddedDataSearcher.GetFriendlyFileName(
            displayName,
            Path.GetExtension(relativeFilePath),
            embeddedData);

        return File(relativeFilePath, "application/octet-stream", fileName);
    }

    private async Task<IActionResult> GetInstallFile(string organizationId, string platformID)
    {
        var settings = await _dataService.GetSettings();
        await LogRequest(nameof(GetInstallFile));

        if (!await _fileLock.WaitAsync(TimeSpan.FromSeconds(15)))
        {
            return StatusCode(StatusCodes.Status408RequestTimeout);
        }

        try
        {
            switch (platformID)
            {
                case "WindowsInstaller":
                    {
                        var effectiveScheme = settings.ForceClientHttps ? "https" : Request.Scheme;

                        var filePath = Path.Combine(_hostEnv.WebRootPath, "Content", "Install-Remotely.ps1");
                        if (!FileIO.Exists(filePath))
                        {
                            return NotFound();
                        }
                        
                        var fileContents = await FileIO.ReadAllLinesAsync(filePath);
                        var hostIndex = fileContents.IndexWhere(x => 
                            x.Contains("[string]$HostName = $null", StringComparison.OrdinalIgnoreCase));
                        var orgIndex = fileContents.IndexWhere(x => 
                            x.Contains("[string]$Organization = $null", StringComparison.OrdinalIgnoreCase));

                        if (hostIndex < 0 || orgIndex < 0)
                        {
                            return NotFound();
                        }

                        fileContents[hostIndex] = $"[string]$HostName = \"{effectiveScheme}://{Request.Host}\"";
                        fileContents[orgIndex] = $"[string]$Organization = \"{organizationId}\"";
                        var fileBytes = Encoding.UTF8.GetBytes(string.Join("\n", fileContents));

                        return File(fileBytes, "application/octet-stream", "Install-Remotely.ps1");
                    }
                case "ManjaroInstaller-x64":
                    {
                        var fileName = "Install-Manjaro-x64.sh";

                        return await GetBashInstaller(fileName, organizationId);
                    }
                case "UbuntuInstaller-x64":
                    {
                        var fileName = "Install-Ubuntu-x64.sh";

                        return await GetBashInstaller(fileName, organizationId);
                    }
                case "MacOSInstaller-x64":
                    {
                        var fileName = "Install-MacOS-x64.sh";

                        return await GetBashInstaller(fileName, organizationId);
                    }
                case "MacOSInstaller-arm64":
                    {
                        var fileName = "Install-MacOS-arm64.sh";

                        return await GetBashInstaller(fileName, organizationId);
                    }
                default:
                    return BadRequest();
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private static readonly Regex _organizationIdRegex = new("^[A-Za-z0-9-]{1,64}$", RegexOptions.Compiled);

    private async Task<bool> IsValidOrganizationId(string? organizationId)
    {
        if (string.IsNullOrWhiteSpace(organizationId) ||
            !_organizationIdRegex.IsMatch(organizationId))
        {
            return false;
        }

        var orgResult = await _dataService.GetOrganizationNameById(organizationId);
        return orgResult.IsSuccess;
    }

    private async Task LogRequest(string methodName)
    {
        var settings = await _dataService.GetSettings();
        if (settings.UseHttpLogging)
        {
            var ip = Request.HttpContext.Connection.RemoteIpAddress;
            if (ip?.IsIPv4MappedToIPv6 == true)
            {
                ip = ip.MapToIPv4();
            }

            var effectiveScheme = settings.ForceClientHttps ? "https" : Request.Scheme;

            _logger.LogInformation(
                "Started client download via {methodName}.  Effective Scheme: {scheme}.  Effective Host: {host}.  Remote IP: {ip}.",
                methodName,
                effectiveScheme,
                Request.Host,
                $"{ip}");
        }
    }
}
