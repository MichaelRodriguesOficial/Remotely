using Remotely.Shared.Models.Diagnostics;
using System;
using System.IO;
using System.Text.Json;

namespace Remotely.Agent.Services.Diagnostics;

/// <summary>
/// Guarda em disco o diagnóstico que agendou o teste de memória, para o agente completar
/// a mesma execução com o resultado quando o computador voltar do reinício.
/// </summary>
internal static class MemoryTestMarker
{
    private sealed record Marker(DateTimeOffset ScheduledAt, string ReportJson);

    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "memory-test-pending.json");

    public static void Save(DiagnosticReport report, DateTimeOffset scheduledAt)
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new Marker(scheduledAt, DiagnosticSerializer.ToJson(report))));
        }
        catch
        {
            // Sem o marcador, o resultado só aparece rodando o teste Sistema de novo.
        }
    }

    public static (DiagnosticReport Report, DateTimeOffset ScheduledAt)? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            var marker = JsonSerializer.Deserialize<Marker>(File.ReadAllText(FilePath));
            var report = DiagnosticSerializer.FromJson(marker?.ReportJson);
            return marker is null || report is null ? null : (report, marker.ScheduledAt);
        }
        catch
        {
            Delete();
            return null;
        }
    }

    public static void Delete()
    {
        try
        {
            File.Delete(FilePath);
        }
        catch
        {
            // Já apagado.
        }
    }
}
