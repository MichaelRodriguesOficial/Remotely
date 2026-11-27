using System.Collections.Concurrent;

namespace Remotely.Server.Services;

/// <summary>
/// O que cada agente conectado sabe fazer (enviado em DeviceCameOnline). Recursos novos só são usados
/// com agentes que avisam que os têm; agentes antigos continuam com o caminho antigo.
/// </summary>
public static class AgentFeatures
{
    public const string Chat2 = "chat2";

    private static readonly ConcurrentDictionary<string, string[]> _features = new();

    public static void Set(string deviceId, string[]? features)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return;
        }
        _features[deviceId] = features ?? [];
    }

    public static bool Has(string deviceId, string feature) =>
        _features.TryGetValue(deviceId, out var features) && features.Contains(feature, StringComparer.OrdinalIgnoreCase);
}
