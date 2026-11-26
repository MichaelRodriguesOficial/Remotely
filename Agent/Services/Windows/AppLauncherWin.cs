using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Remotely.Agent.Interfaces;
using Remotely.Shared.Models;
using Remotely.Shared.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Threading.Tasks;
using Remotely.Desktop.Native.Windows;

namespace Remotely.Agent.Services.Windows;

[SupportedOSPlatform("windows")]
public class AppLauncherWin : IAppLauncher
{
    private readonly ConnectionInfo _connectionInfo;
    private readonly ILogger<AppLauncherWin> _logger;
    private readonly string _rcBinaryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Desktop", EnvironmentHelper.DesktopExecutableFileName);

    public AppLauncherWin(IConfigService configService, ILogger<AppLauncherWin> logger)
    {
        _connectionInfo = configService.GetConnectionInfo();
        _logger = logger;
    }

    public async Task<int> LaunchChatService(string pipeName, string userConnectionId, string requesterName, string orgName, string orgId, HubConnection hubConnection)
    {
        try
        {
            if (!File.Exists(_rcBinaryPath))
            {
                await hubConnection.SendAsync("DisplayMessage", "Chat executable not found on target device.", "Executable not found on device.", "bg-danger", userConnectionId);
            }


            // Start Desktop app.
            await hubConnection.SendAsync("DisplayMessage", $"Starting chat service.", "Starting chat service.", "bg-success", userConnectionId);
            if (WindowsIdentity.GetCurrent().IsSystem)
            {
                var result = Win32Interop.CreateInteractiveSystemProcess(
                    BuildCommandLine(
                        "--mode", "Chat",
                        "--host", _connectionInfo.Host,
                        "--pipe-name", pipeName,
                        "--requester-name", requesterName,
                        "--org-name", orgName,
                        "--org-id", orgId),
                    targetSessionId: -1,
                    hiddenWindow: false,
                    out var procInfo);
                if (!result)
                {
                    await hubConnection.SendAsync("DisplayMessage",
                        "Chat service failed to start on target device.",
                        "Failed to start chat service.",
                        "bg-danger",
                        userConnectionId);
                }
                else
                {
                    return procInfo.dwProcessId;
                }
            }
            else
            {
                return StartAsCurrentUser(null,
                    "--mode", "Chat",
                    "--host", _connectionInfo.Host,
                    "--requester-name", requesterName,
                    "--org-name", orgName,
                    "--org-id", orgId,
                    "--pipe-name", pipeName).Id;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while launching chat.");
            await hubConnection.SendAsync("DisplayMessage",
                "Chat service failed to start on target device.",
                "Failed to start chat service.",
                "bg-danger",
                userConnectionId);
        }
        return -1;
    }

    public async Task LaunchRemoteControl(int targetSessionId, string sessionId, string accessKey, string userConnectionId, string requesterName, string orgName, string orgId, HubConnection hubConnection)
    {
        try
        {
            if (!File.Exists(_rcBinaryPath))
            {
                await hubConnection.SendAsync("DisplayMessage",
                    "Remote control executable not found on target device.",
                    "Executable not found on device.",
                    "bg-danger",
                    userConnectionId);
                return;
            }


            // Start Desktop app.
            await hubConnection.SendAsync("DisplayMessage",
                "Starting remote control",
                "Starting remote control",
                "bg-success",
                userConnectionId);
            if (WindowsIdentity.GetCurrent().IsSystem)
            {
                var result = StartAsSystemInSession(
                    sessionId,
                    accessKey,
                    targetSessionId,
                    "--mode", "Unattended",
                    "--host", _connectionInfo.Host,
                    "--requester-name", requesterName,
                    "--org-name", orgName,
                    "--org-id", orgId);
                if (!result)
                {
                    await hubConnection.SendAsync("DisplayMessage",
                        "Remote control failed to start on target device.",
                        "Failed to start remote control.",
                        "bg-danger",
                        userConnectionId);
                }
            }
            else
            {
                StartAsCurrentUser(new() { [SessionIdVariable] = sessionId, [AccessKeyVariable] = accessKey },
                    "--mode", "Unattended",
                    "--host", _connectionInfo.Host,
                    "--requester-name", requesterName,
                    "--org-name", orgName,
                    "--org-id", orgId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while launching remote control.");
            await hubConnection.SendAsync("DisplayMessage",
                "Remote control failed to start on target device.",
                "Failed to start remote control.",
                "bg-danger",
                userConnectionId);
        }
    }
    public async Task RestartScreenCaster(string[] viewerIds, string sessionId, string accessKey, string userConnectionId, string requesterName, string orgName, string orgId, HubConnection hubConnection, int targetSessionID = -1)
    {
        try
        {
            // Start Desktop app.                 
            _logger.LogInformation("Restarting screen caster.");
            if (WindowsIdentity.GetCurrent().IsSystem)
            {
                // Give a little time for session changing, etc.
                await Task.Delay(1000);

                var result = StartAsSystemInSession(
                    sessionId,
                    accessKey,
                    targetSessionID,
                    "--mode", "Unattended",
                    "--relaunch", "true",
                    "--host", _connectionInfo.Host,
                    "--requester-name", requesterName,
                    "--org-name", orgName,
                    "--org-id", orgId,
                    "--viewers", string.Join(",", viewerIds));

                if (!result)
                {
                    _logger.LogWarning("Failed to relaunch screen caster.");
                    await hubConnection.SendAsync("SendConnectionFailedToViewers", viewerIds);
                    await hubConnection.SendAsync("DisplayMessage",
                        "Remote control failed to start on target device.",
                        "Failed to start remote control.",
                        "bg-danger",
                        userConnectionId);
                }
            }
            else
            {
                StartAsCurrentUser(new() { [SessionIdVariable] = sessionId, [AccessKeyVariable] = accessKey },
                    "--mode", "Unattended",
                    "--relaunch", "true",
                    "--host", _connectionInfo.Host,
                    "--requester-name", requesterName,
                    "--org-name", orgName,
                    "--org-id", orgId,
                    "--viewers", string.Join(",", viewerIds));
            }
        }
        catch (Exception ex)
        {
            await hubConnection.SendAsync("SendConnectionFailedToViewers", viewerIds);
            _logger.LogError(ex, "Error while restarting screen caster.");
            throw;
        }
    }

    // A chave de acesso e o ID da sessão não vão mais na linha de comando (visível para outros programas):
    // seguem por variável de ambiente, herdada só pelo processo criado, e o app as apaga ao ler.
    public const string SessionIdVariable = "REMOTELY_SESSION_ID";
    public const string AccessKeyVariable = "REMOTELY_ACCESS_KEY";
    private static readonly object _environmentLock = new();

    private string BuildCommandLine(params string?[] arguments)
    {
        return CommandLineQuoting.Quote(_rcBinaryPath) + " " + CommandLineQuoting.Join(arguments);
    }

    private bool StartAsSystemInSession(string sessionId, string accessKey, int targetSessionId, params string?[] arguments)
    {
        var commandLine = BuildCommandLine(arguments);
        lock (_environmentLock)
        {
            try
            {
                Environment.SetEnvironmentVariable(SessionIdVariable, sessionId);
                Environment.SetEnvironmentVariable(AccessKeyVariable, accessKey);
                return Win32Interop.CreateInteractiveSystemProcess(
                    commandLine,
                    targetSessionId: targetSessionId,
                    hiddenWindow: false,
                    out _);
            }
            finally
            {
                Environment.SetEnvironmentVariable(SessionIdVariable, null);
                Environment.SetEnvironmentVariable(AccessKeyVariable, null);
            }
        }
    }

    private Process StartAsCurrentUser(Dictionary<string, string?>? environment, params string?[] arguments)
    {
        var psi = new ProcessStartInfo(_rcBinaryPath) { UseShellExecute = false };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument ?? string.Empty);
        }
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                psi.Environment[key] = value;
            }
        }
        return Process.Start(psi) ?? throw new InvalidOperationException("Failed to start the remote control app.");
    }
}
