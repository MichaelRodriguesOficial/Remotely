using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Remotely.Server.Enums;
using Remotely.Server.Hubs;
using Remotely.Server.Models.Messages;
using Remotely.Server.Services;
using Remotely.Server.Services.Stores;
using Remotely.Shared;
using Remotely.Shared.Entities;
using Remotely.Shared.Enums;
using Remotely.Shared.Utilities;
using Remotely.Shared.ViewModels;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Remotely.Server.Components.Devices;

public partial class DeviceCard : AuthComponentBase
{
    private bool _sendFileOpen;
    private ElementReference _card;
    private Version _currentVersion = new();
    private Theme _theme;
    private DeviceCardState _state;
    private DeviceGroup[] _deviceGroups = Array.Empty<DeviceGroup>();
    private Remotely.Shared.Models.Inventory.DeviceInventory? _inventory;
    private Remotely.Server.Models.UserPermissions _permissions = Remotely.Server.Models.UserPermissions.Full;

    /// <summary>Aba do terminal do dispositivo aberto pelo menu "Mais" (null = fechado).</summary>
    private string? _shellTab;

    [Parameter]
    public Device Device { get; set; } = null!;

    [CascadingParameter]
    public DevicesFrame ParentFrame { get; init; } = null!;


    [Inject]
    public required ISelectedCardsStore SelectedCards { get; init; }

    [Inject]
    public required IThemeProvider ThemeProvider { get; init; }

    [Inject]
    public required ICircuitConnection CircuitConnection { get; init; }

    [Inject]
    public required IDataService DataService { get; init; }

    [Inject]
    public required IChatSessionStore ChatCache { get; init; }

    [Inject]
    public required ILogger<DeviceCard> Logger { get; init; }

    private bool IsExpanded => _state == DeviceCardState.Expanded;

    private Remotely.Server.Models.UserNameDisplayFormat NameFormat =>
        ParentFrame?.NameFormat ?? Remotely.Server.Models.UserNameDisplayFormat.FirstLast;

    private bool IsOutdated =>
        Version.TryParse(Device.AgentVersion, out var result) &&
        result < _currentVersion;

    private bool IsSelected => SelectedCards.SelectedDevices.Contains(Device.ID);

    [Inject]
    private IJsInterop JsInterop { get; init; } = null!;

    [Inject]
    private IModalService ModalService { get; init; } = null!;

    [Inject]
    private IAgentHubSessionCache ServiceSessionCache { get; init; } = null!;

    [Inject]
    private IToastService ToastService { get; init; } = null!;

    [Inject]
    private IUpgradeService UpgradeService { get; init; } = null!;

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        EnsureUserSet();
        _theme = await ThemeProvider.GetEffectiveTheme();
        _currentVersion = UpgradeService.GetCurrentVersion();
        _permissions = Permissions;
        _deviceGroups = DataService.GetDeviceGroups(UserName);

        await Register<DeviceCardStateChangedMessage, string>(
            CircuitConnection.ConnectionId,
            HandleDeviceCardStateChanged);

        await Register<FileTransferResultMessage, string>(
            CircuitConnection.ConnectionId,
            HandleFileTransferResult);

