using Remotely.Server.Services;
using Remotely.Shared.Helpers;
using Bitbound.SimpleMessenger;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.SignalR;
using Remotely.Server.Models;
using Remotely.Server.Enums;
using Remotely.Server.Models.Messages;
using Remotely.Shared.Models;
using Remotely.Server.Services.Stores;
using Remotely.Shared;
using Remotely.Shared.Entities;
using Remotely.Shared.Enums;
using Remotely.Shared.Interfaces;
using Remotely.Shared.Utilities;
using System.Collections.Concurrent;

namespace Remotely.Server.Hubs;

public interface ICircuitConnection
{
    string ConnectionId { get; }

    RemotelyUser User { get; }

    Task DeleteRemoteLogs(string deviceId);

    Task ExecuteCommandOnAgent(ScriptingShell shell, string command, string[] deviceIDs);

    Task GetPowerShellCompletions(string inputText, int currentIndex, CompletionIntent intent, bool? forward);

    Task GetRemoteLogs(string deviceId);

    Task ReinstallAgents(string[] deviceIDs);

    Task<Result<RemoteControlSession>> RemoteControl(string deviceID, bool viewOnly);

    Task RemoveDevices(string[] deviceIDs);

    Task RunScript(IEnumerable<string> deviceIds, Guid savedScriptId, int scriptRunId, ScriptInputType scriptInputType, bool runAsHostedService);

    Task SendChat(string message, string deviceId, bool isDisconnecting = false);
    Task<bool> TransferFileFromBrowserToAgent(string deviceId, string transferId, string[] fileIds);

    /// <summary>
    /// Manda o agente baixar arquivos já enviados ao servidor e salvar no destino escolhido.
    /// O agente responde com <see cref="FileTransferResultMessage"/>.
    /// </summary>
    Task<Result> SendFilesToDevice(string deviceId, string transferId, string[] fileIds, string destination);

    Task TriggerHeartbeat(string deviceId);

    /// <summary>Pede ao agente uma nova coleta de inventário.</summary>
    Task<bool> RequestInventory(string deviceId);

    /// <summary>Inicia os testes da aba Diagnóstico. Retorna o ID da execução.</summary>
    Task<Result<string>> RunDiagnostics(string deviceId, IReadOnlyCollection<string> tests);

    /// <summary>Executa um reparo da lista fechada <c>DiagnosticRepairs</c> no dispositivo.</summary>
    Task<Result<string>> RunRepair(string deviceId, string repairId, string? target);

    /// <summary>Cancela o diagnóstico ou reparo em andamento no dispositivo.</summary>
    Task<Result> CancelDiagnostics(string deviceId);

    /// <summary>Reinicia ou desliga os dispositivos online (ação "restart" ou "shutdown"). Devolve quantos receberam o comando.</summary>
    Task<Result<int>> PowerAction(string[] deviceIds, string action, int delaySeconds);

    Task UninstallAgents(string[] deviceIDs);
    Task UpdateTags(string deviceID, string tags);

    /// <summary>
    /// Sends a Wake-On-LAN request for the specified device to its peer devices.
    /// Peer devices are those in the same group or the same public IP.
    /// </summary>
    /// <param name="device"></param>
    Task<Result> WakeDevice(Device device);

    /// <summary>
    /// Sends a Wake-On-LAN request for the specified device to its peer devices.
    /// Peer devices are those in the same group or the same public IP.
    /// </summary>
    /// <param name="devices"></param>
    Task<Result> WakeDevices(Device[] devices);
}

public class CircuitConnection : CircuitHandler, ICircuitConnection
{
    private readonly IHubContext<AgentHub, IAgentHubClient> _agentHubContext;
    private readonly IDataService _dataService;
    private readonly ISelectedCardsStore _cardStore;
    private readonly IAuthService _authService;
    private readonly ICircuitManager _circuitManager;
    private readonly IRemoteControlSessionCache _remoteControlSessionCache;
    private readonly IExpiringTokenService _expiringTokenService;
    private readonly ILogger<CircuitConnection> _logger;
    private readonly IAgentHubSessionCache _agentSessionCache;
    private readonly IMessenger _messenger;
    private readonly IToastService _toastService;
    private readonly IAccessProfileService _accessProfiles;
    private RemotelyUser? _user;

