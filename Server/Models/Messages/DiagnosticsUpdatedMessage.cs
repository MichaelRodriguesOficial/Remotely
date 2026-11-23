namespace Remotely.Server.Models.Messages;

/// <summary>O agente enviou (parte de) um resultado de diagnóstico.</summary>
public record DiagnosticsUpdatedMessage(string DeviceId, string RunId);
