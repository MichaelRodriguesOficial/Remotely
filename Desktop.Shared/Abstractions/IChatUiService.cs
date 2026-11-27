using Remotely.Shared.Models;
using Remotely.Shared.Models.Dtos;

namespace Remotely.Desktop.Shared.Abstractions;

public interface IChatUiService
{
    event EventHandler ChatWindowClosed;

    /// <summary>Chat da sessão aberto ou não, mensagens não vistas e a última delas: o painel da sessão mostra.</summary>
    event EventHandler<SessionChatState>? SessionChatChanged;

    // Chat avulso (pelo card do painel, pelo agente).
    void ShowChatWindow(string organizationName, StreamWriter writer);
    Task ReceiveChat(ChatMessage chatMessage);

    // Chat da sessão de acesso (pelo canal da tela).
    Task ReceiveSessionMessage(ChatMessageDto message, string viewerName);
    void ReceiveSessionTyping(string senderName);
    void ReceiveSessionRead(string messageId);
    void OpenSessionChat();
    void ToggleSessionChat();
}

/// <summary>O que o painel da sessão precisa saber do chat.</summary>
/// <param name="IsOpen">A janela do chat está na tela.</param>
/// <param name="Unread">Mensagens do técnico que o usuário ainda não viu.</param>
/// <param name="Preview">"Fulano: texto" da última não vista, ou "Fulano está digitando...".</param>
public record SessionChatState(bool IsOpen, int Unread, string? Preview);
