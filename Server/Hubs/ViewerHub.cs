using Remotely.Server.Enums;
using Remotely.Server.Filters;
using Remotely.Server.Models;
using Remotely.Server.Services;
using Remotely.Shared.Interfaces;
using Remotely.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Remotely.Server.Hubs;

[ServiceFilter(typeof(ViewerAuthorizationFilter))]
public class ViewerHub : Hub<IViewerHubClient>
{
    private readonly IHubContext<DesktopHub, IDesktopHubClient> _desktopHub;
    private readonly IHubContext<AgentHub, IAgentHubClient> _agentHub;
    private readonly IDataService _dataService;
    private readonly ISessionRecordingSink _sessionRecordingSink;
    private readonly IRemoteControlSessionCache _desktopSessionCache;
    private readonly ILogger<ViewerHub> _logger;
    private readonly IDesktopStreamCache _streamCache;
    private readonly IRemoteControlAuditService _audit;

    public ViewerHub(
        IRemoteControlSessionCache desktopSessionCache,
        IDesktopStreamCache streamCache,
        IHubContext<AgentHub, IAgentHubClient> agentHub,
        IHubContext<DesktopHub, IDesktopHubClient> desktopHub,
        ISessionRecordingSink sessionRecordingSink,
        IDataService dataService,
        IRemoteControlAuditService audit,
        ILogger<ViewerHub> logger)
    {
        _audit = audit;
        _desktopSessionCache = desktopSessionCache;
        _streamCache = streamCache;
        _desktopHub = desktopHub;
        _agentHub = agentHub;
        _dataService = dataService;
        _sessionRecordingSink = sessionRecordingSink;
        _logger = logger;
    }

    private string RequesterDisplayName
    {
        get
        {
            if (Context.Items.TryGetValue(nameof(RequesterDisplayName), out var result) &&
                result is string requesterName)
            {
                return requesterName;
            }
            return string.Empty;
        }
        set
        {
            Context.Items[nameof(RequesterDisplayName)] = value;
        }
    }

    // Failed session ID/access key attempts are tracked per client IP, since an
    // attacker can simply open new hub connections to reset per-connection state.
    private const int MaxFailedSessionAttempts = 20;
    private static readonly TimeSpan _failedAttemptsWindow = TimeSpan.FromMinutes(10);
    private static readonly MemoryCache _failedAttemptsByIp = new(new MemoryCacheOptions());

    private string ClientIp => $"{Context.GetHttpContext()?.Connection?.RemoteIpAddress}";

    private int GetFailedSessionAttempts()
    {
        return _failedAttemptsByIp.TryGetValue(ClientIp, out int count) ? count : 0;
    }

    private void RegisterFailedSessionAttempt()
    {
        var count = GetFailedSessionAttempts() + 1;
        _failedAttemptsByIp.Set(ClientIp, count, _failedAttemptsWindow);
    }

    private RemoteControlSession SessionInfo
    {
        get
        {
            if (Context.Items.TryGetValue(nameof(SessionInfo), out var result) &&
                result is RemoteControlSession session)
            {
                return session;
            }

            var newSession = new RemoteControlSession();
            Context.Items[nameof(SessionInfo)] = newSession;
            return newSession;
        }
        set
        {
            Context.Items[nameof(SessionInfo)] = value;
        }
    }
    public async Task<Result> ChangeWindowsSession(int targetWindowsSession)
    {
        try
        {
            if (SessionInfo.Mode != RemoteControlMode.Unattended)
            {
                return Result.Fail("Disponível apenas no modo autônomo.");
            }

            SessionInfo.ViewerList.Remove(Context.ConnectionId);
            await _desktopHub.Clients
                .Client(SessionInfo.DesktopConnectionId)
                .ViewerDisconnected(Context.ConnectionId);

            SessionInfo = SessionInfo.CreateNew();
            _desktopSessionCache.AddOrUpdate($"{SessionInfo.UnattendedSessionId}", SessionInfo);

            await _agentHub.Clients
                .Client(SessionInfo.AgentConnectionId)
                .ChangeWindowsSession(
                    Context.ConnectionId,
                    $"{SessionInfo.UnattendedSessionId}",
                    SessionInfo.AccessKey,
                    SessionInfo.UserConnectionId,
                    SessionInfo.RequesterUserName,
                    SessionInfo.OrganizationName,
                    SessionInfo.OrganizationId,
                    targetWindowsSession);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while changing Windows session.");
            return Result.Fail("Erro ao trocar a sessão do Windows.");
        }
    }

