using System.Collections.Concurrent;

namespace Remotely.Shared.ViewModels;

public class ChatSession
{
    public ConcurrentQueue<ChatHistoryItem> ChatHistory { get; } = new();
    public string? DeviceId { get; set; }
    public string? DeviceName { get; set; }
    public string ExpandedClass => IsExpanded ? "expanded" : "";
    public bool IsExpanded { get; set; }

    public int MissedChats { get; set; }

    /// <summary>O usuário está digitando (até este momento).</summary>
    public DateTimeOffset? TypingUntil { get; set; }

    /// <summary>Última mensagem do usuário ainda sem confirmação de leitura.</summary>
    public string? UnreadIncomingId { get; set; }
    public string SessionId { get; } = Guid.NewGuid().ToString();
}
