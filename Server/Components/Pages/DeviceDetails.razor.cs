using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Remotely.Server.Components.Devices;
using Remotely.Server.Hubs;
using Remotely.Server.Models.Messages;
using Remotely.Server.Services;
using Remotely.Shared.Entities;
using Remotely.Shared.Models.Inventory;
using Remotely.Shared.Utilities;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Remotely.Server.Components.Pages;

public partial class DeviceDetails : AuthComponentBase
{
    private readonly ConcurrentQueue<string> _logLines = new();
    private readonly ConcurrentQueue<ScriptResult> _scriptResults = new();

    private string? _alertMessage;
    private Device? _device;
    private bool _userHasAccess;
    private bool _isLoading = true;
    private DeviceGroup[] _deviceGroups = Array.Empty<DeviceGroup>();
    private DeviceInventory? _inventory;
    private bool _isRefreshingInventory;
    private Remotely.Server.Models.UserNameDisplayFormat _nameFormat;

    [Parameter]
    public string ActiveTab { get; set; } = string.Empty;

    [Parameter]
    public string DeviceId { get; set; } = string.Empty;

    [Inject]
    private ICircuitConnection CircuitConnection { get; set; } = null!;

    [Inject]
    private IDataService DataService { get; set; } = null!;


    [Inject]
    private IJsInterop JsInterop { get; set; } = null!;

    [Inject]
    private IModalService ModalService { get; set; } = null!;

    [Inject]
    private NavigationManager NavManager { get; set; } = null!;

    [Inject]
    private IToastService ToastService { get; set; } = null!;


    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        EnsureUserSet();

        _deviceGroups = DataService.GetDeviceGroups(UserName);
        _nameFormat = (await DataService.GetSettings()).UserNameDisplayFormat;

        await Register<ReceiveLogsMessage, string>(
            CircuitConnection.ConnectionId,
            HandleReceiveLogsMessage);

        await Register<InventoryUpdatedMessage, string>(
            CircuitConnection.ConnectionId,
            HandleInventoryUpdated);

