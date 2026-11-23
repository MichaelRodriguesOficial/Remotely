using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.SignalR;
using Remotely.Server.Hubs;
using Remotely.Server.Models;
using Remotely.Server.Services;
using Remotely.Shared.Entities;
using Remotely.Shared.Interfaces;
using System.Net;

namespace Remotely.Server.Components.Pages;

public partial class ServerConfig : AuthComponentBase
{
    private readonly List<RemotelyUser> _userList = new();
    private string? _alertMessage;
    private string? _bannedDeviceSelected;
    private string? _bannedDeviceToAdd;

    private string? _knownProxySelected;
    private string? _knownProxyToAdd;

    private bool _showAdminsOnly;
    private bool _showMyOrgAdminsOnly = true;
    private string? _trustedCorsOriginSelected;
    private string? _trustedCorsOriginToAdd;

    [Inject]
    public required IHubContext<AgentHub, IAgentHubClient> AgentHubContext { get; init; }

    [Inject]
    public required ICircuitManager CircuitManager { get; init; }

    [Inject]
    public required IDataService DataService { get; init; }

    [Inject]
    public required IEmailSenderEx EmailSender { get; init; }

    [Inject]
    public required IWebHostEnvironment HostEnv { get; init; }

    [Inject]
    public required ILogger<ServerConfig> Logger { get; init; }

    [Inject]
    public required IModalService ModalService { get; init; }

    [Inject]
    public required IAgentHubSessionCache ServiceSessionCache { get; init; }

    [Inject]
    public required IToastService ToastService { get; init; }

    [Inject]
    public required IUpgradeService UpgradeService { get; init; }


    private SettingsModel Input { get; set; } = new();
    private IEnumerable<string> OutdatedDevices => GetOutdatedDevices();
    private int TotalDevices => DataService.GetTotalDevices();