        await Register<DeviceStateChangedMessage, string>(
            CircuitConnection.ConnectionId,
            HandleDeviceStateChanged);
    }

    private async Task HandleDeviceCardStateChanged(object subscriber, DeviceCardStateChangedMessage message)
    {
        if (message.DeviceId == Device.ID)
        {
            if (message.State == _state)
            {
                return;
            }
            _state = message.State;
            if (_state == DeviceCardState.Expanded)
            {
                // Os mesmos dados da página de detalhes (UUID, série do SO etc.).
                var record = await DataService.GetDeviceInventory(Device.ID);
                _inventory = Remotely.Shared.Models.Inventory.InventorySerializer.FromJson(record?.Data);
            }
            await InvokeAsync(StateHasChanged);
        }
        else
        {
            if (_state == DeviceCardState.Normal)
            {
                return;
            }
            _state = DeviceCardState.Normal;
            await InvokeAsync(StateHasChanged);
            return;
        }
    }

    private async Task HandleDeviceStateChanged(object subscriber, DeviceStateChangedMessage message)
    {
        if (message.Device.ID != Device.ID)
        {
            return;
        }

        // TODO: It would be cool to decorate user-editable properties
        // with a "UserEditable" attribute, then use a source generator
        // to create/update a method that copies property values for
        // those that do not have the attribute.  We could do the same
        // with reflection, but this method is called too frequently,
        // and the performance hit would likely be significant.

        // If the card is expanded, only update the immutable UI
        // elements, so any changes to the form fields aren't lost.
        if (IsExpanded)
        {
            Device.CurrentUser = message.Device.CurrentUser;
            Device.Platform = message.Device.Platform;
            Device.TotalStorage = message.Device.TotalStorage;
            Device.UsedStorage = message.Device.UsedStorage;
            Device.Drives = message.Device.Drives;
            Device.CpuUtilization = message.Device.CpuUtilization;
            Device.TotalMemory = message.Device.TotalMemory;
            Device.UsedMemory = message.Device.UsedMemory;
            Device.AgentVersion = message.Device.AgentVersion;
            Device.LastOnline = message.Device.LastOnline;
            Device.PublicIP = message.Device.PublicIP;
            Device.MacAddresses = message.Device.MacAddresses;
            Device.OSDescription = message.Device.OSDescription;
            Device.CurrentUserGivenName = message.Device.CurrentUserGivenName;
            Device.CurrentUserSurname = message.Device.CurrentUserSurname;
            Device.DeviceType = message.Device.DeviceType;
            Device.Manufacturer = message.Device.Manufacturer;
            Device.Model = message.Device.Model;
            Device.SerialNumber = message.Device.SerialNumber;
            Device.ProcessorName = message.Device.ProcessorName;
            Device.InventoryUpdatedAt = message.Device.InventoryUpdatedAt;
        }
        else
        {
            Device = message.Device;
        }
        await InvokeAsync(StateHasChanged);
    }

    private void ContextMenuOpening(MouseEventArgs args)
    {
        if (_state == DeviceCardState.Normal)
        {
            JsInterop.OpenWindow($"/device-details/{Device.ID}", "_blank");
        }
    }

    private async Task ExpandCard(MouseEventArgs args)
    {
        if (_state == DeviceCardState.Expanded)
        {
            return;
        }

        await Messenger.Send(
            new DeviceCardStateChangedMessage(Device.ID, DeviceCardState.Expanded),
            CircuitConnection.ConnectionId);

        JsInterop.ScrollToElement(_card);

        await CircuitConnection.TriggerHeartbeat(Device.ID);
    }


    private string GetCardStateClass()
    {
        return $"{_state}".ToLower();
    }

    private static string FormatPercent(double fraction)
    {
        if (double.IsNaN(fraction) || double.IsInfinity(fraction))
        {
            return "—";
        }
        return $"{Math.Round(Math.Clamp(fraction, 0, 1) * 100)}%";
    }

    private static string GetMeterClass(double fraction)
    {
        return fraction switch
        {
            >= 0.9 => "crit",
            >= 0.75 => "warn",
            _ => string.Empty
        };
    }

    private static string GetMeterWidth(double fraction)
    {
        if (double.IsNaN(fraction) || double.IsInfinity(fraction))
        {
            return "0%";
        }
        var percent = Math.Round(Math.Clamp(fraction, 0, 1) * 100, 1);
        return $"{percent.ToString(System.Globalization.CultureInfo.InvariantCulture)}%";
    }

    private string GetLastSeen()
    {
        if (Device.LastOnline == default)
        {
            return "nunca conectou";
        }

        var elapsed = DateTimeOffset.Now - Device.LastOnline;
        return elapsed.TotalMinutes switch
        {
            < 1 => "visto agora",
            < 60 => $"visto há {(int)elapsed.TotalMinutes} min",
            < 60 * 24 => $"visto há {(int)elapsed.TotalHours} h",
            < 60 * 24 * 30 => $"visto há {(int)elapsed.TotalDays} d",
            _ => $"visto em {Device.LastOnline.ToLocalTime():dd/MM/yyyy}"
        };
    }

    private string GetOsClass()
    {
        var platform = $"{Device.Platform} {Device.OSDescription}".ToLowerInvariant();
        if (platform.Contains("win"))
        {
            return "windows";
        }
        if (platform.Contains("osx") || platform.Contains("mac") || platform.Contains("darwin"))
        {
            return "apple";
        }
        if (platform.Contains("linux") || platform.Contains("ubuntu") || platform.Contains("debian"))
        {
            return "linux";
        }
        return string.Empty;
    }

    private string GetOsIcon()
    {
        return GetOsClass() switch
        {
            "windows" => "fa-brands fa-windows",
            "apple" => "fa-brands fa-apple",
            "linux" => "fa-brands fa-linux",
            _ => "fa-solid fa-computer"
        };
    }

    private string GetSubtitle()
    {
        if (!string.IsNullOrWhiteSpace(Device.Alias))
        {
            return Device.Alias;
        }
        return DeviceDisplay.GetOperatingSystem(Device).Name;
    }

    private void HandleHeaderClick()
    {
        if (IsExpanded)
        {
            _state = DeviceCardState.Normal;
        }
    }
    private async Task HandleValidSubmit()
    {
        EnsureUserSet();
        if (!DataService.DoesUserHaveAccessToDevice(Device.ID, User))
        {
            ToastService.ShowToast("Não autorizado.", classString: "bg-warning");
            return;
        }

        if (!Permissions.ManageDevices)
        {
            ToastService.ShowToast("Seu perfil não permite editar dispositivos.", classString: "bg-warning");
            return;
        }
       
        await DataService.UpdateDevice(Device.ID,
              Device.Tags,
              Device.Alias,
              Device.DeviceGroupID,
              Device.Notes);

        await DataService.UpdateDeviceAsset(Device.ID, Device.AssetTag, Device.Location);

        ToastService.ShowToast("Configurações do dispositivo salvas.");

        await CircuitConnection.TriggerHeartbeat(Device.ID);
    }

    /// <summary>Se a janela de envio já foi fechada, avisa o resultado com um toast.</summary>
    private Task HandleFileTransferResult(object subscriber, FileTransferResultMessage message)
    {
        if (message.DeviceId != Device.ID || _sendFileOpen)
        {
            return Task.CompletedTask;
        }

        var lines = message.Detail.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (message.Success)
        {
            ToastService.ShowToast2($"{Device.DeviceName}: arquivo salvo em {lines.LastOrDefault()}", ToastType.Success);
        }
        else
        {
            ToastService.ShowToast2($"{Device.DeviceName}: {string.Join(" ", lines)}", ToastType.Error);
        }
        return Task.CompletedTask;
    }

    private void OpenDeviceDetails()
    {
        JsInterop.OpenWindow($"/device-details/{Device.ID}", "_blank");
    }

    private void StartChat()
    {
        var session = ChatCache.GetOrAdd(Device.ID, key =>
        {
            return new ChatSession()
            {
                DeviceId = key,
                DeviceName = Device.DeviceName,
                IsExpanded = true
            };
        });

        session.IsExpanded = true;
        Messenger.Send(new ChatSessionsChangedMessage(), CircuitConnection.ConnectionId);
    }

    private async Task StartRemoteControl(bool viewOnly)
    {
        if (!ServiceSessionCache.TryGetConnectionId(Device.ID, out _))
        {
            ToastService.ShowToast("Conexão do dispositivo não encontrada", classString: "bg-danger");
            return;
        }

        var result = await CircuitConnection.RemoteControl(Device.ID, viewOnly);
        if (!result.IsSuccess)
        {
            return;
        }

        var session = result.Value;

        if (!await session.WaitForSessionReady(TimeSpan.FromSeconds(20)))
        {
            ToastService.ShowToast("Não foi possível iniciar a sessão", classString: "bg-danger");
            return;
        }

        JsInterop.OpenWindow(
            $"/Viewer" +
                $"?mode=Unattended&sessionId={session.UnattendedSessionId}" +
                $"&accessKey={session.AccessKey}" +
                $"&viewonly={viewOnly}", 
            "_blank");
    }

    private void ToggleIsSelected(ChangeEventArgs args)
    {
        if (args.Value is not bool isSelected)
        {
            return;
        }

        if (isSelected)
        {
            SelectedCards.Add(Device.ID);
        }
        else
        {
            SelectedCards.Remove(Device.ID);
        }

        SelectedCards.InvokeSelectionsChanged();
    }

    private async Task UninstallAgent()
    {
        var result = await JsInterop.Confirm(new ConfirmOptions(
            "Desinstalar agente",
            $"O agente será removido de {Device.DeviceName} e o computador sai da lista. Para voltar, é preciso instalar de novo.",
            "Desinstalar", Danger: true, Icon: "fa-solid fa-trash-can"));
        if (result)
        {
            await CircuitConnection.UninstallAgents(new[] { Device.ID });
            _state = DeviceCardState.Normal;
            await ParentFrame.Refresh();
        }
    }

    private Task PowerAction(bool restart) =>
        PowerActionPrompt.Run(JsInterop, CircuitConnection, ToastService, [Device], restart);

    private async Task WakeDevice()
    {
        var result = await CircuitConnection.WakeDevice(Device);
        if (result.IsSuccess)
        {
            ToastService.ShowToast2(
                $"Comando Wake-on-LAN enviado aos dispositivos da rede.", 
                ToastType.Success);
        }
        else
        {
            ToastService.ShowToast2(
                $"Falha no Wake-on-LAN. Motivo: {result.Reason}", 
                ToastType.Error);
        }
    }
}