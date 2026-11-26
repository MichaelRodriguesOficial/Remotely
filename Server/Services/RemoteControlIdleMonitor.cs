using Microsoft.AspNetCore.SignalR;
using Remotely.Server.Hubs;
using Remotely.Shared.Interfaces;

namespace Remotely.Server.Services;

/// <summary>
/// Encerra sessões de controle remoto sem mouse ou teclado do técnico pelo tempo configurado
/// (Configuração → Acesso remoto), com aviso 1 minuto antes. Sessões só de visualização não contam.
/// </summary>
public class RemoteControlIdleMonitor : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan WarningBefore = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<ViewerHub, IViewerHubClient> _viewerHub;
    private readonly IHubContext<DesktopHub, IDesktopHubClient> _desktopHub;
    private readonly IRemoteControlAuditService _audit;
    private readonly ILogger<RemoteControlIdleMonitor> _logger;

    public RemoteControlIdleMonitor(
        IServiceScopeFactory scopeFactory,
        IHubContext<ViewerHub, IViewerHubClient> viewerHub,
        IHubContext<DesktopHub, IDesktopHubClient> desktopHub,
        IRemoteControlAuditService audit,
        ILogger<RemoteControlIdleMonitor> logger)
    {
        _scopeFactory = scopeFactory;
        _viewerHub = viewerHub;
        _desktopHub = desktopHub;
        _audit = audit;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await CheckOnce();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while checking idle remote control sessions.");
            }
        }
    }

    internal async Task CheckOnce()
    {
        var viewers = ViewerHub.ActiveViewers;
        if (viewers.Count == 0)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var settings = await scope.ServiceProvider.GetRequiredService<IDataService>().GetSettings();
        if (settings.RemoteControlIdleMinutes <= 0)
        {
            return;
        }

        var timeout = TimeSpan.FromMinutes(settings.RemoteControlIdleMinutes);
        var now = DateTimeOffset.Now;

        foreach (var context in viewers)
        {
            if (ViewerHub.IsViewOnly(context) ||
                !context.Items.TryGetValue(ViewerHub.LastInputKey, out var value) ||
                value is not DateTimeOffset lastInput)
            {
                continue;
            }

            var idle = now - lastInput;
            if (idle >= timeout)
            {
                _logger.LogInformation("Ending idle remote control session {connectionId} after {minutes} min.", context.ConnectionId, (int)idle.TotalMinutes);
                await _viewerHub.Clients.Client(context.ConnectionId)
                    .ShowMessage($"Sessão encerrada por inatividade ({settings.RemoteControlIdleMinutes} min sem uso).");
                await ViewerHub.EndFromServer(context, _desktopHub, _audit, RemoteControlAuditService.IdleTimeout);
            }
            else if (idle >= timeout - WarningBefore && !context.Items.ContainsKey(ViewerHub.IdleWarnedKey))
            {
                context.Items[ViewerHub.IdleWarnedKey] = true;
                await _viewerHub.Clients.Client(context.ConnectionId)
                    .ShowMessage("Sessão parada: será encerrada em 1 minuto se o mouse ou o teclado não forem usados.");
            }
        }
    }
}
