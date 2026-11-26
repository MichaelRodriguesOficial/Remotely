using Microsoft.AspNetCore.SignalR;
using Remotely.Server.Hubs;
using Remotely.Server.Models;
using Remotely.Shared.Entities;
using Remotely.Shared.Interfaces;
using Remotely.Shared.Helpers;
using Remotely.Shared.Utilities;

namespace Remotely.Server.Services;

/// <summary>
/// Abre uma sessão de controle remoto autônoma (acesso pelo painel): confere o acesso ao computador,
/// aplica o aceite do grupo e as permissões do perfil e pede ao agente para abrir o app.
/// Usado pelo botão do painel e pelo "Reconectar" do visualizador.
/// </summary>
public interface IRemoteControlLauncher
{
    Task<Result<RemoteControlSession>> Launch(RemotelyUser user, string deviceId, bool viewOnly, string requesterConnectionId);
}

public class RemoteControlLauncher : IRemoteControlLauncher
{
    private readonly IDataService _dataService;
    private readonly IAgentHubSessionCache _agentSessionCache;
    private readonly IRemoteControlSessionCache _sessionCache;
    private readonly IHubContext<AgentHub, IAgentHubClient> _agentHubContext;
    private readonly IAccessProfileService _accessProfiles;
    private readonly ILogger<RemoteControlLauncher> _logger;

    public RemoteControlLauncher(
        IDataService dataService,
        IAgentHubSessionCache agentSessionCache,
        IRemoteControlSessionCache sessionCache,
        IHubContext<AgentHub, IAgentHubClient> agentHubContext,
        IAccessProfileService accessProfiles,
        ILogger<RemoteControlLauncher> logger)
    {
        _dataService = dataService;
        _agentSessionCache = agentSessionCache;
        _sessionCache = sessionCache;
        _agentHubContext = agentHubContext;
        _accessProfiles = accessProfiles;
        _logger = logger;
    }

    public async Task<Result<RemoteControlSession>> Launch(RemotelyUser user, string deviceId, bool viewOnly, string requesterConnectionId)
    {
        if (!_accessProfiles.GetPermissions(user).RemoteControl)
        {
            return Result.Fail<RemoteControlSession>("Seu perfil não permite acesso remoto.");
        }

        if (!_agentSessionCache.TryGetByDeviceId(deviceId, out var targetDevice))
        {
            return Result.Fail<RemoteControlSession>("O dispositivo não está online.");
        }

        if (!_dataService.DoesUserHaveAccessToDevice(deviceId, user))
        {
            _logger.LogWarning(
                "Remote control attempted by unauthorized user.  Device ID: {deviceId}.  User Name: {userName}.",
                deviceId,
                user.UserName);
            return Result.Fail<RemoteControlSession>("Não autorizado.");
        }

        if (!_agentSessionCache.TryGetConnectionId(targetDevice.ID, out var serviceConnectionId))
        {
            return Result.Fail<RemoteControlSession>("Conexão com o agente não encontrada.");
        }

        var settings = await _dataService.GetSettings();
        var sessionId = Guid.NewGuid();
        var accessKey = RandomGenerator.GenerateAccessKey();

        // Aceite do usuário: o do grupo do computador ou o padrão do servidor.
        DeviceGroup? deviceGroup = null;
        if (!string.IsNullOrWhiteSpace(targetDevice.DeviceGroupID))
        {
            var groupResult = await _dataService.GetDeviceGroup(targetDevice.DeviceGroupID);
            deviceGroup = groupResult.IsSuccess ? groupResult.Value : null;
        }
        var consent = RemoteControlPolicy.Resolve(settings, deviceGroup);

        // O perfil de acesso define o que o técnico pode fazer; sem "controlar", a sessão é só de visualização.
        var permissions = _accessProfiles.GetRemoteControlPermissions(user);

        var session = new RemoteControlSession()
        {
            UnattendedSessionId = sessionId,
            UserConnectionId = requesterConnectionId,
            AgentConnectionId = serviceConnectionId,
            DeviceId = deviceId,
            DeviceGroupId = deviceGroup?.ID,
            ViewOnly = viewOnly || !permissions.Input,
            Permissions = permissions,
            OrganizationId = user.OrganizationID,
            RequireConsent = consent.RequireConsent,
            ConsentAutoAcceptSeconds = consent.AutoAcceptSeconds,
            NotifyUserOnStart = settings.RemoteControlNotifyUser
        };

        _sessionCache.AddOrUpdate($"{sessionId}", session);

        var orgResult = await _dataService.GetOrganizationNameByUserName($"{user.UserName}");
        if (!orgResult.IsSuccess)
        {
            return Result.Fail<RemoteControlSession>(orgResult.Reason);
        }

        await _agentHubContext.Clients.Client(serviceConnectionId).RemoteControl(
            sessionId,
            accessKey,
            requesterConnectionId,
            $"{user.UserOptions?.DisplayName}",
            orgResult.Value,
            user.OrganizationID);

        return Result.Ok(session);
    }
}
