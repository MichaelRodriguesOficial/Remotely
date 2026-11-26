using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Remotely.Agent.Extensions;
using Remotely.Agent.Interfaces;
using Remotely.Shared;
using Remotely.Shared.Enums;
using Remotely.Shared.Models.Inventory;
using Remotely.Shared.Models.Diagnostics;
using Remotely.Shared.Interfaces;
using Remotely.Shared.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;
using Remotely.Desktop.Native.Windows;

namespace Remotely.Agent.Services;

public interface IAgentHubConnection : IAgentHubClient
{
    bool IsConnected { get; }

    Task Connect();
    Task SendHeartbeat();
}

public class AgentHubConnection : IAgentHubConnection, IDisposable
{
    private readonly IAppLauncher _appLauncher;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly IChatClientService _chatService;
    private readonly IConfigService _configService;
    private readonly IDeviceInformationService _deviceInfoService;
    private readonly IInventoryCollector _inventoryCollector;
    private readonly Remotely.Agent.Services.Diagnostics.IDiagnosticsRunner _diagnosticsRunner;
    private readonly SemaphoreSlim _diagnosticsLock = new(1, 1);
    private readonly SemaphoreSlim _inventoryLock = new(1, 1);
    private Timer? _inventoryTimer;
    private DateTimeOffset _lastInventorySent = DateTimeOffset.MinValue;
    private readonly IFileLogsManager _fileLogsManager;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<AgentHubConnection> _logger;
    private readonly IScriptExecutor _scriptExecutor;
    private readonly IScriptingShellFactory _scriptingShellFactory;
    private readonly IUninstaller _uninstaller;
    private readonly IUpdater _updater;
    private readonly IWakeOnLanService _wakeOnLanService;
    private ConnectionInfo? _connectionInfo;
    private Timer? _heartbeatTimer;
    private HubConnection? _hubConnection;
    private bool _isServerVerified;

