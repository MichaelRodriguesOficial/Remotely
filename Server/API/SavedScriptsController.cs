using Microsoft.AspNetCore.Mvc;
using Remotely.Server.Auth;
using Remotely.Server.Extensions;
using Remotely.Server.Services;
using Remotely.Shared.Entities;

namespace Remotely.Server.API;

[Route("api/[controller]")]
[ApiController]
public class SavedScriptsController : ControllerBase
{
    private readonly IDataService _dataService;

    public SavedScriptsController(IDataService dataService)
    {
        _dataService = dataService;
    }

    [ServiceFilter(typeof(ExpiringTokenFilter))]
    [HttpGet("{scriptId}")]
    public async Task<ActionResult<SavedScript>> GetScript(Guid scriptId)
    {
        var result =  await _dataService.GetSavedScript(scriptId);
        if (!result.IsSuccess)
        {
            return NotFound();
        }

        // Logged-in users and API keys may only read scripts from their own organization.
        // Agents authenticate with an expiring token and have no organization header.
        if (Request.Headers.TryGetOrganizationId(out var orgId) &&
            result.Value.OrganizationID != orgId)
        {
            return NotFound();
        }

        return result.Value;
    }
}
