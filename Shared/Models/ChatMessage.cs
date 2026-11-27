using Remotely.Shared.Models.Dtos;

namespace Remotely.Shared.Models;

/// <summary>Chat avulso (pelo card do painel): vai do servidor ao agente e do agente ao app do usuário.</summary>
public class ChatMessage
{
    public ChatMessage() { }

    public ChatMessage(string senderName, string message, bool disconnected = false)
    {
        SenderName = senderName;
        Message = message;
        Disconnected = disconnected;
    }

    public bool Disconnected { get; set; }
    public string Message { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    /// <summary>Anexos guardados no servidor (FileId).</summary>
    public List<ChatAttachmentDto> Attachments { get; set; } = [];

    /// <summary>"message" (padrão), "typing" ou "read" (MessageId lida em <see cref="Message"/>).</summary>
    public string Kind { get; set; } = "message";

    /// <summary>Token para o app do usuário baixar e enviar anexos pelo servidor (vale algumas horas).</summary>
    public string? FileToken { get; set; }
}
