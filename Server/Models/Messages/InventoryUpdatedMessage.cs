namespace Remotely.Server.Models.Messages;

/// <summary>O agente enviou um inventário novo para o dispositivo.</summary>
public record InventoryUpdatedMessage(string DeviceId);
