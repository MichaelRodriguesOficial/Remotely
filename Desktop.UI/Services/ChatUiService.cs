using Microsoft.Extensions.Logging;
using Remotely.Desktop.Shared.Abstractions;
using Remotely.Desktop.Shared.Services;
using Remotely.Desktop.UI.Controls.Dialogs;
using Remotely.Shared.Models;
using Remotely.Shared.Models.Dtos;
using System.ComponentModel;

namespace Remotely.Desktop.UI.Services;

public class ChatUiService : IChatUiService
{
    private readonly IUiDispatcher _dispatcher;
    private readonly IViewModelFactory _viewModelFactory;
    private readonly ISessionChat _sessionChat;
    private readonly ILogger<ChatUiService> _logger;

    private ChatWindowViewModel? _pipeViewModel;
    private PipeChatChannel? _pipeChannel;
    private ChatWindow? _sessionWindow;
    private ChatWindowViewModel? _sessionViewModel;
    private int _unread;

    public ChatUiService(
        IUiDispatcher dispatcher,
        IDialogProvider dialogProvider,
        IViewModelFactory viewModelFactory,
        ISessionChat sessionChat,
        ILogger<ChatUiService> logger)
    {
        _dispatcher = dispatcher;
        _viewModelFactory = viewModelFactory;
        _sessionChat = sessionChat;
        _logger = logger;
    }

    public event EventHandler? ChatWindowClosed;

    public event EventHandler<int>? UnreadChanged;

    // ---------- Chat avulso ----------

    public void ShowChatWindow(string organizationName, StreamWriter writer)
    {
        _dispatcher.Post(() =>
        {
            (_pipeViewModel, _pipeChannel) = _viewModelFactory.CreatePipeChatViewModel(organizationName, writer);
            var chatWindow = new ChatWindow
            {
                DataContext = _pipeViewModel,
                ExitOnClose = true
            };
            chatWindow.Closing += ChatWindow_Closing;
            _dispatcher.ShowMainWindow(chatWindow);
        });
    }

    public async Task ReceiveChat(ChatMessage chatMessage)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            if (_pipeViewModel is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(chatMessage.FileToken) && _pipeChannel is not null)
            {
                _pipeChannel.FileToken = chatMessage.FileToken;
            }

            if (chatMessage.Disconnected)
            {
                _pipeViewModel.AddSystem("O técnico saiu do chat. Você pode fechar esta janela.");
                return;
            }

            switch (chatMessage.Kind)
            {
                case "typing":
                    _pipeViewModel.ShowTyping(chatMessage.SenderName);
                    break;
                case "read":
                    _pipeViewModel.MarkReadUpTo(chatMessage.Message);
                    break;
                default:
                    _pipeViewModel.AddIncoming(new ChatMessageDto
                    {
                        Id = chatMessage.Id,
                        SenderName = chatMessage.SenderName,
                        Text = chatMessage.Message,
                        Timestamp = chatMessage.Timestamp.ToString("O"),
                        Attachments = chatMessage.Attachments ?? []
                    }, chatMessage.SenderName);
                    break;
            }
        });
    }

    // ---------- Chat da sessão ----------

    public async Task ReceiveSessionMessage(ChatMessageDto message, string viewerName)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            var window = EnsureSessionWindow(viewerName);
            _sessionViewModel!.AddIncoming(message, string.IsNullOrWhiteSpace(message.SenderName) ? viewerName : message.SenderName);

            if (!window.IsVisible)
            {
                // Aparece na frente, sem tirar o foco do que o usuário está digitando.
                window.ShowActivated = false;
                window.Show();
            }

            if (window.IsActive)
            {
                _sessionViewModel.MarkRead();
            }
            else
            {
                window.Topmost = true;
                window.Topmost = false;
                SetUnread(_unread + 1);
            }
        });
    }

    public void ReceiveSessionTyping(string senderName)
    {
        _dispatcher.Post(() => _sessionViewModel?.ShowTyping(senderName));
    }

    public void ReceiveSessionRead(string messageId)
    {
        _dispatcher.Post(() => _sessionViewModel?.MarkReadUpTo(messageId));
    }

    public void OpenSessionChat()
    {
        _dispatcher.Post(() =>
        {
            var window = EnsureSessionWindow(_sessionChat.TechnicianName);
            window.ShowActivated = true;
            window.Show();
            window.Activate();
            SetUnread(0);
            _sessionViewModel?.MarkRead();
        });
    }

    private ChatWindow EnsureSessionWindow(string technicianName)
    {
        if (_sessionWindow is not null && _sessionViewModel is not null)
        {
            _sessionViewModel.SetPeer(technicianName);
            return _sessionWindow;
        }

        _sessionViewModel = _viewModelFactory.CreateSessionChatViewModel(technicianName);
        _sessionWindow = new ChatWindow
        {
            DataContext = _sessionViewModel,
            ExitOnClose = false
        };
        _sessionWindow.Activated += (_, _) =>
        {
            SetUnread(0);
            _sessionViewModel?.MarkRead();
        };
        // Fechar só esconde: a conversa continua enquanto a sessão durar.
        _sessionWindow.Closing += (_, e) =>
        {
            if (_sessionWindow is not null && !_sessionWindow.ForceClose)
            {
                e.Cancel = true;
                _sessionWindow.Hide();
            }
        };
        return _sessionWindow;
    }

    private void SetUnread(int count)
    {
        if (_unread == count)
        {
            return;
        }
        _unread = count;
        UnreadChanged?.Invoke(this, count);
    }

    private void ChatWindow_Closing(object? sender, CancelEventArgs e)
    {
        ChatWindowClosed?.Invoke(this, e);
    }
}