    public AgentHubConnection(
        IConfigService configService,
        IUninstaller uninstaller,
        IScriptExecutor scriptExecutor,
        IChatClientService chatService,
        IAppLauncher appLauncher,
        IUpdater updater,
        IDeviceInformationService deviceInfoService,
        IHttpClientFactory httpFactory,
        IWakeOnLanService wakeOnLanService,
        IFileLogsManager fileLogsManager,
        IHostApplicationLifetime appLifetime,
        IScriptingShellFactory scriptingShellFactory,
        IInventoryCollector inventoryCollector,
        Remotely.Agent.Services.Diagnostics.IDiagnosticsRunner diagnosticsRunner,
        ILogger<AgentHubConnection> logger)
    {
        _inventoryCollector = inventoryCollector;
        _diagnosticsRunner = diagnosticsRunner;
        _configService = configService;
        _uninstaller = uninstaller;
        _scriptExecutor = scriptExecutor;
        _appLauncher = appLauncher;
        _chatService = chatService;
        _updater = updater;
        _deviceInfoService = deviceInfoService;
        _httpFactory = httpFactory;
        _wakeOnLanService = wakeOnLanService;
        _logger = logger;
        _fileLogsManager = fileLogsManager;
        _appLifetime = appLifetime;
        _scriptingShellFactory = scriptingShellFactory;
    }

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public async Task ChangeWindowsSession(string viewerConnectionId, string sessionId, string accessKey, string userConnectionId, string requesterName, string orgName, string orgId, int targetSessionId)
    {
        try
        {
            EnsureHubConnection();

            if (!_isServerVerified)
            {
                _logger.LogWarning("Session change attempted before server was verified.");
                return;
            }

            await _appLauncher.RestartScreenCaster(new[] { viewerConnectionId }, sessionId, accessKey, userConnectionId, requesterName, orgName, orgId, _hubConnection, targetSessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while handling ChangeWindowsSession.");
        }
    }

    public async Task Connect()
    {
        using var throttle = new SemaphoreSlim(1, 1);

        for (var i = 1; true; i++)
        {
            try
            {
                var waitSeconds = Math.Min(60, Math.Pow(i, 2));
                // This will allow the first attempt to go through immediately, but
                // subsequent attempts will have an exponential delay.
                _ = await throttle.WaitAsync(TimeSpan.FromSeconds(waitSeconds));

                _logger.LogInformation("Attempting to connect to server.");

                _connectionInfo = _configService.GetConnectionInfo();

                if (string.IsNullOrWhiteSpace(_connectionInfo.OrganizationID))
                {
                    _logger.LogError("Organization ID is not set.  Please set it in the config file.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(_connectionInfo.Host))
                {
                    _logger.LogError("Host (server URL) is not set.  Please set it in the config file.");
                    continue;
                }

                if (_hubConnection is not null)
                {
                    await _hubConnection.DisposeAsync();
                }

                _hubConnection = new HubConnectionBuilder()
                    .WithUrl(_connectionInfo.Host + "/hubs/service")
                    .WithAutomaticReconnect(new RetryPolicy(_logger))
                    .AddMessagePackProtocol()
                    .Build();

                RegisterMessageHandlers();

                _hubConnection.Reconnected += HubConnection_Reconnected;

                await _hubConnection.StartAsync();

                _logger.LogInformation("Connected to server.");

                var device = await _deviceInfoService.CreateDevice(_connectionInfo.DeviceID, _connectionInfo.OrganizationID);

                var result = await _hubConnection.InvokeAsync<bool>("DeviceCameOnline", device);

                if (!result)
                {
                    // Orgnanization ID wasn't found, or this device is already connected.
                    // The above can be caused by temporary issues on the server.  So we'll do
                    // nothing here and wait for it to get resolved.
                    _logger.LogError("There was an issue registering with the server.  The server might be undergoing maintenance, or the supplied organization ID might be incorrect.");
                    continue;
                }

                if (!await VerifyServer())
                {
                    continue;
                }

                if (await CheckForServerMigration())
                {
                    continue;
                }

                // TODO: Move to background service.
                _heartbeatTimer?.Dispose();
                _heartbeatTimer = new Timer(TimeSpan.FromMinutes(5).TotalMilliseconds);
                _heartbeatTimer.Elapsed += HeartbeatTimer_Elapsed;
                _heartbeatTimer.Start();

                // Inventário: logo depois de conectar e a cada 12 horas.
                _inventoryTimer?.Dispose();
                _inventoryTimer = new Timer(TimeSpan.FromHours(12).TotalMilliseconds);
                _inventoryTimer.Elapsed += InventoryTimer_Elapsed;
                _inventoryTimer.Start();
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(30));
                    await SendInventory(force: false);
                });

                ResumePendingMemoryTest();

                await _hubConnection.SendAsync("CheckForPendingScriptRuns");
                await _hubConnection.SendAsync("CheckForPendingRemoteControlSessions");

                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while connecting to server.");
            }
        }
    }

