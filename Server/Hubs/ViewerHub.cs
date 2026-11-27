using Remotely.Server.Enums;
using Remotely.Server.Filters;
using Remotely.Server.Models;
using Remotely.Server.Services;
using Remotely.Shared.Interfaces;
using Remotely.Shared.Models;
using Remotely.Shared.Models.Dtos;
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
    private readonly IAccessProfileService _accessProfiles;
    private readonly IRemoteSessionNotifier _notifier;
    private readonly IAgentHubSessionCache _agentCache;
    private readonly IGlpiClient _glpi;

    public ViewerHub(
        IRemoteControlSessionCache desktopSessionCache,
        IDesktopStreamCache streamCache,
        IHubContext<AgentHub, IAgentHubClient> agentHub,
        IHubContext<DesktopHub, IDesktopHubClient> desktopHub,
        ISessionRecordingSink sessionRecordingSink,
        IDataService dataService,
        IRemoteControlAuditService audit,
        IAccessProfileService accessProfiles,
        IRemoteSessionNotifier notifier,
        IAgentHubSessionCache agentCache,
        IGlpiClient glpi,
        ILogger<ViewerHub> logger)
    {
        _glpi = glpi;
        _agentCache = agentCache;
        _notifier = notifier;
        _audit = audit;
        _accessProfiles = accessProfiles;
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
            if (!Permissions.SystemActions || !Permissions.Input || SessionInfo.ViewOnly)
            {
                return Result.Fail("Seu perfil não permite trocar a sessão do Windows.");
            }

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

    private const string PermissionsKey = "RemoteControlPermissions";
    internal const string LastInputKey = "RemoteControlLastInput";

    internal static bool IsInputDto(DtoType dtoType) => dtoType is
        DtoType.MouseMove or DtoType.MouseDown or DtoType.MouseUp or DtoType.Tap or
        DtoType.MouseWheel or DtoType.KeyDown or DtoType.KeyUp or DtoType.KeyPress;

    /// <summary>Permissões desta conexão (definidas quando a sessão é aceita).</summary>
    private RemoteControlPermissions Permissions =>
        Context.Items.TryGetValue(PermissionsKey, out var value) && value is RemoteControlPermissions permissions
            ? permissions
            : RemoteControlPermissions.Full;

    /// <summary>
    /// Anotação do que foi feito na sessão (fica no histórico) e, se pedido, acompanhamento num chamado
    /// do GLPI ou um chamado novo. glpiMode: "none", "followup" ou "ticket".
    /// </summary>
    public async Task<Result<string>> SaveSessionNote(string? note, string? glpiMode, string? ticketId)
    {
        var logId = _audit.GetOpenLogId(Context.ConnectionId);
        if (logId is null || Context.User?.Identity?.IsAuthenticated != true)
        {
            return Result.Fail<string>("Esta sessão não tem registro no histórico.");
        }

        note = note?.Trim() ?? string.Empty;
        if (note.Length > 4000)
        {
            note = note[..4000];
        }

        var mode = (glpiMode ?? "none").Trim().ToLowerInvariant();
        if (mode == "none" || string.IsNullOrWhiteSpace(note))
        {
            if (!string.IsNullOrWhiteSpace(note))
            {
                await _audit.Update(logId, note: note);
            }
            return Result.Ok(string.Empty);
        }

        var settings = await _dataService.GetSettings();
        if (!_glpi.IsConfigured(settings))
        {
            await _audit.Update(logId, note: note);
            return Result.Fail<string>("Anotação salva, mas o GLPI não está configurado.");
        }

        var session = await _audit.GetSession(logId);
        var machine = session?.DeviceName ?? SessionInfo.MachineName;
        var content =
            $"Acesso remoto pelo Remotely\n" +
            $"Computador: {machine}\n" +
            $"Técnico: {RequesterDisplayName}\n" +
            $"Início: {session?.StartedAt.ToLocalTime():dd/MM/yyyy HH:mm}\n\n" +
            note;

        Result<string> result = mode switch
        {
            "followup" => await _glpi.AddFollowup(settings, ticketId ?? string.Empty, content),
            "ticket" => await _glpi.CreateTicket(settings, $"Acesso remoto - {machine}", content, machine),
            _ => Result.Fail<string>("Opção do GLPI inválida.")
        };

        if (!result.IsSuccess)
        {
            await _audit.Update(logId, note: note);
            return Result.Fail<string>($"Anotação salva no histórico. {result.Reason}");
        }

        await _audit.Update(logId, note: note, ticketId: result.Value);
        _logger.LogInformation(
            "Session note sent to GLPI ({mode}) ticket {ticket}. User: {user}.",
            mode, result.Value, Context.User?.Identity?.Name);
        return Result.Ok(result.Value);
    }

    /// <summary>Pelo último inventário: rede só por Wi-Fi, sem cabo ligado.</summary>
    internal static bool IsOnlyOnWifi(Remotely.Shared.Models.Inventory.DeviceInventory? inventory)
    {
        var ports = inventory?.NetworkPorts?
            .Where(x => x.Connected == true && x.Virtual != true)
            .ToList() ?? [];
        return ports.Any(x => x.Type == "Wi-Fi") && !ports.Any(x => x.Type == "Ethernet");
    }

    public async Task<RemoteControlViewerOptions> GetViewerOptions()
    {
        var settings = await _dataService.GetSettings();
        var onWifi = false;
        if (!string.IsNullOrWhiteSpace(SessionInfo.DeviceId))
        {
            var record = await _dataService.GetDeviceInventory(SessionInfo.DeviceId);
            onWifi = IsOnlyOnWifi(Remotely.Shared.Models.Inventory.InventorySerializer.FromJson(record?.Data));
        }
        var permissions = Permissions;
        return new RemoteControlViewerOptions()
        {
            ShouldRecordSession = settings.EnableRemoteControlRecording || SessionInfo.RecordSession,
            PrivacyScreen = RemoteControlPolicy.NormalizePrivacy(SessionInfo.PrivacyScreen),
            DeviceOnWifi = onWifi,
            AllowNote = Context.User?.Identity?.IsAuthenticated == true &&
                _audit.GetOpenLogId(Context.ConnectionId) is not null,
            GlpiEnabled = Context.User?.Identity?.IsAuthenticated == true &&
                _audit.GetOpenLogId(Context.ConnectionId) is not null &&
                _glpi.IsConfigured(settings),
            AllowInput = permissions.Input && !SessionInfo.ViewOnly,
            AllowFiles = permissions.Files,
            AllowClipboard = permissions.Clipboard,
            AllowSystemActions = permissions.SystemActions,
            IdleTimeoutMinutes = Math.Max(0, settings.RemoteControlIdleMinutes),
            // O painel lateral só existe em sessão aberta pelo painel, com técnico logado.
            // A página do painel confere de novo o acesso ao computador.
            DeviceId = SessionInfo.Mode == RemoteControlMode.Unattended &&
                Context.User?.Identity?.IsAuthenticated == true &&
                !string.IsNullOrWhiteSpace(SessionInfo.DeviceId)
                    ? SessionInfo.DeviceId
                    : null,
            DeviceName = string.IsNullOrWhiteSpace(SessionInfo.MachineName) ? null : SessionInfo.MachineName,
            DevicePlatform = !string.IsNullOrWhiteSpace(SessionInfo.DeviceId) &&
                _agentCache.TryGetByDeviceId(SessionInfo.DeviceId, out var sessionDevice)
                    ? sessionDevice.Platform
                    : null
        };
    }

    /// <summary>
    /// "Reiniciar e voltar": reinicia o computador sem encerrar a sessão. Quando o agente volta,
    /// o servidor reabre a tela remota (AgentHub.CheckForPendingRemoteControlSessions) e o visualizador reconecta.
    /// </summary>
    public async Task<Result> RestartDevice(string? mode)
    {
        var safeMode = string.Equals(mode, "safe", StringComparison.OrdinalIgnoreCase);
        var session = SessionInfo;
        if (session.Mode != RemoteControlMode.Unattended || string.IsNullOrWhiteSpace(session.DeviceId))
        {
            return Result.Fail("Disponível só no acesso aberto pelo painel.");
        }

        if (!session.ViewerList.Contains(Context.ConnectionId))
        {
            return Result.Fail("A sessão ainda não foi iniciada.");
        }

        if (!Permissions.SystemActions || !Permissions.Input || session.ViewOnly)
        {
            return Result.Fail("Seu perfil não permite reiniciar o computador.");
        }

        if (!_agentCache.TryGetConnectionId(session.DeviceId, out var agentConnectionId))
        {
            return Result.Fail("O agente do computador não está conectado.");
        }

        if (safeMode &&
            (!_agentCache.TryGetByDeviceId(session.DeviceId, out var device) ||
             device.Platform?.Contains("Windows", StringComparison.OrdinalIgnoreCase) != true))
        {
            return Result.Fail("O modo de segurança só existe no Windows.");
        }

        // Desconexão esperada: a sessão fica guardada até o agente voltar, sem tentar reabrir antes.
        session.StreamerState = StreamerState.WindowsShuttingDown | StreamerState.DisconnectExpected;
        session.AgentConnectionId = agentConnectionId;
        Context.Items[LastInputKey] = DateTimeOffset.Now;
        Context.Items.Remove(IdleWarnedKey);

        _logger.LogInformation(
            "Restart ({mode}) requested from remote control. Device: {deviceId}. User: {user}. Viewer: {connectionId}.",
            safeMode ? "safe mode" : "normal",
            session.DeviceId,
            Context.User?.Identity?.Name,
            Context.ConnectionId);

        await _agentHub.Clients.Client(agentConnectionId).PowerAction(safeMode ? "restart-safe" : "restart", 0);

        var others = session.ViewerList.Where(x => x != Context.ConnectionId).ToList();
        if (others.Count > 0)
        {
            await Clients.Clients(others).ShowMessage("O computador está reiniciando. O acesso volta quando ele ligar.");
        }

        return Result.Ok();
    }

    /// <summary>Esperando o computador reiniciar ou reabrir a tela remota (não conta como sessão parada).</summary>
    internal static bool IsWaitingForDevice(HubCallerContext context) =>
        context.Items.TryGetValue(nameof(SessionInfo), out var value) &&
        value is RemoteControlSession session &&
        (session.StreamerState.HasFlag(StreamerState.DisconnectExpected) ||
         session.StreamerState.HasFlag(StreamerState.Reconnecting));

    /// <summary>
    /// Confere no servidor se o tipo de mensagem é permitido para esta conexão. O visualizador esconde
    /// os botões, mas a regra vale aqui: um visualizador alterado não passa por cima do perfil.
    /// </summary>
    public static bool IsDtoAllowed(DtoType dtoType, RemoteControlPermissions permissions, bool viewOnly)
    {
        return dtoType switch
        {
            DtoType.MouseMove or DtoType.MouseDown or DtoType.MouseUp or DtoType.Tap or
            DtoType.MouseWheel or DtoType.KeyDown or DtoType.KeyUp or DtoType.KeyPress or
            DtoType.SetKeyStatesUp => permissions.Input && !viewOnly,
            DtoType.ClipboardText or DtoType.TextTransfer => permissions.Clipboard && permissions.Input && !viewOnly,
            DtoType.File or DtoType.OpenFileTransferWindow or DtoType.FolderListRequest or
            DtoType.DownloadFilesRequest => permissions.Files,
            DtoType.CtrlAltDel or DtoType.ToggleBlockInput or
            DtoType.TogglePrivacyScreen => permissions.SystemActions && permissions.Input && !viewOnly,
            _ => true
        };
    }

    public async Task InvokeCtrlAltDel()
    {
        if (!IsDtoAllowed(DtoType.CtrlAltDel, Permissions, SessionInfo.ViewOnly))
        {
            return;
        }

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

    // Conexões com sessão aceita, para o monitor de sessão parada.
    private static readonly ConcurrentDictionary<string, HubCallerContext> _activeViewers = new();
    internal const string IdleWarnedKey = "RemoteControlIdleWarned";

    /// <summary>Conexões com sessão ativa (usado pelo <see cref="RemoteControlIdleMonitor"/>).</summary>
    internal static IReadOnlyCollection<HubCallerContext> ActiveViewers => _activeViewers.Values.ToList();

    /// <summary>Sessão só de visualização (não conta para o encerramento por inatividade).</summary>
    internal static bool IsViewOnly(HubCallerContext context)
    {
        var viewOnly = context.Items.TryGetValue(nameof(SessionInfo), out var value) &&
            value is RemoteControlSession { ViewOnly: true };
        var noInput = context.Items.TryGetValue(PermissionsKey, out var perms) &&
            perms is RemoteControlPermissions { Input: false };
        return viewOnly || noInput;
    }

    /// <summary>Encerra a conexão do servidor (ex.: inatividade), avisando o computador e o histórico.</summary>
    internal static Task EndFromServer(HubCallerContext context, IHubContext<DesktopHub, IDesktopHubClient> desktopHub, IRemoteControlAuditService audit, string reason) =>
        EndViewerConnection(context, desktopHub, audit, reason);

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
        return EndViewerConnection(Context, _desktopHub, _audit, RemoteControlAuditService.BrowserClosed);
    }

    /// <summary>Encerra a conexão do viewer dono do token. Usado pelo endpoint de sendBeacon.</summary>
    public static async Task<bool> CloseByToken(string token, IHubContext<DesktopHub, IDesktopHubClient> desktopHub, IRemoteControlAuditService audit)
    {
        if (string.IsNullOrWhiteSpace(token) || !_closeTokens.TryRemove(token, out var context))
        {
            return false;
        }

        await EndViewerConnection(context, desktopHub, audit, RemoteControlAuditService.BrowserClosed);
        return true;
    }

    private static async Task EndViewerConnection(HubCallerContext context, IHubContext<DesktopHub, IDesktopHubClient> desktopHub, IRemoteControlAuditService audit, string reason)
    {
        if (context.Items.ContainsKey(CloseNotifiedKey))
        {
            return;
        }
        context.Items[CloseNotifiedKey] = true;
        _activeViewers.TryRemove(context.ConnectionId, out _);
        await audit.SessionEnded(context.ConnectionId, reason);

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
        _activeViewers.TryRemove(Context.ConnectionId, out _);
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

        DtoType dtoType;
        try
        {
            dtoType = MessagePack.MessagePackSerializer.Deserialize<DtoWrapper>(dtoWrapper).DtoType;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid DTO from viewer {connectionId}.", Context.ConnectionId);
            return Task.CompletedTask;
        }

        if (!IsDtoAllowed(dtoType, Permissions, SessionInfo.ViewOnly))
        {
            return Task.CompletedTask;
        }

        // Tela preta proibida pelo grupo do computador.
        if (dtoType == DtoType.TogglePrivacyScreen &&
            RemoteControlPolicy.NormalizePrivacy(SessionInfo.PrivacyScreen) == RemoteControlPolicy.PrivacyDeny)
        {
            return Task.CompletedTask;
        }

        if (IsInputDto(dtoType))
        {
            Context.Items[LastInputKey] = DateTimeOffset.Now;
            Context.Items.Remove(IdleWarnedKey);
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
        Context.Items[LastInputKey] = DateTimeOffset.Now;

        // Sessão aberta pelo painel: vale o perfil de quem abriu. Sessão assistida (ID de 9 dígitos):
        // vale o perfil de quem está conectando, se estiver logado.
        if (session.Mode == RemoteControlMode.Unattended)
        {
            Context.Items[PermissionsKey] = session.Permissions;
        }
        else if (Context.User?.Identity?.IsAuthenticated == true &&
                 await _dataService.GetUserByName($"{Context.User.Identity.Name}") is { IsSuccess: true } viewerUser)
        {
            Context.Items[PermissionsKey] = _accessProfiles.GetRemoteControlPermissions(viewerUser.Value);
        }
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
                    SessionInfo.OrganizationName,
                    SessionInfo.ConsentAutoAcceptSeconds);

                var result = await _desktopHub.Clients
                    .Client(SessionInfo.DesktopConnectionId)
                    .PromptForAccess(request);

                if (result != Shared.Enums.PromptForAccessResult.Accepted)
                {
                    SessionInfo.ViewerList.Remove(Context.ConnectionId);
                    return Result.Fail(result switch
                    {
                        Shared.Enums.PromptForAccessResult.Denied => "O usuário recusou o acesso.",
                        Shared.Enums.PromptForAccessResult.TimedOut => "O usuário não respondeu ao pedido de acesso.",
                        _ => "Não foi possível pedir o acesso ao usuário."
                    });
                }
            }

            SessionInfo.RequireConsent = false;

            // Reconexão (reinício, troca de sessão do Windows) não abre outro registro nem repete o aviso.
            if (await _audit.SessionStarted(Context.ConnectionId, SessionInfo, RequesterDisplayName, ClientIp))
            {
                _ = _notifier.NotifySessionStarted(SessionInfo, RequesterDisplayName, ClientIp);
            }
            _activeViewers[Context.ConnectionId] = Context;

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
            if (await _audit.SessionStarted(Context.ConnectionId, SessionInfo, RequesterDisplayName, ClientIp))
            {
                _ = _notifier.NotifySessionStarted(SessionInfo, RequesterDisplayName, ClientIp);
            }
            _activeViewers[Context.ConnectionId] = Context;
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
        var settings = await _dataService.GetSettings();
        if (!settings.EnableRemoteControlRecording && !SessionInfo.RecordSession)
        {
            _logger.LogWarning("Recording rejected: not enabled for this session. Viewer: {connectionId}.", Context.ConnectionId);
            return;
        }

        var logId = _audit.GetOpenLogId(Context.ConnectionId);
        try
        {
            await _sessionRecordingSink.SinkWebmStream(
                webmStream,
                SessionInfo,
                logId is null ? null : file => _audit.Update(logId, recordingFile: file));
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
