namespace Remotely.Server.Models.Messages;

/// <summary>Resultado do "Enviar arquivo" informado pelo agente. Detail traz os caminhos salvos (um por linha) ou o erro.</summary>
public record FileTransferResultMessage(string DeviceId, string TransferId, bool Success, string Detail);