    public async Task DeleteLogs()
    {
        try
        {
            await _fileLogsManager.DeleteLogs(_appLifetime.ApplicationStopping);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while deleting logs.");
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _heartbeatTimer?.Dispose();
        _inventoryTimer?.Dispose();
    }

    public Task RequestInventory() => SendInventory(force: true);

    public Task RunDiagnostics(string runId, string[] tests) =>
        StartDiagnosticsJob(runId, TimeSpan.FromMinutes(15), (report, token) =>
            _diagnosticsRunner.Run(runId, tests, _connectionInfo?.Host, report, token));

    public Task RunRepair(string runId, string repairId, string? target) =>
        StartDiagnosticsJob(runId, TimeSpan.FromMinutes(90), (report, token) =>
            _diagnosticsRunner.RunRepair(runId, repairId, target, report, token));

    /// <summary>
    /// Envia o andamento de um diagnóstico. O resultado final espera a conexão voltar
    /// (reparos de rede derrubam a conexão por alguns segundos).
    /// </summary>
    private async Task SendDiagnosticReport(DiagnosticReport report, CancellationToken cancellationToken)
    {
        var waitUntil = DateTimeOffset.Now.AddSeconds(report.Completed ? 90 : 0);
        while (_hubConnection?.State != HubConnectionState.Connected && DateTimeOffset.Now < waitUntil)
        {
            await Task.Delay(2_000, cancellationToken);
        }

        if (_hubConnection?.State == HubConnectionState.Connected)
        {
            await _hubConnection.SendAsync("ReportDiagnostics", DiagnosticSerializer.Compress(report), cancellationToken);
        }
    }

    private int _memoryResumeRunning;

    /// <summary>Completa o teste de memória que reiniciou o computador (uma vez por conexão).</summary>
    private void ResumePendingMemoryTest()
    {
        if (Interlocked.Exchange(ref _memoryResumeRunning, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                // Dá tempo do servidor registrar o dispositivo online.
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(_appLifetime.ApplicationStopping);
                _memoryResumeCts = cts;
                await Task.Delay(TimeSpan.FromSeconds(10), cts.Token);
                await _diagnosticsRunner.ResumeMemoryTest(
                    _connectionInfo?.Host,
                    report => SendDiagnosticReport(report, cts.Token),
                    _diagnosticsLock,
                    cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Agente encerrando.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while resuming memory test.");
            }
            finally
            {
                _memoryResumeCts = null;
                Interlocked.Exchange(ref _memoryResumeRunning, 0);
            }
        });
    }

    /// <summary>
    /// Roda um diagnóstico ou reparo em segundo plano (podem levar minutos) e envia
    /// cada atualização ao servidor. Um por vez.
    /// </summary>
    private Task StartDiagnosticsJob(
        string runId,
        TimeSpan timeout,
        Func<Func<DiagnosticReport, Task>, CancellationToken, Task> job)
    {
        if (_hubConnection is null || !_isServerVerified)
        {
            _logger.LogWarning("Diagnostics requested before the server was verified.");
            return Task.CompletedTask;
        }

        _ = Task.Run(async () =>
        {
            // O re-teste automático chega logo depois que um reparo termina.
            if (!await _diagnosticsLock.WaitAsync(TimeSpan.FromSeconds(60)))
            {
                _logger.LogWarning("Diagnostics already running. Request {runId} ignored.", runId);
                return;
            }

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(_appLifetime.ApplicationStopping);
                cts.CancelAfter(timeout);
                _currentDiagnosticsCts = cts;

                await job(report => SendDiagnosticReport(report, cts.Token), cts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while running diagnostics {runId}.", runId);
            }
            finally
            {
                _currentDiagnosticsCts = null;
                _diagnosticsLock.Release();
            }
        });

        return Task.CompletedTask;
    }

    public async Task<DeviceEventResult> GetEventLog(DeviceEventQuery query, CancellationToken cancellationToken)
    {
        if (!_isServerVerified)
        {
            return new DeviceEventResult { Error = "Servidor não verificado." };
        }

        return await Task.Run(() => Remotely.Agent.Services.Events.DeviceEventReader.Read(query), cancellationToken);
    }

    public Task PowerAction(string action, int delaySeconds)
    {
        if (!_isServerVerified)
        {
            return Task.CompletedTask;
        }

        var (success, output) = PowerActions.Execute(action, delaySeconds);
        _logger.LogInformation("Power action {action} in {delay}s requested by the server. Success: {success}. {output}",
            action, delaySeconds, success, output);
        return Task.CompletedTask;
    }

    private CancellationTokenSource? _currentDiagnosticsCts;
    private CancellationTokenSource? _memoryResumeCts;

    public Task CancelDiagnostics()
    {
        if (!_isServerVerified)
        {
            return Task.CompletedTask;
        }

        _logger.LogInformation("Diagnostics canceled by the server.");
        try
        {
            _currentDiagnosticsCts?.Cancel();
            _memoryResumeCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Já terminou.
        }

        Remotely.Agent.Services.Diagnostics.DiagnosticsRunner.CancelPendingMemoryTest();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Coleta o inventário e envia ao servidor (JSON compactado).
    /// Sem <paramref name="force"/>, só envia se o último envio tiver mais de 11 horas.
    /// </summary>
    private async Task SendInventory(bool force)
    {
        var sinceLast = DateTimeOffset.Now - _lastInventorySent;
        if (!force && sinceLast < TimeSpan.FromHours(11))
        {
            return;
        }

        // Pedidos repetidos do painel não podem deixar a máquina coletando sem parar.
        if (force && sinceLast < TimeSpan.FromMinutes(1))
        {
            _logger.LogInformation("Inventory requested again too soon. Ignoring.");
            return;
        }

        // Uma coleta por vez.
        if (!await _inventoryLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            if (_hubConnection is null || _hubConnection.State != HubConnectionState.Connected || !_isServerVerified)
            {
                return;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_appLifetime.ApplicationStopping);
            cts.CancelAfter(TimeSpan.FromMinutes(5));

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var inventory = await _inventoryCollector.Collect(cts.Token);
            var data = InventorySerializer.Compress(inventory);

            await _hubConnection.SendAsync("ReportInventory", data, cts.Token);
            _lastInventorySent = DateTimeOffset.Now;

            _logger.LogInformation(
                "Inventory sent in {elapsed} ms ({bytes} bytes, {software} programs).",
                stopwatch.ElapsedMilliseconds,
                data.Length,
                inventory.Software.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while collecting or sending the inventory.");
        }
        finally
        {
            _inventoryLock.Release();
        }
    }

    private async void InventoryTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        await SendInventory(force: true);
    }

    public async Task ExecuteCommand(ScriptingShell shell, string command, string authToken, string senderUsername, string senderConnectionId)
    {
        try
        {
            EnsureHubConnection();

            if (!_isServerVerified)
            {
                _logger.LogWarning(
                    "Command attempted before server was verified.  Shell: {shell}.  Command: {command}.  Sender: {senderConnectionID}",
                    shell,
                    command,
                    senderConnectionId);
                return;
            }

            await _scriptExecutor.RunCommandFromTerminal(
                    shell,
                    command,
                    authToken,
                    senderUsername,
                    senderConnectionId,
                    ScriptInputType.Terminal,
                    TimeSpan.FromSeconds(30),
                    _hubConnection)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while executing command.");
        }
    }

    public async Task ExecuteCommandFromApi(ScriptingShell shell, string authToken, string requestID, string command, string senderUsername)
    {
        try
        {
            EnsureHubConnection();

            if (!_isServerVerified)
            {
                _logger.LogWarning(
                    "Command attempted before server was verified.  Shell: {shell}.  Command: {command}.  Sender: {senderUsername}",
                    shell,
                    command,
                    senderUsername);
                return;
            }

            await _scriptExecutor
                .RunCommandFromApi(shell, requestID, command, senderUsername, authToken, _hubConnection)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while executing command from API.");
        }
    }

    public async Task GetLogs(string senderConnectionId)
    {
        try
        {
            EnsureHubConnection();

            if (!await _fileLogsManager.AnyLogsExist(_appLifetime.ApplicationStopping))
            {
                var message = "There are no log entries written.";
                await _hubConnection.InvokeAsync("SendLogs", message, senderConnectionId).ConfigureAwait(false);
                return;
            }

            await foreach (var chunk in _fileLogsManager.ReadAllBytes(_appLifetime.ApplicationStopping))
            {
                var lines = Encoding.UTF8.GetString(chunk);
                await _hubConnection.InvokeAsync("SendLogs", lines, senderConnectionId).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while retrieving logs.");
        }
    }

    public async Task GetPowerShellCompletions(string inputText, int currentIndex, CompletionIntent intent, bool? forward, string senderConnectionId)
    {
        try
        {
            EnsureHubConnection();
            var session = _scriptingShellFactory.GetOrCreatePsCoreShell(senderConnectionId);
            var completion = session.GetCompletions(inputText, currentIndex, forward);
            var completionModel = completion.ToPwshCompletion();
            await _hubConnection
                .InvokeAsync("ReturnPowerShellCompletions", completionModel, intent, senderConnectionId)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while getting PowerShell completions.");
        }
    }

    public Task InvokeCtrlAltDel()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                return Task.CompletedTask;
            }

            if (!_isServerVerified)
            {
                _logger.LogWarning("CtrlAltDel attempted before server was verified.");
                return Task.CompletedTask;
            }

            User32.SendSAS(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while invoking CtrlAltDel.");
        }
        return Task.CompletedTask;
    }

    public async Task ReinstallAgent()
    {
        try
        {
            await _updater.InstallLatestVersion();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while reinstalling agent.");
        }
    }

    public async Task RemoteControl(Guid sessionId, string accessKey, string userConnectionId, string requesterName, string orgName, string orgId)
    {
        try
        {
            EnsureHubConnection();

            if (!_isServerVerified)
            {
                _logger.LogWarning("Remote control attempted before server was verified.");
                return;
            }
            await _appLauncher.LaunchRemoteControl(-1, $"{sessionId}", accessKey, userConnectionId, requesterName, orgName, orgId, _hubConnection);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while starting remote control.");
        }
    }

    public async Task RestartScreenCaster(string[] viewerIds, string sessionId, string accessKey, string userConnectionId, string requesterName, string orgName, string orgId)
    {
        try
        {
            EnsureHubConnection();

            if (!_isServerVerified)
            {
                _logger.LogWarning("Remote control attempted before server was verified.");
                return;
            }
            await _appLauncher.RestartScreenCaster(
                viewerIds,
                sessionId,
                accessKey,
                userConnectionId,
                requesterName,
                orgName,
                orgId,
                _hubConnection);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while restarting screen caster.");
        }
    }

    public async Task RunScript(
        Guid savedScriptId,
        int scriptRunId,
        string initiator,
        ScriptInputType scriptInputType,
        string authToken)
    {
        try
        {
            if (!_isServerVerified)
            {
                _logger.LogWarning(
                    "Script run attempted before server was verified.  Script ID: {savedScriptId}.  Initiator: {initiator}",
                    savedScriptId,
                    initiator);
                return;
            }

            await _scriptExecutor.RunScript(savedScriptId, scriptRunId, initiator, scriptInputType, authToken);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while running script.");
        }
    }

    public async Task SendChatMessage(string senderName, string message, string orgName, string orgId, bool disconnected, string senderConnectionId)
    {
        try
        {
            EnsureHubConnection();

            if (!_isServerVerified)
            {
                _logger.LogWarning("Chat attempted before server was verified.");
                return;
            }

            await _chatService
                .SendMessage(senderName, message, orgName, orgId, disconnected, senderConnectionId, _hubConnection)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while handling chat message.");
        }
    }

    public async Task SendHeartbeat()
    {
        try
        {
            if (_connectionInfo is null || _hubConnection is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_connectionInfo.OrganizationID))
            {
                _logger.LogError("Organization ID is not set.  Please set it in the config file.");
                return;
            }

            var currentInfo = await _deviceInfoService.CreateDevice(_connectionInfo.DeviceID, _connectionInfo.OrganizationID);
            await _hubConnection
                .SendAsync("DeviceHeartbeat", currentInfo)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while sending heartbeat.");
        }
    }