    public async IAsyncEnumerable<byte[]> GetDesktopStream()
    {
        var sessionResult = await _streamCache.WaitForStreamSession(
            SessionInfo.StreamId,
            Context.ConnectionId,
            TimeSpan.FromSeconds(30));

        if (!sessionResult.IsSuccess)
        {
            _logger.LogError("Timed out while waiting for desktop stream.");
            await Clients.Caller.ShowMessage("Tempo esgotado");
            yield break;
        }

        var signaler = sessionResult.Value;

        if (signaler.Stream is null)
        {
            _logger.LogError("Stream was null.");
            yield break;
        }

        SessionInfo.StreamerState = StreamerState.Connected;

        try
        {
            await foreach (var chunk in signaler.Stream)
            {
                yield return chunk;
            }
        }
        finally
        {
            signaler.EndSignal.Release();
            _logger.LogInformation("Streaming session ended for {sessionId}.", SessionInfo.StreamId);
        }
    }

    public async Task<RemoteControlViewerOptions> GetViewerOptions()
    {
        var settings = await _dataService.GetSettings();
        return new RemoteControlViewerOptions()
        {
            ShouldRecordSession = settings.EnableRemoteControlRecording
        };
    }

    public async Task InvokeCtrlAltDel()
    {
        try
        {
            await _agentHub.Clients.Client(SessionInfo.AgentConnectionId).InvokeCtrlAltDel();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while invoking Ctrl+Alt+Del.");
        }
    }

    // Fechar a aba ou o navegador sem clicar no X: o viewer avisa no "pagehide" pelo hub
    // (ViewerClosing) e por navigator.sendBeacon com um token secreto (POST /api/viewer/closed),
    // para o computador do usuário encerrar a sessão na hora em vez de esperar o timeout da conexão.
    private const string CloseTokenKey = "ViewerCloseToken";
    private const string CloseNotifiedKey = "ViewerCloseNotified";
    private static readonly ConcurrentDictionary<string, HubCallerContext> _closeTokens = new();

