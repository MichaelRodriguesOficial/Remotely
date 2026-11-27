using Microsoft.Extensions.Logging;
using Remotely.Shared.Models.Dtos;

namespace Remotely.Desktop.Shared.Services;

/// <summary>Chat da sessão de acesso: envia ao(s) técnico(s) conectado(s) pelo canal da tela.</summary>
public interface ISessionChat
{
    bool HasViewers { get; }

    /// <summary>Nome do técnico (primeiro conectado).</summary>
    string TechnicianName { get; }

    Task<bool> Send(ChatMessageDto message);
    Task SendTyping(string senderName);
    Task SendRead(string messageId);
}

public class SessionChat : ISessionChat
{
    private readonly IAppState _appState;
    private readonly ILogger<SessionChat> _logger;

    public SessionChat(IAppState appState, ILogger<SessionChat> logger)
    {
        _appState = appState;
        _logger = logger;
    }

    public bool HasViewers => !_appState.Viewers.IsEmpty;

    public string TechnicianName =>
        _appState.Viewers.Values.Select(x => x.Name).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ??
        (string.IsNullOrWhiteSpace(_appState.RequesterName) ? "Suporte de TI" : _appState.RequesterName);

    public async Task<bool> Send(ChatMessageDto message)
    {
        var sent = false;
        foreach (var viewer in _appState.Viewers.Values.ToList())
        {
            try
            {
                await viewer.SendDto(message, DtoType.ChatMessage);
                sent = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not send chat message to viewer {viewer}.", viewer.ViewerConnectionId);
            }
        }
        return sent;
    }

    public Task SendTyping(string senderName) =>
        SendSignal(new ChatSignalDto { SenderName = senderName }, DtoType.ChatTyping);

    public Task SendRead(string messageId) =>
        SendSignal(new ChatSignalDto { MessageId = messageId }, DtoType.ChatRead);

    private async Task SendSignal(ChatSignalDto dto, DtoType type)
    {
        foreach (var viewer in _appState.Viewers.Values.ToList())
        {
            try
            {
                await viewer.SendDto(dto, type);
            }
            catch
            {
                // Sinal opcional.
            }
        }
    }
}
