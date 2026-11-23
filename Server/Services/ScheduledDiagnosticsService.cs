using Microsoft.AspNetCore.SignalR;
using Remotely.Server.Hubs;
using Remotely.Shared.Interfaces;
using Remotely.Shared.Models.Diagnostics;

namespace Remotely.Server.Services;

/// <summary>
/// Roda os testes rápidos de diagnóstico sozinho nos dispositivos online, conforme
/// "Diagnóstico agendado" em Configuração do servidor. Os testes pesados nunca rodam sozinhos.
/// </summary>
public class ScheduledDiagnosticsService : BackgroundService
{
    /// <summary>Quantos dispositivos por rodada, para espalhar a carga.</summary>
    private const int MaxPerTick = 25;

    public const string RequestedBy = "Diagnóstico agendado";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAgentHubSessionCache _sessionCache;
    private readonly IHubContext<AgentHub, IAgentHubClient> _agentHub;
    private readonly ILogger<ScheduledDiagnosticsService> _logger;

    public ScheduledDiagnosticsService(
        IServiceScopeFactory scopeFactory,
        IAgentHubSessionCache sessionCache,
        IHubContext<AgentHub, IAgentHubClient> agentHub,
        ILogger<ScheduledDiagnosticsService> logger)
    {
        _scopeFactory = scopeFactory;
        _sessionCache = sessionCache;
        _agentHub = agentHub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Dá tempo dos agentes reconectarem depois que o servidor sobe.
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
        do
        {
            try
            {
                await RunOnce(DateTime.Now);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while starting scheduled diagnostics.");
            }
        }
        while (await WaitNext(timer, stoppingToken));
    }

    private static async Task<bool> WaitNext(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public static bool IsInWindow(DateTime now, bool businessHoursOnly) =>
        !businessHoursOnly ||
        (now.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && now.Hour is >= 8 and < 18);

    public async Task<int> RunOnce(DateTime now)
    {
        using var scope = _scopeFactory.CreateScope();
        var dataService = scope.ServiceProvider.GetRequiredService<IDataService>();

        var settings = await dataService.GetSettings();
        if (!settings.ScheduledDiagnosticsEnabled || !IsInWindow(now, settings.ScheduledDiagnosticsBusinessHoursOnly))
        {
            return 0;
        }

        var interval = TimeSpan.FromDays(Math.Clamp(settings.ScheduledDiagnosticsIntervalDays, 1, 30));
        var online = _sessionCache.GetAllDevices()
            .Where(x => !string.IsNullOrWhiteSpace(x.AgentVersion))
            .ToList();
        if (online.Count == 0)
        {
            return 0;
        }

        var lastRuns = await dataService.GetLastDiagnosticTimes(online.Select(x => x.ID).ToList());
        var due = online
            .Where(x => !lastRuns.TryGetValue(x.ID, out var last) || DateTimeOffset.Now - last >= interval)
            .OrderBy(x => lastRuns.TryGetValue(x.ID, out var last) ? last : DateTimeOffset.MinValue)
            .Take(MaxPerTick)
            .ToList();

        var tests = DiagnosticTests.All.Where(x => !x.Heavy).Select(x => x.Id).ToArray();
        var started = 0;
        foreach (var device in due)
        {
            if (!_sessionCache.TryGetConnectionId(device.ID, out var connectionId))
            {
                continue;
            }

            var run = await dataService.CreateDiagnosticRun(device.ID, RequestedBy, tests);
            await _agentHub.Clients.Client(connectionId).RunDiagnostics(run.ID, tests);
            started++;
        }

        if (started > 0)
        {
            _logger.LogInformation("Scheduled diagnostics started on {count} device(s).", started);
        }
        return started;
    }
}
