using Remotely.Shared.Models.Diagnostics;

namespace Remotely.Server.Models;

/// <summary>Situação de um dispositivo pelos diagnósticos mais recentes (lista do Inventário).</summary>
public sealed record DeviceDiagnosticSummary(
    DiagnosticStatus Status,
    DateTimeOffset LastRunAt,
    IReadOnlyList<(string Name, DiagnosticStatus Status)> Issues);