    /// <summary>Método antigo: salva na pasta temporária do agente, como o Remotely original.</summary>
    public Task TransferFileFromBrowserToAgent(string transferId, string[] fileIds, string requesterId, string expiringToken)
    {
        return ReceiveFiles(transferId, fileIds, requesterId, expiringToken, FileTransferDestinations.Temp);
    }

    public async Task ReceiveFiles(string transferId, string[] fileIds, string requesterId, string expiringToken, string destination)
    {
        try
        {
            EnsureHubConnection();

            if (!_isServerVerified)
            {
                _logger.LogWarning("File upload attempted before server was verified.");
                return;
            }

            if (!FileTransferDestinations.TryParse(destination, OperatingSystem.IsWindows(), out var kind, out var customPath, out var error))
            {
                await SendFileTransferResult(transferId, requesterId, false, error);
                return;
            }

            _logger.LogInformation("File upload started by {requesterID} to {destination}.", requesterId, destination);

            var target = FileDestinationResolver.Resolve(kind, customPath);
            var saved = new List<string>();

            foreach (var fileID in fileIds)
            {
                var url = $"{_connectionInfo?.Host}/API/FileSharing/{fileID}";
                using var client = _httpFactory.CreateClient();
                client.DefaultRequestHeaders.Add(AppConstants.ExpiringTokenHeaderName, expiringToken);
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode)
                {
                    await SendFileTransferResult(transferId, requesterId, false,
                        $"O servidor recusou o download do arquivo ({(int)response.StatusCode}).");
                    return;
                }

                var disposition = response.Content.Headers.ContentDisposition;
                var filename = FileDestinationResolver.SanitizeFileName(disposition?.FileNameStar ?? disposition?.FileName);
                var filePath = FileDestinationResolver.GetAvailablePath(target.Folder, filename);

                await using (var rs = await response.Content.ReadAsStreamAsync())
                await using (var fs = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write))
                {
                    await rs.CopyToAsync(fs);
                }

                if (target.LinuxOwner is { } owner)
                {
                    FileDestinationResolver.ChownToUser(filePath, owner);
                }

                saved.Add(filePath);
            }