    /// <summary>Token de uso único que permite encerrar esta conexão por sendBeacon ao fechar a página.</summary>
    public string GetCloseToken()
    {
        if (Context.Items.TryGetValue(CloseTokenKey, out var existing) && existing is string current)
        {
            return current;
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        Context.Items[CloseTokenKey] = token;
        _closeTokens[token] = Context;
        return token;
    }

    /// <summary>O viewer está saindo (aba fechada, navegação ou navegador encerrado).</summary>
    public Task ViewerClosing()
    {
        return EndViewerConnection(Context, _desktopHub, _audit);
    }

    /// <summary>Encerra a conexão do viewer dono do token. Usado pelo endpoint de sendBeacon.</summary>
    public static async Task<bool> CloseByToken(string token, IHubContext<DesktopHub, IDesktopHubClient> desktopHub, IRemoteControlAuditService audit)
    {
        if (string.IsNullOrWhiteSpace(token) || !_closeTokens.TryRemove(token, out var context))
        {
            return false;
        }

        await EndViewerConnection(context, desktopHub, audit);
        return true;
    }

    private static async Task EndViewerConnection(HubCallerContext context, IHubContext<DesktopHub, IDesktopHubClient> desktopHub, IRemoteControlAuditService audit)
    {
        if (context.Items.ContainsKey(CloseNotifiedKey))
        {
            return;
        }
        context.Items[CloseNotifiedKey] = true;
        await audit.SessionEnded(context.ConnectionId, RemoteControlAuditService.BrowserClosed);

        if (context.Items.TryGetValue(nameof(SessionInfo), out var value) &&
            value is RemoteControlSession session)
        {
            session.ViewerList.Remove(context.ConnectionId);
            if (!string.IsNullOrWhiteSpace(session.DesktopConnectionId))
            {
                await desktopHub.Clients
                    .Client(session.DesktopConnectionId)
                    .ViewerDisconnected(context.ConnectionId);
            }
        }

        context.Abort();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await _audit.SessionEnded(
            Context.ConnectionId,
            exception is null ? RemoteControlAuditService.EndedByTechnician : RemoteControlAuditService.ConnectionLost);

        if (Context.Items.TryGetValue(CloseTokenKey, out var token) && token is string closeToken)
        {
            _closeTokens.TryRemove(closeToken, out _);
        }

        if (Context.Items.ContainsKey(CloseNotifiedKey))
        {
            await base.OnDisconnectedAsync(exception);
            return;
        }

        if (!string.IsNullOrWhiteSpace(SessionInfo.DesktopConnectionId))
        {
            await _desktopHub.Clients
                .Client(SessionInfo.DesktopConnectionId)
                .ViewerDisconnected(Context.ConnectionId);
        }

        SessionInfo.ViewerList.Remove(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public Task SendDtoToClient(byte[] dtoWrapper)
    {
        if (string.IsNullOrWhiteSpace(SessionInfo.DesktopConnectionId))
        {
            return Task.CompletedTask;
        }

        return _desktopHub.Clients
            .Client(SessionInfo.DesktopConnectionId)
            .SendDtoToClient(dtoWrapper, Context.ConnectionId);
    }
    public async Task<Result> SendScreenCastRequestToDevice(string sessionId, string accessKey, string requesterName)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return Result.Fail("Informe o ID da sessão.");
        }

        // Throttle session ID/access key guessing (tracked per client IP).
        if (GetFailedSessionAttempts() >= MaxFailedSessionAttempts)
        {
            _logger.LogWarning(
                "Too many failed remote control session attempts.  Connection: {connectionId}.  IP: {ip}",
                Context.ConnectionId,
                ClientIp);
            return Result.Fail("Muitas tentativas inválidas. Tente novamente mais tarde.");
        }

        if (!_desktopSessionCache.TryGetValue(sessionId, out var session))
        {
            RegisterFailedSessionAttempt();
            return Result.Fail("ID de sessão não encontrado.");
        }

        // MVC filters (like ViewerAuthorizationFilter) don't apply to SignalR hubs,
        // so the "Remote control requires authentication" setting has to be
        // enforced here as well.  Unattended sessions are already protected by
        // their random access key, which is only handed out to authorized users.
        if (session.Mode != RemoteControlMode.Unattended &&
            Context.User?.Identity?.IsAuthenticated != true)
        {
            var settings = await _dataService.GetSettings();
            if (settings.RemoteControlRequiresAuthentication)
            {
                return Result.Fail("Você precisa estar logado para conectar a uma sessão.");
            }
        }

        if (session.Mode == RemoteControlMode.Unattended &&
            accessKey != session.AccessKey)
        {
            RegisterFailedSessionAttempt();
            _logger.LogError("Access key does not match for unattended session.  " +
                "Session ID: {sessionId}.  " +
                "Requester Name: {requesterName}.  " +
                "Requester Connection ID: {connectionId}",
                sessionId,
                requesterName,
                Context.ConnectionId);
            return Result.Fail("Falha de autorização.");
        }

        SessionInfo = session;
        SessionInfo.ViewerList.Add(Context.ConnectionId);
        SessionInfo.StreamId = Guid.NewGuid();
        RequesterDisplayName = requesterName;

        if (Context.User?.Identity?.IsAuthenticated == true)
        {
            SessionInfo.RequesterUserName = Context.User.Identity.Name ?? string.Empty;
        }

        var logMessage = $"Remote control session requested.  " +
                            $"Login ID (if logged in): {Context.User?.Identity?.Name}.  " +
                            $"Machine Name: {SessionInfo.MachineName}.  " +
                            $"Stream ID: {SessionInfo.StreamId}.  " +
                            $"Requester Name (if specified): {RequesterDisplayName}.  " +
                            $"Connection ID: {Context.ConnectionId}. User ID: {Context.UserIdentifier}.  " +
                            $"Screen Caster Connection ID: {SessionInfo.DesktopConnectionId}.  " +
                            $"Mode: {SessionInfo.Mode}.  " +
                            $"Requester IP Address: {Context.GetHttpContext()?.Connection?.RemoteIpAddress}";

        _logger.LogInformation("{msg}", logMessage);

        if (SessionInfo.Mode == RemoteControlMode.Unattended)
        {
            if (SessionInfo.RequireConsent)
            {
                var request = new RemoteControlAccessRequest(
                    Context.ConnectionId,
                    RequesterDisplayName,
                    SessionInfo.OrganizationName);

                var result = await _desktopHub.Clients
                    .Client(SessionInfo.DesktopConnectionId)
                    .PromptForAccess(request);

                if (result != Shared.Enums.PromptForAccessResult.Accepted)
                {
                    return Result.Fail($"Acesso não concedido. Motivo: {result}");
                }
            }

            SessionInfo.RequireConsent = false;

            await _audit.SessionStarted(Context.ConnectionId, SessionInfo, RequesterDisplayName, ClientIp);

            await _desktopHub.Clients
                .Client(SessionInfo.DesktopConnectionId)
                .GetScreenCast(
                    Context.ConnectionId,
                    RequesterDisplayName,
                    SessionInfo.NotifyUserOnStart,
                    SessionInfo.StreamId);
        }
        else
        {
            SessionInfo.Mode = RemoteControlMode.Attended;
            await _audit.SessionStarted(Context.ConnectionId, SessionInfo, RequesterDisplayName, ClientIp);
            await _desktopHub.Clients
                .Client(SessionInfo.DesktopConnectionId)
                .RequestScreenCast(
                    Context.ConnectionId,
                    RequesterDisplayName,
                    SessionInfo.NotifyUserOnStart,
                    SessionInfo.StreamId);
        }

        return Result.Ok();
    }

    public async Task StoreSessionRecording(IAsyncEnumerable<byte[]> webmStream)
    {
        try
        {
            await _sessionRecordingSink.SinkWebmStream(webmStream, SessionInfo);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Session recording stopped for stream {streamId}.", SessionInfo.StreamId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while storing session recording for stream {streamId}.", SessionInfo.StreamId);
        }
    }

}
