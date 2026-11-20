using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Remotely.Server.Components.Pages;
using Remotely.Server.Enums;
using Remotely.Server.Services;
using Remotely.Shared.Entities;
using Remotely.Shared.Utilities;

namespace Remotely.Server.Components.Scripts;

[Authorize]
public partial class ScriptSchedules : AuthComponentBase
{
    private readonly List<string> _selectedDeviceGroups = new();
    private readonly List<string> _selectedDevices = new();
    private readonly List<ScriptSchedule> _schedules = new();
    private DeviceGroup[] _deviceGroups = Array.Empty<DeviceGroup>();
    private Device[] _devices = Array.Empty<Device>();
    private Guid? _selectedScriptId;
    private ScriptSchedule _selectedSchedule = NewSchedule();

    [CascadingParameter]
    private ScriptsPage ParentPage { get; set; } = null!;

    [Inject]
    private IDataService DataService { get; set; } = null!;

    [Inject]
    private IJsInterop JsInterop { get; set; } = null!;

    [Inject]
    private IToastService ToastService { get; set; } = null!;

    [Inject]
    public required ILogger<ScriptSchedules> Logger { get; set; }

    private bool IsNew => _selectedSchedule.Id == 0;

    private bool CanModifySchedule
    {
        get
        {
            if (User is null)
            {
                return false;
            }
            return
                IsNew ||
                _selectedSchedule.CreatorId == User.Id ||
                User.IsAdministrator;
        }
    }

    private DateTime StartAtLocal
    {
        get => _selectedSchedule.StartAt.ToLocalTime().DateTime;
        set => _selectedSchedule.StartAt = new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Local));
    }

    private IEnumerable<IGrouping<string, SavedScript>> ScriptsByFolder => ParentPage.VisibleScripts
        .GroupBy(x => string.IsNullOrWhiteSpace(x.FolderPath) ? "Sem pasta" : x.FolderPath.Trim('/'))
        .OrderBy(x => x.Key == "Sem pasta" ? 1 : 0)
        .ThenBy(x => x.Key);

    private static ScriptSchedule NewSchedule() => new()
    {
        Name = string.Empty,
        StartAt = Time.Now,
        Interval = Remotely.Shared.Enums.RepeatInterval.Daily,
        RunOnNextConnect = true
    };

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        EnsureUserSet();

        _deviceGroups = DataService.GetDeviceGroups(UserName);
        _devices = DataService
            .GetDevicesForUser(UserName)
            .OrderBy(x => x.DeviceName)
            .ToArray();

        await RefreshSchedules();

        if (ParentPage.TakePendingScriptId() is Guid pendingId)
        {
            _selectedScriptId = pendingId;
            var script = ParentPage.AllScripts.FirstOrDefault(x => x.Id == pendingId);
            if (script is not null)
            {
                _selectedSchedule.Name = script.Name;
            }
        }
    }

    private string GetScriptName(Guid scriptId)
    {
        return ParentPage.AllScripts.FirstOrDefault(x => x.Id == scriptId)?.Name ?? "Script indisponível";
    }

    private void ScriptChanged(ChangeEventArgs args)
    {
        _selectedScriptId = Guid.TryParse($"{args.Value}", out var id) ? id : null;
        if (_selectedScriptId is Guid scriptId && string.IsNullOrWhiteSpace(_selectedSchedule.Name))
        {
            _selectedSchedule.Name = GetScriptName(scriptId);
        }
    }

    private void CreateNew()
    {
        _selectedScriptId = null;
        _selectedSchedule = NewSchedule();
        _selectedDeviceGroups.Clear();
        _selectedDevices.Clear();
    }

    private async Task DeleteSelectedSchedule()
    {
        try
        {
            if (!CanModifySchedule)
            {
                ToastService.ShowToast2("Você não pode excluir agendamentos de outras pessoas.", ToastType.Warning);
                return;
            }

            var result = await JsInterop.Confirm($"Excluir o agendamento \"{_selectedSchedule.Name}\"?");
            if (!result)
            {
                return;
            }

            await DataService.DeleteScriptSchedule(_selectedSchedule.Id);
            ToastService.ShowToast2("Agendamento excluído.", ToastType.Success);
            CreateNew();
            await RefreshSchedules();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error while deleting script schedule.");
            ToastService.ShowToast2("Falha ao excluir o agendamento.", ToastType.Error);
        }
    }

    private async Task OnValidSubmit(EditContext context)
    {
        EnsureUserSet();

        if (!CanModifySchedule)
        {
            ToastService.ShowToast2("Você não pode alterar agendamentos de outras pessoas.", ToastType.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedSchedule.Name))
        {
            ToastService.ShowToast2("Dê um nome ao agendamento.", ToastType.Warning);
            return;
        }

        if (_selectedScriptId is not Guid scriptId)
        {
            ToastService.ShowToast2("Selecione o script.", ToastType.Warning);
            return;
        }

        if (!_selectedDevices.Any() && !_selectedDeviceGroups.Any())
        {
            ToastService.ShowToast2("Selecione pelo menos um dispositivo ou grupo.", ToastType.Warning);
            return;
        }

        try
        {
            _selectedSchedule.SavedScriptId = scriptId;
            if (string.IsNullOrWhiteSpace(_selectedSchedule.CreatorId))
            {
                _selectedSchedule.CreatedAt = Time.Now;
                _selectedSchedule.CreatorId = User.Id;
            }
            _selectedSchedule.OrganizationID = User.OrganizationID;
            _selectedSchedule.NextRun = _selectedSchedule.StartAt;
            _selectedSchedule.Devices = _devices.Where(x => _selectedDevices.Contains(x.ID)).ToList();
            _selectedSchedule.DeviceGroups = _deviceGroups.Where(x => _selectedDeviceGroups.Contains(x.ID)).ToList();

            await DataService.AddOrUpdateScriptSchedule(_selectedSchedule);
            ToastService.ShowToast2("Agendamento salvo.", ToastType.Success);

            var savedId = _selectedSchedule.Id;
            await RefreshSchedules();
            var saved = _schedules.FirstOrDefault(x => x.Id == savedId);
            if (saved is not null)
            {
                SelectSchedule(saved);
            }
            else
            {
                CreateNew();
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error while saving script schedule.");
            ToastService.ShowToast2("Falha ao salvar o agendamento.", ToastType.Error);
        }
    }

    private async Task RefreshSchedules()
    {
        _schedules.Clear();
        if (User is not null)
        {
            _schedules.AddRange(await DataService.GetScriptSchedules(User.OrganizationID));
        }
    }

    private void SelectSchedule(ScriptSchedule schedule)
    {
        _selectedSchedule = schedule;
        _selectedScriptId = schedule.SavedScriptId;
        _selectedDevices.Clear();
        _selectedDeviceGroups.Clear();
        _selectedDevices.AddRange(schedule.Devices?.Select(x => x.ID) ?? Enumerable.Empty<string>());
        _selectedDeviceGroups.AddRange(schedule.DeviceGroups?.Select(x => x.ID) ?? Enumerable.Empty<string>());
    }
}