            var detail = string.Join("\n", saved);
            if (!string.IsNullOrWhiteSpace(target.Note))
            {
                detail = target.Note + "\n" + detail;
            }

            await SendFileTransferResult(transferId, requesterId, true, detail);
            await _hubConnection.SendAsync("TransferCompleted", transferId, requesterId);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied while saving transferred file.");
            await SendFileTransferResult(transferId, requesterId, false, "Sem permissão para gravar nessa pasta.");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error while saving transferred file.");
            await SendFileTransferResult(transferId, requesterId, false, $"Não foi possível gravar o arquivo: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while transfering file from browser to agent.");
            await SendFileTransferResult(transferId, requesterId, false, "Erro no agente ao receber o arquivo.");
        }
    }

    private async Task SendFileTransferResult(string transferId, string requesterId, bool success, string detail)
    {
        try
        {
            if (_hubConnection is not null)
            {
                await _hubConnection.SendAsync("FileTransferResult", transferId, requesterId, success, detail);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not report file transfer result.");
        }
    }

    public Task TriggerHeartbeat() => SendHeartbeat();

    public Task UninstallAgent()
    {
        try
        {
            _uninstaller.UninstallAgent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while uninstalling agent.");
        }
        return Task.CompletedTask;
    }

    public async Task WakeDevice(string macAddress)
    {
        try
        {
            _logger.LogInformation(
                    "Received request to wake device with MAC address {macAddress}.",
                    macAddress);
            await _wakeOnLanService.WakeDevice(macAddress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while waking device.");
        }
    }

    private async Task<bool> CheckForServerMigration()
    {
        if (_connectionInfo is null || _hubConnection is null)
        {
            return false;
        }

        var serverUrl = await _hubConnection.InvokeAsync<string>("GetServerUrl");

        if (Uri.TryCreate(serverUrl, UriKind.Absolute, out var serverUri) &&
            Uri.TryCreate(_connectionInfo.Host, UriKind.Absolute, out var savedUri) &&
            serverUri.Host != savedUri.Host)
        {
            _connectionInfo.Host = serverUrl.Trim().TrimEnd('/');
            _connectionInfo.ServerVerificationToken = null;
            _configService.SaveConnectionInfo(_connectionInfo);
            await _hubConnection.DisposeAsync();
            return true;
        }
        return false;
    }

    [MemberNotNull(nameof(_hubConnection))]
    private void EnsureHubConnection()
    {
        if (_hubConnection is null || _hubConnection.State != HubConnectionState.Connected)
        {
            throw new InvalidOperationException("Hub connection is not established.");
        }
    }
    private async void HeartbeatTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        await SendHeartbeat();
    }

    private async Task HubConnection_Reconnected(string? arg)
    {
        if (_connectionInfo is null || _hubConnection is null)
        {
            return;
        }

        _logger.LogInformation("Reconnected to server.");
        await _updater.CheckForUpdates();

        var device = await _deviceInfoService.CreateDevice(_connectionInfo.DeviceID, $"{_connectionInfo.OrganizationID}");

        if (!await _hubConnection.InvokeAsync<bool>("DeviceCameOnline", device))
        {
            await Connect();
            return;
        }

        if (await CheckForServerMigration())
        {
            await Connect();
            return;
        }
    }

    private void RegisterMessageHandlers()
    {
        if (_hubConnection is null)
        {
            throw new InvalidOperationException("Hub connection is null.");
        }

        // TODO: Replace all these parameters with a single DTO per method.
        _hubConnection.On<string, string, string, string, string, string, string, int>(
            nameof(ChangeWindowsSession),
            ChangeWindowsSession);

        _hubConnection.On<string, string, string, string, bool, string>(nameof(SendChatMessage), SendChatMessage);

        _hubConnection.On(nameof(InvokeCtrlAltDel), InvokeCtrlAltDel);

        _hubConnection.On(nameof(DeleteLogs), DeleteLogs);

        _hubConnection.On<ScriptingShell, string, string, string, string>(nameof(ExecuteCommand), ExecuteCommand);

        _hubConnection.On<ScriptingShell, string, string, string, string>(nameof(ExecuteCommandFromApi), ExecuteCommandFromApi);

        _hubConnection.On<string>(nameof(GetLogs), GetLogs);

        _hubConnection.On<string, int, CompletionIntent, bool?, string>(nameof(GetPowerShellCompletions), GetPowerShellCompletions);

        _hubConnection.On(nameof(ReinstallAgent), ReinstallAgent);

        _hubConnection.On(nameof(UninstallAgent), UninstallAgent);

        _hubConnection.On<Guid, string, string, string, string, string>(nameof(RemoteControl), RemoteControl);

        _hubConnection.On<string[], string, string, string, string, string, string>(
            nameof(RestartScreenCaster),
            RestartScreenCaster);

        _hubConnection.On<Guid, int, string, ScriptInputType, string>(nameof(RunScript), RunScript);

        _hubConnection.On<string, string[], string, string>(
            nameof(TransferFileFromBrowserToAgent),
            TransferFileFromBrowserToAgent);

        _hubConnection.On<string, string[], string, string, string>(nameof(ReceiveFiles), ReceiveFiles);

        _hubConnection.On(nameof(TriggerHeartbeat), TriggerHeartbeat);

        _hubConnection.On(nameof(RequestInventory), RequestInventory);

        _hubConnection.On<string, string[]>(nameof(RunDiagnostics), RunDiagnostics);

        _hubConnection.On<string, string, string?>(nameof(RunRepair), RunRepair);

        _hubConnection.On(nameof(CancelDiagnostics), CancelDiagnostics);

        _hubConnection.On<string, int>(nameof(PowerAction), PowerAction);

        _hubConnection.On<DeviceEventQuery, DeviceEventResult>(nameof(GetEventLog), query => GetEventLog(query, CancellationToken.None));

        _hubConnection.On<string>(nameof(WakeDevice), WakeDevice);
    }

    private async Task<bool> VerifyServer()
    {
        if (_connectionInfo is null || _hubConnection is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_connectionInfo.ServerVerificationToken))
        {
            _isServerVerified = true;
            _connectionInfo.ServerVerificationToken = Guid.NewGuid().ToString();
            await _hubConnection.SendAsync("SetServerVerificationToken", _connectionInfo.ServerVerificationToken).ConfigureAwait(false);
            _configService.SaveConnectionInfo(_connectionInfo);
        }
        else
        {
            var verificationToken = await _hubConnection.InvokeAsync<string>("GetServerVerificationToken");

            if (verificationToken == _connectionInfo.ServerVerificationToken)
            {
                _isServerVerified = true;
            }
            else
            {
                _logger.LogWarning("Server sent an incorrect verification token.  Token Sent: {verificationToken}.", verificationToken);
                return false;
            }
        }

        return true;
    }

    private class RetryPolicy : IRetryPolicy
    {
        private readonly ILogger<AgentHubConnection> _logger;

        public RetryPolicy(ILogger<AgentHubConnection> logger)
        {
            _logger = logger;
        }

        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            if (retryContext.PreviousRetryCount == 0)
            {
                return TimeSpan.FromSeconds(3);
            }

            var waitSeconds = Random.Shared.Next(3, 10);
            _logger.LogDebug("Attempting to reconnect in {seconds} seconds.", waitSeconds);
            return TimeSpan.FromSeconds(waitSeconds);
        }
    }
}