    public CircuitConnection(
        IAuthService authService,
        IDataService dataService,
        ISelectedCardsStore cardStore,
        IHubContext<AgentHub, IAgentHubClient> agentHubContext,
        ICircuitManager circuitManager,
        IToastService toastService,
        IExpiringTokenService expiringTokenService,
        IRemoteControlSessionCache remoteControlSessionCache,
        IAgentHubSessionCache agentSessionCache,
        IMessenger messenger,
        IAccessProfileService accessProfiles,
        ILogger<CircuitConnection> logger)
    {
        _accessProfiles = accessProfiles;
        _dataService = dataService;
        _agentHubContext = agentHubContext;
        _cardStore = cardStore;
        _authService = authService;
        _circuitManager = circuitManager;
        _toastService = toastService;
        _expiringTokenService = expiringTokenService;
        _remoteControlSessionCache = remoteControlSessionCache;
        _agentSessionCache = agentSessionCache;
        _messenger = messenger;
        _logger = logger;
    }


    public string ConnectionId { get; } = Guid.NewGuid().ToString();

    public RemotelyUser User
    {
        get => _user ?? throw new InvalidOperationException("User is not set.");
        internal set => _user = value;
    }


    private bool HasPermission(AccessPermission permission)
    {
        var permissions = _accessProfiles.GetPermissions(_user);
        var (allowed, label) = permission switch
        {
            AccessPermission.RemoteControl => (permissions.RemoteControl, "acesso remoto e chat"),
            AccessPermission.Scripts => (permissions.Scripts, "executar scripts e comandos"),
            AccessPermission.ManageDevices => (permissions.ManageDevices, "gerenciar dispositivos"),
            AccessPermission.Repairs => (permissions.Repairs, "executar reparos"),
            _ => (false, "esta ação")
        };

        if (!allowed)
        {
            _logger.LogWarning(
                "Action blocked by access profile. User: {userName}. Profile: {profile}. Permission: {permission}.",
                _user?.UserName,
                permissions.ProfileName,
                permission);

            _toastService.ShowToast2(
                $"Seu perfil{(string.IsNullOrWhiteSpace(permissions.ProfileName) ? "" : $" ({permissions.ProfileName})")} não permite {label}.",
                ToastType.Warning);
        }

        return allowed;
    }

    public Task DeleteRemoteLogs(string deviceId)
    {
        if (!HasPermission(AccessPermission.ManageDevices))
        {
            return Task.CompletedTask;
        }

        var (canAccess, key) = CanAccessDevice(deviceId);
        if (!canAccess)
        {
            _toastService.ShowToast("Acesso negado.", classString: "bg-warning");
            return Task.CompletedTask;
        }

        _logger.LogInformation("Delete logs command sent.  Device: {deviceId}.  User: {username}",
            deviceId,
            User?.UserName);

        return _agentHubContext.Clients.Client(key).DeleteLogs();
    }

    public async Task ExecuteCommandOnAgent(ScriptingShell shell, string command, string[] deviceIDs)
    {
        if (!HasPermission(AccessPermission.Scripts))
        {
            return;
        }

        deviceIDs = _dataService.FilterDeviceIdsByUserPermission(deviceIDs, User);
        var connections = GetActiveConnectionsForUserOrg(deviceIDs);

        _logger.LogInformation("Command executed by {username}.  Shell: {shell}.  Command: {command}.  Devices: {deviceIds}",
              User.UserName,
              shell,
              command,
              string.Join(", ", deviceIDs));

        var authTokenForUploadingResults = _expiringTokenService.GetToken(Time.Now.AddMinutes(5));

        await _agentHubContext.Clients.Clients(connections).ExecuteCommand(
            shell,
            command,
            authTokenForUploadingResults,
            $"{User.UserName}",
            ConnectionId);
    }

    public Task GetPowerShellCompletions(string inputText, int currentIndex, CompletionIntent intent, bool? forward)
    {
        if (!HasPermission(AccessPermission.Scripts))
        {
            return Task.CompletedTask;
        }

        var device = _cardStore.SelectedDevices.FirstOrDefault();
        if (device is null)
        {
            return Task.CompletedTask;
        }

        var (canAccess, key) = CanAccessDevice(device);
        if (!canAccess)
        {
            return Task.CompletedTask;
        }

        return _agentHubContext.Clients.Client(key).GetPowerShellCompletions(
            inputText,
            currentIndex,
            intent,
            forward,
            ConnectionId);
    }

