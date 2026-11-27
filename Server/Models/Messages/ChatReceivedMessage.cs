using Remotely.Shared.Models.Dtos;

namespace Remotely.Server.Models.Messages;

public record ChatReceivedMessage(
    string DeviceId,
    string DeviceName,
    string MessageText,
    bool DidDisconnect = false,
    string Kind = "message",
    string? MessageId = null,
    IReadOnlyList<ChatAttachmentDto>? Attachments = null,
    string? SenderName = null);
