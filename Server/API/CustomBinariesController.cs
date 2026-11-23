using Microsoft.AspNetCore.Mvc;
using Remotely.Server.Extensions;
using Remotely.Server.Services;
using Remotely.Server.Utilities;
using Remotely.Shared.Models;
using Remotely.Shared.Services;

namespace Remotely.Server.API;

[Route("api/custom-binaries")]
[ApiController]
// O arquivo leva o endereço do servidor; sem isso o navegador pode
// reaproveitar um download antigo (com outro endereço ou esquema).
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class CustomBinariesController(
    IDataService _dataService,
    IWebHostEnvironment _hostingEnvironment,
    IEmbeddedServerDataProvider _embeddedData) : ControllerBase
{
    [HttpGet("win-x86/desktop/{organizationId}")]
    public async Task<IActionResult> GetWinX86Desktop(string organizationId)
    {
        var embeddedData = await GetEmbeddedData(organizationId);
        var filePath = Path.Combine(_hostingEnvironment.ContentRootPath, "AppData", "Win-x86", "Remotely_Desktop.exe");
        if (!System.IO.File.Exists(filePath))
        {
            return NotFound();
        }
        var rs = new AppendedDataStream(filePath, _embeddedData.CreateTrailer(embeddedData));
        return File(rs, "application/octet-stream", "Remotely_Desktop_x86.exe");
    }

    [HttpGet("win-x64/desktop/{organizationId}")]
    public async Task<IActionResult> GetWinX64Desktop(string organizationId)
    {
        var embeddedData = await GetEmbeddedData(organizationId);
        var filePath = Path.Combine(_hostingEnvironment.ContentRootPath, "AppData", "Win-x64", "Remotely_Desktop.exe");
        if (!System.IO.File.Exists(filePath))
        {
            return NotFound();
        }
        var rs = new AppendedDataStream(filePath, _embeddedData.CreateTrailer(embeddedData));
        return File(rs, "application/octet-stream", "Remotely_Desktop_x64.exe");
    }

    private async Task<EmbeddedServerData> GetEmbeddedData(string? organizationId)
    {
        var defaultOrg = await _dataService.GetDefaultOrganization();

        // The default org will be used if unspecified, so might as well save the
        // space in the file name.
        if (defaultOrg.IsSuccess &&
            defaultOrg.Value.ID.Equals(organizationId, StringComparison.OrdinalIgnoreCase))
        {
            organizationId = null;
        }

        var settings = await _dataService.GetSettings();
        var effectiveScheme = Request.GetEffectiveScheme(settings.ForceClientHttps);
        var serverUrl = $"{effectiveScheme}://{Request.Host}";
        return new EmbeddedServerData(new Uri(serverUrl), organizationId);
    }
}