        await Register<DeviceStateChangedMessage, string>(
            CircuitConnection.ConnectionId,
            HandleDeviceStateChanged);

    }

    private string? _loadedDeviceId;

    // A mesma instância da página é reaproveitada quando só o ID da rota muda
    // (lista → dispositivo, ou um dispositivo para outro). Por isso o carregamento
    // fica aqui, e não em OnInitializedAsync.
    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();

        if (string.Equals(_loadedDeviceId, DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _loadedDeviceId = DeviceId;
        _device = null;
        _inventory = null;
        _userHasAccess = false;
        _logLines.Clear();
        _scriptResults.Clear();

        if (string.IsNullOrWhiteSpace(DeviceId))
        {
            _isLoading = false;
            return;
        }

        _isLoading = true;
        EnsureUserSet();

        var deviceId = DeviceId.Trim();
        var deviceResult = await DataService.GetDevice(deviceId);
        if (deviceResult.IsSuccess)
        {
            _device = deviceResult.Value;
            _userHasAccess = DataService.DoesUserHaveAccessToDevice(_device.ID, User);
            if (_userHasAccess)
            {
                await LoadInventory();
            }
        }

        _isLoading = false;
    }

    private async Task LoadInventory()
    {
        if (_device is null)
        {
            return;
        }

        var record = await DataService.GetDeviceInventory(_device.ID);
        _inventory = InventorySerializer.FromJson(record?.Data);
    }

    private async Task HandleInventoryUpdated(object subscriber, InventoryUpdatedMessage message)
    {
        if (_device is null || message.DeviceId != _device.ID)
        {
            return;
        }

        await LoadInventory();

        if (_isRefreshingInventory)
        {
            _isRefreshingInventory = false;
            ToastService.ShowToast2("Inventário atualizado.", Enums.ToastType.Success);
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task HandleDeviceStateChanged(object subscriber, DeviceStateChangedMessage message)
    {
        if (_device is null || message.Device.ID != _device.ID)
        {
            return;
        }

        // Atualiza só o que vem do agente; os campos do formulário ficam como o usuário deixou.
        _device.IsOnline = message.Device.IsOnline;
        _device.CurrentUser = message.Device.CurrentUser;
        _device.CurrentUserGivenName = message.Device.CurrentUserGivenName;
        _device.CurrentUserSurname = message.Device.CurrentUserSurname;
        _device.OSDescription = message.Device.OSDescription;
        _device.LastOnline = message.Device.LastOnline;
        _device.DeviceType = message.Device.DeviceType;
        _device.Manufacturer = message.Device.Manufacturer;
        _device.Model = message.Device.Model;
        _device.SerialNumber = message.Device.SerialNumber;
        _device.ProcessorName = message.Device.ProcessorName;
        _device.InventoryUpdatedAt = message.Device.InventoryUpdatedAt;
        await InvokeAsync(StateHasChanged);
    }

    private async Task RefreshInventory()
    {
        if (_device is null)
        {
            return;
        }

        if (!_device.IsOnline)
        {
            ToastService.ShowToast2("O dispositivo está offline.", Enums.ToastType.Warning);
            return;
        }

        if (!await CircuitConnection.RequestInventory(_device.ID))
        {
            ToastService.ShowToast2("Não foi possível pedir o inventário ao agente.", Enums.ToastType.Warning);
            return;
        }

        _isRefreshingInventory = true;
        ToastService.ShowToast2("Coleta solicitada. O inventário aparece aqui em alguns segundos.", Enums.ToastType.Info);

        // Se o agente for antigo (sem suporte a inventário), não fica girando para sempre.
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMinutes(2));
            if (_isRefreshingInventory)
            {
                _isRefreshingInventory = false;
                await InvokeAsync(StateHasChanged);
            }
        });
    }

    private string GetUserDisplay()
    {
        if (_device is null)
        {
            return string.Empty;
        }

        var (user, fullName) = DeviceDisplay.GetUser(_device, _nameFormat);
        return fullName is null ? user : $"{fullName} · {user}";
    }

    private async Task HandleReceiveLogsMessage(object subscriber, ReceiveLogsMessage message)
    {
        _logLines.Enqueue(message.LogChunk);
        await InvokeAsync(StateHasChanged);
    }

    private async Task DeleteLogs()
    {
        if (_device is null)
        {
            return;
        }

        var result = await JsInterop.Confirm(new ConfirmOptions("Excluir logs do agente", "Os logs guardados no computador serão apagados.", "Excluir", Danger: true, Icon: "fa-solid fa-trash-can"));
        if (result)
        {
            await CircuitConnection.DeleteRemoteLogs(_device.ID);
            ToastService.ShowToast("Comando de exclusão enviado.");
        }
    }

    private void EditFormKeyDown()
    {
        _alertMessage = string.Empty;
    }

    private void GetRemoteLogs()
    {
        if (_device is null)
        {
            return;
        }

        _logLines.Clear();

        if (_device.IsOnline)
        {
            CircuitConnection.GetRemoteLogs(_device.ID);
        }
    }

    private void GetScriptHistory()
    {
        if (_device is null)
        {
            return;
        }

        EnsureUserSet();

        _scriptResults.Clear();

        if (User.IsAdministrator)
        {
            var results = DataService
                .GetAllScriptResults(User.OrganizationID, _device.ID)
                .OrderByDescending(x => x.TimeStamp);

            foreach (var result in results)
            {
                _scriptResults.Enqueue(result);
            }
        }
        else
        {
            var results = DataService
                .GetAllCommandResultsForUser(User.OrganizationID, UserName, _device.ID)
                .OrderByDescending(x => x.TimeStamp);

            foreach (var result in results)
            {
                _scriptResults.Enqueue(result);
            }
        }
    }

    private async Task HandleValidSubmit()
    {
        if (_device is null)
        {
            return;
        }

        EnsureUserSet();
        if (!_userHasAccess || !DataService.DoesUserHaveAccessToDevice(_device.ID, User))
        {
            _alertMessage = "Você não tem acesso a este dispositivo.";
            return;
        }

        if (!Permissions.ManageDevices)
        {
            _alertMessage = "Seu perfil não permite editar dispositivos.";
            return;
        }

        await DataService.UpdateDevice(
            _device.ID,
            _device.Tags,
            _device.Alias,
            _device.DeviceGroupID,
            _device.Notes);

        await DataService.UpdateDeviceAsset(_device.ID, _device.AssetTag, _device.Location);

        _alertMessage = string.Empty;
        ToastService.ShowToast2("Detalhes do dispositivo salvos.", Enums.ToastType.Success);
    }

    private string GetOsIcon()
    {
        var platform = $"{_device?.Platform} {_device?.OSDescription}".ToLowerInvariant();
        if (platform.Contains("win"))
        {
            return "fa-brands fa-windows";
        }
        if (platform.Contains("osx") || platform.Contains("mac"))
        {
            return "fa-brands fa-apple";
        }
        if (platform.Contains("linux"))
        {
            return "fa-brands fa-linux";
        }
        return "fa-solid fa-computer";
    }
}