    private IEnumerable<RemotelyUser> UserList
    {
        get
        {
            if (User is null)
            {
                return [];
            }

            EnsureUserSet();

            return _userList.Where(x =>
                (!_showAdminsOnly || x.IsServerAdmin) &&
                (!_showMyOrgAdminsOnly || x.OrganizationID == User.OrganizationID));
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        EnsureUserSet();
        if (!User.IsServerAdmin)
        {
            return;
        }

        Input = await DataService.GetSettings();

        _userList.AddRange(DataService.GetAllUsersForServer().OrderBy(x => x.UserName));
    }

    private void AddBannedDevice()
    {
        if (string.IsNullOrWhiteSpace(_bannedDeviceToAdd))
        {
            return;
        }

        Input.BannedDevices.Add(_bannedDeviceToAdd);
        _bannedDeviceToAdd = string.Empty;
    }

    private void AddKnownProxy()
    {
        if (IPAddress.TryParse(_knownProxyToAdd, out _))
        {
            Input.KnownProxies.Add(_knownProxyToAdd);
        }
        else
        {
            ToastService.ShowToast2("Endereço IP inválido.", Enums.ToastType.Warning);
        }

        _knownProxyToAdd = string.Empty;
    }

    private void AddTrustedCorsOrigin()
    {
        if (string.IsNullOrWhiteSpace(_trustedCorsOriginToAdd))
        {
            return;
        }

        Input.TrustedCorsOrigins.Add(_trustedCorsOriginToAdd);
        _trustedCorsOriginToAdd = null;
    }

    private IEnumerable<string> GetOutdatedDevices()
    {
        try
        {
            var currentVersion = UpgradeService.GetCurrentVersion();

            return ServiceSessionCache.GetAllDevices()
                .Where(x => Version.TryParse(x.AgentVersion, out var result) && result < currentVersion)
                .Select(x => x.ID);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error while getting outdated devices.");
        }

        return Enumerable.Empty<string>();
    }

    private void HandleBannedDeviceKeyDown(KeyboardEventArgs args)
    {
        if (args.Key.Equals("Enter", StringComparison.OrdinalIgnoreCase))
        {
            AddBannedDevice();
        }
    }

    private void HandleKnownProxyKeyDown(KeyboardEventArgs args)
    {
        if (args.Key.Equals("Enter", StringComparison.OrdinalIgnoreCase))
        {
            AddKnownProxy();
        }
    }

    private void HandleTrustedCorsKeyDown(KeyboardEventArgs args)
    {
        if (args.Key.Equals("Enter", StringComparison.OrdinalIgnoreCase))
        {
            AddTrustedCorsOrigin();
        }
    }
    private async Task HandleValidSubmit(EditContext context)
    {
        await Save();
    }

    private void RemoveBannedDeviceItem(string item)
    {
        _bannedDeviceSelected = item;
        RemoveBannedDevice();
    }

    private void RemoveKnownProxyItem(string item)
    {
        _knownProxySelected = item;
        RemoveKnownProxy();
    }

    private void RemoveTrustedCorsOriginItem(string item)
    {
        _trustedCorsOriginSelected = item;
        RemoveTrustedCorsOrigin();
    }

    private void RemoveBannedDevice()
    {
        if (string.IsNullOrWhiteSpace(_bannedDeviceSelected))
        {
            return;
        }

        Input.BannedDevices.Remove(_bannedDeviceSelected);
        _bannedDeviceSelected = null;
    }

    private void RemoveKnownProxy()
    {
        if (string.IsNullOrWhiteSpace(_knownProxySelected))
        {
            return;
        }
        Input.KnownProxies.Remove(_knownProxySelected);
        _knownProxySelected = null;
    }

    private void RemoveTrustedCorsOrigin()
    {
        if (string.IsNullOrWhiteSpace(_trustedCorsOriginSelected))
        {
            return;
        }

        Input.TrustedCorsOrigins.Remove(_trustedCorsOriginSelected);
        _trustedCorsOriginSelected = null;
    }

    private async Task Save()
    {
        Input.ScheduledDiagnosticsIntervalDays = Math.Clamp(Input.ScheduledDiagnosticsIntervalDays, 1, 30);
        await DataService.SaveSettings(Input);

        ToastService.ShowToast("Configuração salva.");
    }

    private async Task SaveAndTestSmtpSettings()
    {
        EnsureUserSet();

        await DataService.SaveSettings(Input);

        if (string.IsNullOrWhiteSpace(User.Email))
        {
            ToastService.ShowToast2("O usuário não tem e-mail definido.", Enums.ToastType.Warning);
            return;
        }

        var success = await EmailSender.SendEmailAsync(User.Email, "Remotely Test Email", "Congratulations! Your SMTP settings are working!", User.OrganizationID);
        if (success)
        {
            ToastService.ShowToast($"E-mail de teste enviado para {User.Email}. Verifique a caixa de entrada (ou o spam).");
            _alertMessage = $"E-mail de teste enviado para {User.Email}. Verifique a caixa de entrada (ou o spam).";
        }
        else
        {
            ToastService.ShowToast("Erro ao enviar o e-mail. Veja os logs do servidor para detalhes.", classString: "bg-danger");
            _alertMessage = "Erro ao enviar o e-mail. Veja os logs do servidor para detalhes.";
        }
    }

    private void SetIsServerAdmin(ChangeEventArgs ev, RemotelyUser user)
    {
        if (ev.Value is not bool isAdmin)
        {
            return;
        }

        EnsureUserSet();

        DataService.SetIsServerAdmin(user.Id, isAdmin, User.Id);
        ToastService.ShowToast("Administradores do servidor atualizados.");
    }

    private void ShowOutdatedDevices()
    {
        if (OutdatedDevices.Any())
        {
            var outdatedDeviceNames = DataService
                .GetDevices(OutdatedDevices)
                .Select(x => $"{x.DeviceName}");

            var body = (new[] { "Outdated Devices:" })
                .Concat(outdatedDeviceNames)
                .ToArray();

            ModalService.ShowModal("Dispositivos desatualizados", body);
        }
        else
        {
            ModalService.ShowModal("Dispositivos desatualizados", new[] { "Não há dispositivos desatualizados online no momento." });
        }
    }

    private async Task UpdateAllDevices()
    {
        EnsureUserSet();

        if (!User.IsServerAdmin)
        {
            return;
        }

        if (!OutdatedDevices.Any())
        {
            ToastService.ShowToast("Nenhum agente precisa de atualização.");
            return;
        }

        var agentConnections = ServiceSessionCache.GetConnectionIdsByDeviceIds(OutdatedDevices);

        await AgentHubContext.Clients.Clients(agentConnections).ReinstallAgent();
        ToastService.ShowToast("Comando de atualização enviado.");
    }
}
