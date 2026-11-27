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
    private string? _preview;
    private string? _typingPreview;
    private System.Threading.Timer? _typingTimer;
    private SessionChatState? _lastState;

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

    public event EventHandler<SessionChatState>? SessionChatChanged;

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
            WindowPlacement.KeepBottomRight(chatWindow);
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
            var sender = string.IsNullOrWhiteSpace(message.SenderName) ? viewerName : message.SenderName;
            _sessionViewModel!.AddIncoming(message, sender);

            if (!window.IsVisible)
            {
                // Aparece na frente, sem tirar o foco do que o usuário está digitando.
                ShowSessionWindow(window, activate: false);
            }

            if (window.IsActive)
            {
                _sessionViewModel.MarkRead();
                return;
            }

            window.Topmost = true;
            window.Topmost = false;
            _unread++;
            _preview = $"{sender}: {Preview(message)}";
            PublishState();
        });
    }

    public void ReceiveSessionTyping(string senderName)
    {
        _dispatcher.Post(() =>
        {
            _sessionViewModel?.ShowTyping(senderName);
            if (_sessionWindow?.IsVisible != true && _unread == 0)
            {
                _typingPreview = $"{(string.IsNullOrWhiteSpace(senderName) ? _sessionChat.TechnicianName : senderName)} está digitando...";
                PublishState();
                _typingTimer?.Dispose();
                _typingTimer = new System.Threading.Timer(_ => _dispatcher.Post(() =>
                {
                    _typingPreview = null;
                    PublishState();
                }), null, TimeSpan.FromSeconds(4.5), System.Threading.Timeout.InfiniteTimeSpan);
            }
        });
    }

    public void ReceiveSessionRead(string messageId)
    {
        _dispatcher.Post(() => _sessionViewModel?.MarkReadUpTo(messageId));
    }

    public void OpenSessionChat()
    {
        _dispatcher.Post(() => ShowSessionWindow(EnsureSessionWindow(_sessionChat.TechnicianName), activate: true));
    }

    public void ToggleSessionChat()
    {
        _dispatcher.Post(() =>
        {
            if (_sessionWindow?.IsVisible == true && _unread == 0)
            {
                _sessionWindow.Hide();
                PublishState();
                return;
            }
            ShowSessionWindow(EnsureSessionWindow(_sessionChat.TechnicianName), activate: true);
        });
    }

    private void ShowSessionWindow(ChatWindow window, bool activate)
    {
        if (!window.IsVisible)
        {
            PlaceAbovePanel(window);
        }
        window.ShowActivated = activate;
        window.Show();
        if (activate)
        {
            window.Activate();
            _unread = 0;
            _preview = null;
            _sessionViewModel?.MarkRead();
        }
        _typingPreview = null;
        PublishState();
    }

    /// <summary>Logo acima do painel da sessão (ou ao lado da aba, se ele estiver recolhido).</summary>
    private void PlaceAbovePanel(ChatWindow window)
    {
        var panel = _dispatcher.MainWindow;
        var screen = panel?.Screens.ScreenFromPoint(panel.Position) ?? window.Screens.Primary ?? window.Screens.All.FirstOrDefault();
        if (screen is null)
        {
            return;
        }

        var scaling = screen.Scaling > 0 ? screen.Scaling : 1;
        var width = (int)Math.Ceiling(window.Width * scaling);
        var height = (int)Math.Ceiling(window.Height * scaling);
        var gap = (int)Math.Round(12 * scaling);
        var area = screen.WorkingArea;

        int right;
        int bottom;
        if (panel is { IsVisible: true } && panel.FrameSize is { } panelSize)
        {
            var panelWidth = (int)Math.Ceiling(panelSize.Width * scaling);
            var panelHeight = (int)Math.Ceiling(panelSize.Height * scaling);
            var collapsed = panelWidth < 120 * scaling;
            right = collapsed ? panel.Position.X - gap / 2 : panel.Position.X + panelWidth - (int)Math.Round(8 * scaling);
            bottom = collapsed ? panel.Position.Y + panelHeight : panel.Position.Y;
        }
        else
        {
            right = area.Right - gap;
            bottom = area.Bottom - gap;
        }

        var x = Math.Clamp(right - width, area.X, Math.Max(area.X, area.Right - width));
        var y = Math.Clamp(bottom - height, area.Y, Math.Max(area.Y, area.Bottom - height));
        window.Position = new Avalonia.PixelPoint(x, y);
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
            _sessionViewModel?.MarkRead();
            if (_unread > 0 || _preview is not null)
            {
                _unread = 0;
                _preview = null;
                PublishState();
            }
        };
        // Fechar só esconde: a conversa continua enquanto a sessão durar.
        _sessionWindow.Closing += (_, e) =>
        {
            if (_sessionWindow is not null && !_sessionWindow.ForceClose)
            {
                e.Cancel = true;
                _sessionWindow.Hide();
                PublishState();
            }
        };
        return _sessionWindow;
    }

    private void PublishState()
    {
        var state = new SessionChatState(_sessionWindow?.IsVisible == true, _unread, _preview ?? _typingPreview);
        if (state == _lastState)
        {
            return;
        }
        _lastState = state;
        SessionChatChanged?.Invoke(this, state);
    }

    private static string Preview(ChatMessageDto message)
    {
        var text = (message.Text ?? string.Empty).ReplaceLineEndings(" ").Trim();
        if (text.Length > 0)
        {
            return text.Length > 80 ? $"{text[..80]}…" : text;
        }
        var count = message.Attachments?.Count ?? 0;
        if (count == 0)
        {
            return "mensagem";
        }
        return count == 1
            ? $"📎 {message.Attachments![0].FileName}"
            : $"📎 {count} anexos";
    }

    private void ChatWindow_Closing(object? sender, CancelEventArgs e)
    {
        ChatWindowClosed?.Invoke(this, e);
    }
}
