using Remotely.Shared.Models.Diagnostics;

namespace Remotely.Server.Services;

/// <summary>
/// Quando uma execução de diagnóstico ou reparo ainda conta como "em andamento".
/// Usado pela lista, pela aba do dispositivo e para bloquear execuções simultâneas.
/// </summary>
public static class DiagnosticRunRules
{
    /// <param name="tests">IDs pedidos (coluna Tests).</param>
    /// <param name="hasData">O agente já respondeu alguma vez.</param>
    /// <param name="superseded">Existe uma execução mais nova no mesmo dispositivo.</param>
    public static bool IsActive(string tests, bool hasData, DateTimeOffset startedAt, DateTimeOffset? finishedAt, bool superseded, DateTimeOffset now)
    {
        if (finishedAt is not null || superseded)
        {
            return false;
        }

        var elapsed = now - startedAt;

        // O agente responde em segundos; sem resposta nenhuma é agente desligado ou antigo.
        if (!hasData)
        {
            return elapsed < (DiagnosticRepairs.IsRepairRun(tests) ? TimeSpan.FromMinutes(3) : TimeSpan.FromMinutes(5));
        }

        if (DiagnosticRepairs.IsRepairRun(tests))
        {
            return elapsed < TimeSpan.FromMinutes(100);
        }

        // Com teste de memória a execução atravessa o reinício.
        return elapsed < (tests.Contains(DiagnosticTests.MemoryTest) ? TimeSpan.FromHours(14) : TimeSpan.FromMinutes(20));
    }
}
