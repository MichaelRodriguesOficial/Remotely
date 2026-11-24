using Remotely.Shared.Models.Diagnostics;

namespace Remotely.Server.Models;

/// <summary>Resultado mais recente de um teste num dispositivo.</summary>
public sealed record DiagnosticTestState(string Id, string Name, DiagnosticStatus Status, DateTimeOffset At, string? Summary);

/// <summary>Último reparo executado num dispositivo.</summary>
public sealed record DiagnosticRepairState(string Name, DiagnosticStatus Status, DateTimeOffset At, string? RequestedBy);

/// <summary>Situação de um dispositivo pelos diagnósticos mais recentes (menu Diagnóstico).</summary>
public sealed record DeviceDiagnosticSummary(
    DiagnosticStatus Status,
    DateTimeOffset LastRunAt,
    string? LastRequestedBy,
    IReadOnlyDictionary<string, DiagnosticTestState> Tests,
    DiagnosticRepairState? LastRepair,
    bool IsRunning)
{
    /// <summary>Testes com atenção ou problema, os problemas primeiro.</summary>
    public IReadOnlyList<DiagnosticTestState> Issues { get; } = Tests.Values
        .Where(x => x.Status is DiagnosticStatus.Warning or DiagnosticStatus.Error)
        .OrderByDescending(x => x.Status == DiagnosticStatus.Error)
        .ThenBy(x => x.Name)
        .ToList();

    /// <summary>Tem algum diagnóstico concluído (não só reparos ou execução em andamento).</summary>
    public bool HasResults => Tests.Count > 0;
}
