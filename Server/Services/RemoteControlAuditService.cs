using Microsoft.EntityFrameworkCore;
using Remotely.Server.Data;
using Remotely.Server.Models;
using Remotely.Shared.Entities;
using System.Collections.Concurrent;

namespace Remotely.Server.Services;

/// <summary>Filtro da tela de histórico de sessões.</summary>
public sealed record RemoteSessionQuery(
    string OrganizationId,
    DateTimeOffset From,
    string? RequesterUserName = null,
    string? Search = null,
    int Limit = 1000);

/// <summary>
/// Registra o início e o fim de cada sessão de controle remoto (histórico de sessões).
/// Guarda o ID do registro por conexão do visualizador para fechá-lo com o motivo certo.
/// </summary>
public interface IRemoteControlAuditService
{
    /// <summary>Abre o registro da sessão. Retorna false se esta conexão já tinha um registro aberto (reconexão).</summary>
    Task<bool> SessionStarted(string viewerConnectionId, RemoteControlSession session, string? requesterName, string? viewerIp);

    /// <summary>Fecha o registro da conexão. Só o primeiro motivo vale.</summary>
    Task SessionEnded(string viewerConnectionId, string reason);

    Task<List<RemoteControlSessionLog>> GetSessions(RemoteSessionQuery query);

    /// <summary>Remove registros mais antigos que o limite (padrão: 1 ano).</summary>
    Task<int> Prune(TimeSpan keep);

    /// <summary>
    /// Na subida do servidor: sessões que ficaram abertas (o servidor parou no meio) recebem o motivo
    /// "Servidor reiniciado" e deixam de aparecer como em andamento.
    /// </summary>
    Task<int> CloseOrphans();
}

public class RemoteControlAuditService : IRemoteControlAuditService
{
    public const string EndedByTechnician = "Encerrada pelo técnico";
    public const string BrowserClosed = "Navegador fechado";
    public const string ConnectionLost = "Conexão perdida";
    public const string EndedOnComputer = "Encerrada no computador";
    public const string DeniedByUser = "Recusada pelo usuário";
    public const string ServerRestarted = "Servidor reiniciado";
    public const string IdleTimeout = "Encerrada por inatividade";

    private readonly ConcurrentDictionary<string, string> _openLogs = new();
    private readonly IAppDbFactory _dbFactory;
    private readonly ILogger<RemoteControlAuditService> _logger;

    public RemoteControlAuditService(IAppDbFactory dbFactory, ILogger<RemoteControlAuditService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<bool> SessionStarted(string viewerConnectionId, RemoteControlSession session, string? requesterName, string? viewerIp)
    {
        try
        {
            if (_openLogs.ContainsKey(viewerConnectionId))
            {
                return false;
            }

            using var db = _dbFactory.GetContext();
            string? deviceName = string.IsNullOrWhiteSpace(session.MachineName) ? null : session.MachineName;
            if (!string.IsNullOrWhiteSpace(session.DeviceId))
            {
                deviceName = await db.Devices
                    .Where(x => x.ID == session.DeviceId)
                    .Select(x => x.DeviceName)
                    .FirstOrDefaultAsync() ?? deviceName;
            }

            var log = new RemoteControlSessionLog
            {
                OrganizationID = session.OrganizationId,
                DeviceID = string.IsNullOrWhiteSpace(session.DeviceId) ? null : session.DeviceId,
                DeviceName = Truncate(deviceName, 256),
                RequesterUserName = Truncate(session.RequesterUserName, 256),
                RequesterName = Truncate(requesterName, 256),
                Mode = session.Mode.ToString(),
                ViewOnly = session.ViewOnly,
                ViewerIp = Truncate(viewerIp, 64),
                StartedAt = DateTimeOffset.Now
            };

            db.RemoteControlSessionLogs.Add(log);
            await db.SaveChangesAsync();
            _openLogs[viewerConnectionId] = log.ID;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while recording the start of a remote control session.");
            return true;
        }
    }

    public async Task SessionEnded(string viewerConnectionId, string reason)
    {
        if (!_openLogs.TryRemove(viewerConnectionId, out var logId))
        {
            return;
        }

        try
        {
            using var db = _dbFactory.GetContext();
            var log = await db.RemoteControlSessionLogs.FirstOrDefaultAsync(x => x.ID == logId);
            if (log is null || log.EndedAt is not null || log.EndReason is not null)
            {
                return;
            }

            log.EndedAt = DateTimeOffset.Now;
            log.EndReason = Truncate(reason, 100);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while recording the end of a remote control session.");
        }
    }

    public async Task<List<RemoteControlSessionLog>> GetSessions(RemoteSessionQuery query)
    {
        using var db = _dbFactory.GetContext();
        var logs = db.RemoteControlSessionLogs
            .AsNoTracking()
            .Where(x => x.OrganizationID == query.OrganizationId);

        if (!string.IsNullOrWhiteSpace(query.RequesterUserName))
        {
            logs = logs.Where(x => x.RequesterUserName == query.RequesterUserName);
        }

        // A data é filtrada em memória: no SQLite ela é guardada como texto.
        var list = await logs.ToListAsync();
        var search = query.Search?.Trim();

        return list
            .Where(x => x.StartedAt >= query.From)
            .Where(x => string.IsNullOrWhiteSpace(search) ||
                Contains(x.DeviceName, search) ||
                Contains(x.RequesterName, search) ||
                Contains(x.RequesterUserName, search) ||
                Contains(x.ViewerIp, search))
            .OrderByDescending(x => x.StartedAt)
            .Take(query.Limit)
            .ToList();
    }

    public async Task<int> Prune(TimeSpan keep)
    {
        using var db = _dbFactory.GetContext();
        var cutoff = DateTimeOffset.Now - keep;
        var old = (await db.RemoteControlSessionLogs.ToListAsync())
            .Where(x => x.StartedAt < cutoff)
            .ToList();
        if (old.Count == 0)
        {
            return 0;
        }

        db.RemoteControlSessionLogs.RemoveRange(old);
        await db.SaveChangesAsync();
        return old.Count;
    }

    public async Task<int> CloseOrphans()
    {
        using var db = _dbFactory.GetContext();
        var open = await db.RemoteControlSessionLogs
            .Where(x => x.EndedAt == null && x.EndReason == null)
            .ToListAsync();

        var orphans = open.Where(x => !_openLogs.Values.Contains(x.ID)).ToList();
        foreach (var log in orphans)
        {
            log.EndReason = ServerRestarted;
        }

        if (orphans.Count > 0)
        {
            await db.SaveChangesAsync();
        }
        return orphans.Count;
    }

    private static bool Contains(string? value, string search) =>
        value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
