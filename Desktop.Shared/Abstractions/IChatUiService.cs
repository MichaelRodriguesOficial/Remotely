using Remotely.Shared.Models;
using Remotely.Shared.Models.Dtos;

namespace Remotely.Desktop.Shared.Abstractions;

public interface IChatUiService
{
    event EventHandler ChatWindowClosed;

    /// <summary>Mensagens do chat da sessão ainda não vistas pelo usuário.</summary>
    event EventHandler<int>? UnreadChanged;

    // Chat avulso (pelo card do painel, pelo agente).
    void ShowChatWindow(string organizationName, StreamWriter writer);
    Task ReceiveChat(ChatMessage chatMessage);

    // Chat da sessão de acesso (pelo canal da tela).
    Task ReceiveSessionMessage(ChatMessageDto message, string viewerName);
    void ReceiveSessionTyping(string senderName);
    void ReceiveSessionRead(string messageId);
    void OpenSessionChat();
}
