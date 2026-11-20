using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Remotely.Server.Components.Pages;
using Remotely.Server.Enums;
using Remotely.Server.Hubs;
using Remotely.Server.Services;
using Remotely.Shared.Entities;
using Remotely.Shared.Enums;
using Remotely.Shared.Utilities;

namespace Remotely.Server.Components.Scripts;

[Authorize]
public partial class RunScript : AuthComponentBase
{
    private readonly List<string> _selectedDeviceGroups = new();
    private readonly List<string> _selectedDevices = new();
    private DeviceGroup[] _deviceGroups = Array.Empty<DeviceGroup>();
    private Device[] _devices = Array.Empty<Device>();
    private bool _runOnNextConnect = true;
    private bool _isRunning;
    private string? _lastRun;
    private SavedScript? _selectedScript;

    [Inject]
    private IDataService DataService { get; init; } = null!;

    [Inject]
    private IToastService ToastService { get; init; } = null!;

    [Inject]
    private IAgentHubSessionCache ServiceSessionCache { get; init; } = null!;

    [Inject]
    private ICircuitConnection CircuitConnection { get; init; } = null!;

    [Inject]
    private ILogger<RunScript> Logger { get; init; } = null!;

    [CascadingParameter]
    private ScriptsPage ParentPage { get; init; } = null!;

    private bool CanRun => _selectedScript is not null && TargetDeviceIds.Count > 0;

    private List<string> TargetDeviceIds => _devices
        .Where(x =>
            _selectedDevices.Contains(x.ID) ||
            (x.DeviceGroupID is not null && _selectedDeviceGroups.Contains(x.DeviceGroupID)))
        .Select(x => x.ID)
        .ToList();

    private int OnlineTargetCount => _devices.Count(x => x.IsOnline && TargetDeviceIds.Contains(x.ID));

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        EnsureUserSet();

        _deviceGroups = DataService.GetDeviceGroups(UserName);
        _devices = DataService
            .GetDevicesForUser(UserName)
            .OrderBy(x => x.DeviceName)
            .ToArray();

        if (ParentPage.TakePendingScriptId() is Guid pendingId)
        {
            await LoadScript(pendingId);
        }
    }

    private async Task ExecuteScript()
    {
        EnsureUserSet();

        if (_selectedScript is null || TargetDeviceIds.Count == 0)
        {
            return;
        }

        _isRunning = true;
        try
        {
            var filteredDevices = DataService.FilterDeviceIdsByUserPermission(TargetDeviceIds.ToArray(), User);
            var onlineDevices = ServiceSessionCache.FilterDevicesByOnlineStatus(filteredDevices, true).ToArray();

            var scriptRun = new ScriptRun()
            {
                OrganizationID = User.OrganizationID,
                RunAt = Time.Now,
                SavedScriptId = _selectedScript.Id,
                RunOnNextConnect = _runOnNextConnect,
                InputType = ScriptInputType.OneTimeScript,
                Initiator = User.UserName,
                Devices = DataService.GetDevices(_runOnNextConnect ? filteredDevices : onlineDevices)
            };

            await DataService.AddScriptRun(scriptRun);
            await CircuitConnection.RunScript(onlineDevices, _selectedScript.Id, scriptRun.Id, ScriptInputType.OneTimeScript, false);

            var pending = scriptRun.Devices.Count - onlineDevices.Length;
            _lastRun = pending > 0
                ? $"Rodando agora em {onlineDevices.Length} dispositivo(s); {pending} vão executar quando conectarem."
                : $"Rodando agora em {onlineDevices.Length} dispositivo(s).";

            ToastService.ShowToast2(_lastRun, ToastType.Success);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error while running script.");
            ToastService.ShowToast2("Falha ao executar o script.", ToastType.Error);
        }
        finally
        {
            _isRunning = false;
        }
    }

    private void OnTargetsChanged()
    {
        _lastRun = null;
    }

    private async Task ScriptSelected(SavedScript script)
    {
        await LoadScript(script.Id);
    }

    private async Task LoadScript(Guid scriptId)
    {
        EnsureUserSet();
        var result = await DataService.GetSavedScript(User.Id, scriptId);
        _selectedScript = result.IsSuccess ? result.Value : null;
        _lastRun = null;
    }
}
