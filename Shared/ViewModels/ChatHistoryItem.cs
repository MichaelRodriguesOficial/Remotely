using Remotely.Shared.Enums;
using Remotely.Shared.Models.Dtos;

namespace Remotely.Shared.ViewModels;

public class ChatHistoryItem
{
    public ChatHistoryItemOrigin Origin { get; init; }
    public string? Message { get; init; }

    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string? SenderName { get; init; }

    public IReadOnlyList<ChatAttachmentDto> Attachments { get; init; } = [];

    /// <summary>Mensagem do técnico já lida pelo usuário.</summary>
    public bool IsRead { get; set; }

    public DateTimeOffset Timestamp { get; init; } = DateTime.Now;

    public string ClassName
    {
        get
        {
            switch (Origin)
            {
                case ChatHistoryItemOrigin.System:
                    return "chat-history-item-system";
                case ChatHistoryItemOrigin.Self:
                    return "chat-history-item-self";
                case ChatHistoryItemOrigin.Device:
                    return "chat-history-item-device";
                default:
                    return string.Empty;
            }
        }
    }
}
