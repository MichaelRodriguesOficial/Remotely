using Remotely.Desktop.Shared.Abstractions;
using Remotely.Desktop.Shared.Services;
using System.Diagnostics;

namespace Remotely.Desktop.Win.Services;

/// <summary>
/// Evita várias cópias do app do modo autônomo na mesma sessão do Windows.
/// Cada cópia autônoma abre um evento nomeado só dela. Ao iniciar, a cópia nova sinaliza as mais antigas
/// da mesma sessão: as que não têm técnico conectado saem na hora (antes esperavam 30 s); as que ainda
/// atendem alguém continuam até ele sair. O app do modo assistido e o do chat não participam.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class InstanceCoordinator
{
    private const string Prefix = @"Local\Remotely_Desktop_Unattended_";

    public static void Start(IAppState appState, IShutdownService shutdownService, ILogger logger, CancellationToken cancellationToken)
    {
        using var current = Process.GetCurrentProcess();
        var myId = current.Id;
        var mySession = current.SessionId;
        var myStart = SafeStartTime(current) ?? DateTime.Now;

        EventWaitHandle signal;
        try
        {
            signal = new EventWaitHandle(false, EventResetMode.AutoReset, Prefix + myId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not create the instance coordination event.");
            return;
        }

        _ = Task.Run(async () =>
        {
            using (signal)
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!signal.WaitOne(TimeSpan.FromSeconds(1)))
                    {
                        continue;
                    }

                    if (appState.Viewers.IsEmpty)
                    {
                        logger.LogInformation("A newer unattended instance started in this session and no viewer is connected here. Shutting down.");
                        await shutdownService.Shutdown();
                        return;
                    }

                    logger.LogInformation("A newer unattended instance started, but this one still has viewers. Staying until they leave.");
                }
            }
        }, CancellationToken.None);

        foreach (var process in Process.GetProcessesByName(current.ProcessName))
        {
            using (process)
            {
                try
                {
                    if (process.Id == myId || process.SessionId != mySession)
                    {
                        continue;
                    }

                    if (SafeStartTime(process) is { } started && started > myStart)
                    {
                        continue;
                    }

                    if (EventWaitHandle.TryOpenExisting(Prefix + process.Id, out var older))
                    {
                        using (older)
                        {
                            older.Set();
                        }
                        logger.LogInformation("Asked older unattended instance {pid} to yield.", process.Id);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Could not signal process {pid}.", process.Id);
                }
            }
        }
    }

    private static DateTime? SafeStartTime(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch
        {
            return null;
        }
    }
}