    public Task GetRemoteLogs(string deviceId)
    {
        var (canAccess, key) = CanAccessDevice(deviceId);
        if (!canAccess)
        {
            _toastService.ShowToast("Acesso negado.", classString: "bg-warning");
            return Task.CompletedTask;
        }

        return _agentHubContext.Clients.Client(key).GetLogs(ConnectionId);
    }

    public override async Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(ConnectionId))
        {
            _circuitManager.TryRemoveConnection(ConnectionId, out _);
        }
        await base.OnCircuitClosedAsync(circuit, cancellationToken);
    }

    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        if (await _authService.IsAuthenticated())
        {
            var userResult = await _authService.GetUser();
            if (!userResult.IsSuccess)
            {
                _toastService.ShowToast2("Falha de autorização.", Enums.ToastType.Error);
                return;
            }
            User = userResult.Value;
            _circuitManager.TryAddConnection(ConnectionId, this);
        }
        await base.OnCircuitOpenedAsync(circuit, cancellationToken);
    }

    public async Task ReinstallAgents(string[] deviceIDs)
    {
        if (!HasPermission(AccessPermission.ManageDevices))
        {
            return;
        }

        deviceIDs = _dataService.FilterDeviceIdsByUserPermission(deviceIDs, User);
        var connections = GetActiveConnectionsForUserOrg(deviceIDs);
        await _agentHubContext.Clients.Clients(connections).ReinstallAgent();
        _dataService.RemoveDevices(deviceIDs);
    }

    public async Task<Result<RemoteControlSession>> RemoteControl(string deviceId, bool viewOnly)
    {
        if (!HasPermission(AccessPermission.RemoteControl))
        {
            return Result.Fail<RemoteControlSession>("Seu perfil não permite acesso remoto.");
        }

        var settings = await _dataService.GetSettings();

        if (!_agentSessionCache.TryGetByDeviceId(deviceId, out var targetDevice))
        {
            var message = new DisplayNotificationMessage(
                 "The selected device is not online.",
                 "O dispositivo não está online.",
                 "bg-warning");

            await _messenger.Send(message, ConnectionId);

            return Result.Fail<RemoteControlSession>("O dispositivo não está online.");
        }


        if (!_dataService.DoesUserHaveAccessToDevice(deviceId, User))
        {
            var device = _dataService.GetDevice(targetDevice.ID);
            _logger.LogWarning(
                "Remote control attempted by unauthorized user.  Device ID: {deviceId}.  User Name: {userName}.",
                deviceId,
                User.UserName);
            return Result.Fail<RemoteControlSession>("Não autorizado.");

        }

        if (!_agentSessionCache.TryGetConnectionId(targetDevice.ID, out var serviceConnectionId))
        {
            var message = new DisplayNotificationMessage(
                "Conexão com o agente não encontrada.",
                "Conexão com o agente não encontrada.",
                "bg-warning");
            
            await _messenger.Send(message, ConnectionId);
            return Result.Fail<RemoteControlSession>("Conexão com o agente não encontrada.");
        }

        var sessionId = Guid.NewGuid();
        var accessKey = RandomGenerator.GenerateAccessKey();

        var session = new RemoteControlSession()
        {
            UnattendedSessionId = sessionId,
            UserConnectionId = ConnectionId,
            AgentConnectionId = serviceConnectionId,
            DeviceId = deviceId,
            ViewOnly = viewOnly,
            OrganizationId = User.OrganizationID,
            RequireConsent = settings.EnforceAttendedAccess,
            NotifyUserOnStart = settings.RemoteControlNotifyUser
        };

        _remoteControlSessionCache.AddOrUpdate($"{sessionId}", session);

        var orgResult = await _dataService.GetOrganizationNameByUserName($"{User.UserName}");

        if (!orgResult.IsSuccess)
        {
            _toastService.ShowToast2(orgResult.Reason, Enums.ToastType.Warning);
            return Result.Fail<RemoteControlSession>(orgResult.Reason);
        }

        await _agentHubContext.Clients.Client(serviceConnectionId).RemoteControl(
            sessionId,
            accessKey,
            ConnectionId,
            $"{User.UserOptions?.DisplayName}",
            orgResult.Value,
            User.OrganizationID);

        return Result.Ok(session);
    }

    public Task RemoveDevices(string[] deviceIDs)
    {
        if (!HasPermission(AccessPermission.ManageDevices))
        {
            return Task.CompletedTask;
        }

        if (User is not null)
        {
            var filterDevices = _dataService.FilterDeviceIdsByUserPermission(deviceIDs, User);
            _dataService.RemoveDevices(filterDevices);
        }

        return Task.CompletedTask;
    }

    public async Task RunScript(
        IEnumerable<string> deviceIds,
        Guid savedScriptId,
        int scriptRunId,
        ScriptInputType scriptInputType,
        bool runAsHostedService)
    {
        if (!HasPermission(AccessPermission.Scripts))
        {
            return;
        }

        var username = string.Empty;
        if (runAsHostedService)
        {
            username = "Remotely Server";
        }
        else if (User is not null)
        {
            username = User.UserName;
            deviceIds = _dataService.FilterDeviceIdsByUserPermission(deviceIds.ToArray(), User);
        }

        var authToken = _expiringTokenService.GetToken(Time.Now.AddMinutes(AppConstants.ScriptRunExpirationMinutes));

        var connectionIds = _agentSessionCache.GetConnectionIdsByDeviceIds(deviceIds).ToArray();

        if (connectionIds.Any())
        {
            await _agentHubContext.Clients.Clients(connectionIds).RunScript(
                savedScriptId,
                scriptRunId,
                $"{username}",
                scriptInputType,
                authToken);
        }

    }

    public async Task SendChat(string message, string deviceId, bool isDisconnecting = false)
    {
        if (!HasPermission(AccessPermission.RemoteControl))
        {
            return;
        }

        if (!_dataService.DoesUserHaveAccessToDevice(deviceId, User))
        {
            return;
        }

        if (!_agentSessionCache.TryGetByDeviceId(deviceId, out var device) ||
            !_agentSessionCache.TryGetConnectionId(deviceId, out var connectionId))
        {
            _toastService.ShowToast("Dispositivo não encontrado.");
            return;
        }

        if (device.OrganizationID != User.OrganizationID)
        {
            _toastService.ShowToast("Não autorizado.");
            return;
        }

        var orgResult = await _dataService.GetOrganizationNameByUserName($"{User.UserName}");
        if (!orgResult.IsSuccess)
        {
            _toastService.ShowToast2("Organização não encontrada.", Enums.ToastType.Warning);
            return;
        }

        await _agentHubContext.Clients.Client(connectionId).SendChatMessage(
            User.UserOptions?.DisplayName ?? $"{User.UserName}",
            message,
            orgResult.Value,
            User.OrganizationID,
            isDisconnecting,
            ConnectionId);
    }

    public async Task<Result> SendFilesToDevice(string deviceId, string transferId, string[] fileIds, string destination)
    {
        if (!HasPermission(AccessPermission.ManageDevices))
        {
            return Result.Fail("Você não tem permissão para enviar arquivos.");
        }

        if (fileIds.Length == 0 || fileIds.Length > 20)
        {
            return Result.Fail("Envie de 1 a 20 arquivos por vez.");
        }

        if (!_dataService.DoesUserHaveAccessToDevice(deviceId, User))
        {
            _logger.LogWarning("User {username} does not have access to device ID {deviceId} and attempted file upload.",
                User.UserName,
                deviceId);
            return Result.Fail("Dispositivo não encontrado.");
        }

        var deviceResult = await _dataService.GetDevice(deviceId);
        if (!deviceResult.IsSuccess)
        {
            return Result.Fail("Dispositivo não encontrado.");
        }

        var isWindows = deviceResult.Value.Platform?.Contains("Windows", StringComparison.OrdinalIgnoreCase) == true;
        if (!FileTransferDestinations.TryParse(destination, isWindows, out _, out _, out var error))
        {
            return Result.Fail(error);
        }

        if (!_agentSessionCache.TryGetConnectionId(deviceId, out var connectionId))
        {
            return Result.Fail("O dispositivo está offline.");
        }

        var authToken = _expiringTokenService.GetToken(Time.Now.AddMinutes(5));

        _logger.LogInformation(
            "User {username} sent {count} file(s) to device {deviceId} ({destination}).",
            User.UserName,
            fileIds.Length,
            deviceId,
            destination);

        await _agentHubContext.Clients
            .Client(connectionId)
            .ReceiveFiles(
                transferId,
                fileIds,
                ConnectionId,
                authToken,
                destination);

        return Result.Ok();
    }

    public async Task<bool> TransferFileFromBrowserToAgent(string deviceId, string transferId, string[] fileIds)
    {
        if (!HasPermission(AccessPermission.ManageDevices))
        {
            return false;
        }

        if (!_agentSessionCache.TryGetConnectionId(deviceId, out var connectionId))
        {
            return false;
        }

        if (!_dataService.DoesUserHaveAccessToDevice(deviceId, User))
        {
            _logger.LogWarning("User {username} does not have access to device ID {deviceId} and attempted file upload.",
                User.UserName,
                deviceId);

            return false;
        }

        var authToken = _expiringTokenService.GetToken(Time.Now.AddMinutes(5));

        await _agentHubContext.Clients
            .Client(connectionId)
            .TransferFileFromBrowserToAgent(
                transferId,
                fileIds,
                ConnectionId,
                authToken);

        return true;
    }

    public async Task TriggerHeartbeat(string deviceId)
    {
        var (canAccess, connectionId) = CanAccessDevice(deviceId);

        if (!canAccess)
        {
            return;
        }

        await _agentHubContext.Clients.Client(connectionId).TriggerHeartbeat();
    }

    public async Task<bool> RequestInventory(string deviceId)
    {
        var (canAccess, connectionId) = CanAccessDevice(deviceId);

        if (!canAccess)
        {
            return false;
        }

        await _agentHubContext.Clients.Client(connectionId).RequestInventory();
        return true;
    }

    public async Task<Result<string>> RunDiagnostics(string deviceId, IReadOnlyCollection<string> tests)
    {
        if (!HasPermission(AccessPermission.ManageDevices))
        {
            return Result.Fail<string>("Seu perfil não permite executar diagnósticos.");
        }

        var validTests = tests
            .Where(x => Remotely.Shared.Models.Diagnostics.DiagnosticTests.Get(x) is not null)
            .Distinct()
            .ToArray();
        if (validTests.Length == 0)
        {
            return Result.Fail<string>("Escolha ao menos um teste.");
        }

        var (canAccess, connectionId) = CanAccessDevice(deviceId);
        if (!canAccess)
        {
            return Result.Fail<string>("Dispositivo offline ou sem acesso.");
        }

        if (await _dataService.GetActiveDiagnosticRun(deviceId) is { } active)
        {
            return Result.Fail<string>(ActiveRunMessage(active));
        }

        var run = await _dataService.CreateDiagnosticRun(deviceId, User?.UserName, validTests);

        _logger.LogInformation(
            "Diagnostics requested. Device: {deviceId}. Tests: {tests}. User: {username}",
            deviceId,
            string.Join(',', validTests),
            User?.UserName);

        await _agentHubContext.Clients.Client(connectionId).RunDiagnostics(run.ID, validTests);
        return Result.Ok(run.ID);
    }

    public async Task<Result<string>> RunRepair(string deviceId, string repairId, string? target)
    {
        if (!HasPermission(AccessPermission.Repairs))
        {
            return Result.Fail<string>("Seu perfil não permite executar reparos.");
        }

        var repair = Remotely.Shared.Models.Diagnostics.DiagnosticRepairs.Get(repairId);
        if (repair is null)
        {
            return Result.Fail<string>("Reparo desconhecido.");
        }

        var normalizedTarget = Remotely.Shared.Models.Diagnostics.DiagnosticRepairs.NormalizeTarget(repair, target);
        if (normalizedTarget is null)
        {
            return Result.Fail<string>("Alvo do reparo inválido.");
        }

        var (canAccess, connectionId) = CanAccessDevice(deviceId);
        if (!canAccess || !_agentSessionCache.TryGetByDeviceId(deviceId, out var device))
        {
            return Result.Fail<string>("Dispositivo offline ou sem acesso.");
        }

        if (!Remotely.Shared.Models.Diagnostics.DiagnosticRepairs.IsAvailable(repair, device.Platform))
        {
            return Result.Fail<string>("Este reparo não está disponível para o sistema do dispositivo.");
        }

        if (await _dataService.GetActiveDiagnosticRun(deviceId) is { } active)
        {
            return Result.Fail<string>(ActiveRunMessage(active));
        }

        var run = await _dataService.CreateDiagnosticRun(
            deviceId,
            User?.UserName,
            [Remotely.Shared.Models.Diagnostics.DiagnosticRepairs.ToTestId(repair.Id)]);

        _logger.LogInformation(
            "Repair requested. Device: {deviceId} ({deviceName}). Repair: {repair}. Target: {target}. User: {username}",
            deviceId,
            device.DeviceName,
            repair.Id,
            normalizedTarget,
            User?.UserName);

        await _agentHubContext.Clients.Client(connectionId).RunRepair(run.ID, repair.Id, normalizedTarget);
        return Result.Ok(run.ID);
    }

    public async Task<Result<int>> PowerAction(string[] deviceIds, string action, int delaySeconds)
    {
        if (!HasPermission(AccessPermission.ManageDevices))
        {
            return Result.Fail<int>("Seu perfil não permite reiniciar ou desligar computadores.");
        }

        if (action is not ("restart" or "shutdown"))
        {
            return Result.Fail<int>("Ação inválida.");
        }

        delaySeconds = Math.Clamp(delaySeconds, 0, 3600);
        var allowed = _dataService.FilterDeviceIdsByUserPermission(deviceIds, User);
        var sent = new List<string>();
        foreach (var deviceId in allowed)
        {
            if (_agentSessionCache.TryGetConnectionId(deviceId, out var connectionId))
            {
                await _agentHubContext.Clients.Client(connectionId).PowerAction(action, delaySeconds);
                sent.Add(deviceId);
            }
        }

        _logger.LogInformation(
            "Power action {action} (delay {delay}s) sent to {count} device(s): {devices}. User: {username}",
            action,
            delaySeconds,
            sent.Count,
            string.Join(", ", sent),
            User?.UserName);

        return Result.Ok(sent.Count);
    }

    private static string ActiveRunMessage(DeviceDiagnosticRun active) =>
        (Remotely.Shared.Models.Diagnostics.DiagnosticRepairs.IsRepairRun(active.Tests) ? "Já há um reparo" : "Já há um diagnóstico") +
        $" em andamento neste computador (desde {active.StartedAt.ToLocalTime():HH:mm}). Aguarde terminar ou cancele.";

    public async Task<Result> CancelDiagnostics(string deviceId)
    {
        if (!HasPermission(AccessPermission.ManageDevices) && !HasPermission(AccessPermission.Repairs))
        {
            return Result.Fail("Seu perfil não permite cancelar diagnósticos.");
        }

        if (!_dataService.DoesUserHaveAccessToDevice(deviceId, User))
        {
            return Result.Fail("Sem acesso a este dispositivo.");
        }

        var canceled = await _dataService.CancelDiagnosticRuns(deviceId, User?.UserName);

        // Se o agente estiver online, interrompe o que estiver rodando (e desfaz o teste de memória agendado).
        if (_agentSessionCache.TryGetConnectionId(deviceId, out var connectionId))
        {
            await _agentHubContext.Clients.Client(connectionId).CancelDiagnostics();
        }

        _logger.LogInformation(
            "Diagnostics canceled. Device: {deviceId}. Runs: {count}. User: {username}",
            deviceId,
            canceled,
            User?.UserName);

        await _messenger.Send(new DiagnosticsUpdatedMessage(deviceId, string.Empty), ConnectionId);
        return Result.Ok();
    }

    public async Task UninstallAgents(string[] deviceIDs)
    {
        if (!HasPermission(AccessPermission.ManageDevices))
        {
            return;
        }

        deviceIDs = _dataService.FilterDeviceIdsByUserPermission(deviceIDs, User);
        var connections = GetActiveConnectionsForUserOrg(deviceIDs);
        await _agentHubContext.Clients.Clients(connections).UninstallAgent();
        _dataService.RemoveDevices(deviceIDs);
    }

    public async Task UpdateTags(string deviceID, string tags)
    {
        if (!HasPermission(AccessPermission.ManageDevices))
        {
            return;
        }

        if (_dataService.DoesUserHaveAccessToDevice(deviceID, User))
        {
            if (tags.Length > 200)
            {
                var message = new DisplayNotificationMessage(
                     $"Tag must be 200 characters or less. Supplied length is {tags.Length}.",
                    "Tag must be under 200 characters.",
                    "bg-warning");

                await _messenger.Send(message, ConnectionId);
                return;
            }

            await _dataService.UpdateTags(deviceID, tags);

            var successMessage = new DisplayNotificationMessage(
                "Device updated successfully.",
                "Device updated.",
                "bg-success");

            await _messenger.Send(successMessage, ConnectionId);
        }
    }

    public async Task<Result> WakeDevice(Device device)
    {
        if (!HasPermission(AccessPermission.ManageDevices))
        {
            return Result.Fail("Seu perfil não permite gerenciar dispositivos.");
        }

        try
        {
            if (!_dataService.DoesUserHaveAccessToDevice(device.ID, User.Id))
            {
                return Result.Fail("Não autorizado.");
            }

            var availableDevices = _agentSessionCache
                .GetAllDevices()
                .Where(x =>
                     x.OrganizationID == User.OrganizationID &&
                    (x.DeviceGroupID == device.DeviceGroupID || x.PublicIP == device.PublicIP))
                .ToArray();

            await SendWakeCommand(device, availableDevices);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error waking device {deviceId}.", device.ID);
            return Result.Fail(ex);
        }
    }

    public async Task<Result> WakeDevices(Device[] devices)
    {
        if (!HasPermission(AccessPermission.ManageDevices))
        {
            return Result.Fail("Seu perfil não permite gerenciar dispositivos.");
        }

        try
        {
            var deviceIds = devices.Select(x => x.ID).ToArray();
            var filteredIds = _dataService.FilterDeviceIdsByUserPermission(deviceIds, User);
            var filteredDevices = devices.Where(x => filteredIds.Contains(x.ID)).ToArray();

            var availableDevices = _agentSessionCache
                .GetAllDevices()
                .Where(x => x.OrganizationID == User.OrganizationID);

            var devicesByGroupId = new ConcurrentDictionary<string, List<Device>>();
            var devicesByPublicIp = new ConcurrentDictionary<string, List<Device>>();

            foreach (var device in availableDevices)
            {
                if (!string.IsNullOrWhiteSpace(device.DeviceGroupID))
                {
                    var group = devicesByGroupId.GetOrAdd(device.DeviceGroupID, key => new());
                    group.Add(device);
                    // We only need the device in one group.
                    break;
                }

                if (!string.IsNullOrWhiteSpace(device.PublicIP))
                {
                    var group = devicesByPublicIp.GetOrAdd(device.PublicIP, key => new());
                    group.Add(device);
                }
            }

            foreach (var deviceToWake in filteredDevices)
            {
                if (!string.IsNullOrWhiteSpace(deviceToWake.DeviceGroupID) &&
                    devicesByGroupId.TryGetValue(deviceToWake.DeviceGroupID, out var groupList))
                {
                    await SendWakeCommand(deviceToWake, groupList);
                }

                if (!string.IsNullOrWhiteSpace(deviceToWake.PublicIP) &&
                    devicesByPublicIp.TryGetValue(deviceToWake.PublicIP, out var ipList))
                {
                    await SendWakeCommand(deviceToWake, ipList);
                }

            }
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while waking devices.");
            return Result.Fail(ex);
        }
    }

    private (bool canAccess, string connectionId) CanAccessDevice(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return (false, string.Empty);
        }

        if (!_agentSessionCache.TryGetByDeviceId(deviceId, out var device) ||
            !_dataService.DoesUserHaveAccessToDevice(device.ID, User) ||
            !_agentSessionCache.TryGetConnectionId(device.ID, out var connectionId))
        {
            return (false, string.Empty);
        }


        return (true, connectionId);
    }

    private IEnumerable<string> GetActiveConnectionsForUserOrg(IEnumerable<string> deviceIds)
    {

        foreach (var deviceId in deviceIds)
        {
            if (!_agentSessionCache.TryGetByDeviceId(deviceId, out var device))
            {
                continue;
            }

            if (device.OrganizationID != User.OrganizationID)
            {
                continue;
            }

            if (_agentSessionCache.TryGetConnectionId(device.ID, out var connectionId))
            {
                yield return connectionId;
            }
        }
    }


    private async Task SendWakeCommand(Device deviceToWake, IEnumerable<Device> peerDevices)
    {
        foreach (var peerDevice in peerDevices)
        {
            foreach (var mac in deviceToWake.MacAddresses ?? Array.Empty<string>())
            {
                if (_agentSessionCache.TryGetConnectionId(peerDevice.ID, out var connectionId))
                {
                    _logger.LogInformation(
                        "Sending wake command for device {deviceName} ({deviceId}) to " +
                        "peer device {peerDeviceName} ({peerDeviceId}).  " +
                        "Sender: {username}.",
                        deviceToWake.DeviceName,
                        deviceToWake.ID,
                        peerDevice.DeviceName,
                        peerDevice.ID,
                        User.UserName);
                    await _agentHubContext.Clients.Client(connectionId).WakeDevice(mac);
                }
            }
        }
    }
}
