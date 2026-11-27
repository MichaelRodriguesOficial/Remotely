using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Remotely.Server.Hubs;
using Remotely.Server.Models.Messages;
using Remotely.Server.Services;
using Remotely.Server.Services.Stores;
using Remotely.Shared.Enums;
using Remotely.Shared.Models.Dtos;
using Remotely.Shared.ViewModels;

namespace Remotely.Server.Components.Devices;

public partial class ChatCard : AuthComponentBase
{
    private static readonly string[] Emojis =
    [
        "😀", "😄", "😁", "😅", "😂", "🙂", "😉", "😊", "😍", "😎", "🤔", "😐",
        "🙄", "😬", "😢", "😭", "😡", "😱", "🥳", "🤗", "😇", "👍", "👎", "👌",
        "🙏", "👏", "🙌", "👋", "💪", "🤝", "❤️", "🔥", "✨", "💯", "✅", "❌",
        "⚠️", "❓", "💡", "🎉", "☕", "💻", "🖥️", "🖨️", "📱", "🔌", "🔒", "🔑",
        "🛠️", "📄", "📊", "📎", "📁", "📅", "⏰", "📧", "📝", "🔍", "💬", "⏳"
    ];

    private readonly List<ChatAttachmentDto> _pending = [];
    private readonly string _fileInputId = $"chat-file-{Guid.NewGuid():N}";
    private ElementReference _chatMessagesWindow;
    private string? _inputText;
    private bool _showEmojis;
    private bool _uploading;
    private DateTimeOffset _lastTypingSent;

    [Parameter]
    [EditorRequired]
    public required ChatSession Session { get; set; }

    [Inject]
    private IChatSessionStore ChatSessionStore { get; init; } = null!;

    [Inject]
    private ICircuitConnection CircuitConnection { get; init; } = null!;

    [Inject]
    private IJsInterop JsInterop { get; init; } = null!;

    [Inject]
    private IDataService DataService { get; init; } = null!;

    [Inject]
    private IToastService ToastService { get; init; } = null!;

    private bool IsTyping => Session.TypingUntil > DateTimeOffset.Now;

    private string Initials
    {
        get
        {
            var name = Session.DeviceName ?? "PC";
            return name.Length >= 2 ? name[..2].ToUpperInvariant() : name.ToUpperInvariant();
        }
    }

    protected override void OnAfterRender(bool firstRender)
    {
        JsInterop.ScrollToEnd(_chatMessagesWindow);
        base.OnAfterRender(firstRender);
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        await Register<ChatReceivedMessage, string>(
            CircuitConnection.ConnectionId,
            HandleChatMessageReceived);
    }

    private async Task HandleChatMessageReceived(object subscriber, ChatReceivedMessage message)
    {
        if (message.DeviceId != Session.DeviceId)
        {
            return;
        }

        if (!ChatSessionStore.TryGetSession(message.DeviceId, out var session))
        {
            return;
        }

        switch (message.Kind)
        {
            case "typing":
                session.TypingUntil = DateTimeOffset.Now.AddSeconds(4.5);
                await InvokeAsync(StateHasChanged);
                _ = Task.Delay(4700).ContinueWith(_ => InvokeAsync(StateHasChanged));
                return;
            case "read":
                foreach (var item in session.ChatHistory.Where(x => x.Origin == ChatHistoryItemOrigin.Self))
                {
                    item.IsRead = true;
                }
                await InvokeAsync(StateHasChanged);
                return;
        }

        session.TypingUntil = null;
        if (message.DidDisconnect)
        {
            session.ChatHistory.Enqueue(new ChatHistoryItem
            {
                Message = $"{Session.DeviceName}: o usuário fechou o chat.",
                Origin = ChatHistoryItemOrigin.System
            });
        }
        else
        {
            session.ChatHistory.Enqueue(new ChatHistoryItem
            {
                Id = message.MessageId ?? Guid.NewGuid().ToString("N"),
                Message = message.MessageText,
                SenderName = message.SenderName,
                Attachments = message.Attachments ?? [],
                Origin = ChatHistoryItemOrigin.Device
            });

            if (session.IsExpanded)
            {
                await SendRead(message.MessageId);
            }
            else
            {
                session.UnreadIncomingId = message.MessageId;
                session.MissedChats++;
            }
        }

        await InvokeAsync(StateHasChanged);
        JsInterop.ScrollToEnd(_chatMessagesWindow);
    }

    private async Task SendRead(string? messageId)
    {
        if (!string.IsNullOrWhiteSpace(messageId) && Session.DeviceId is { } deviceId)
        {
            await CircuitConnection.SendChat(deviceId, string.Empty, [], "read", messageId);
        }
    }

    private async Task OnFilesSelected(InputFileChangeEventArgs args)
    {
        EnsureUserSet();
        var files = args.GetMultipleFiles(ChatLimits.MaxAttachmentsPerMessage);
        _uploading = true;
        try
        {
            foreach (var file in files)
            {
                if (_pending.Count >= ChatLimits.MaxAttachmentsPerMessage)
                {
                    break;
                }
                if (file.Size > ChatLimits.MaxAttachmentBytes)
                {
                    ToastService.ShowToast2($"{file.Name} passa de 25 MB.", Enums.ToastType.Warning);
                    continue;
                }

                var fileId = await DataService.AddSharedFile(file, User.OrganizationID, (_, _) => { });
                _pending.Add(new ChatAttachmentDto
                {
                    FileId = fileId,
                    FileName = file.Name,
                    ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? ChatLimits.ContentTypeFor(file.Name) : file.ContentType,
                    Size = file.Size
                });
            }
        }
        catch (Exception)
        {
            ToastService.ShowToast2("Não foi possível anexar o arquivo.", Enums.ToastType.Warning);
        }
        finally
        {
            _uploading = false;
        }
    }

    private void InsertEmoji(string emoji)
    {
        _inputText = $"{_inputText}{emoji}";
        _showEmojis = false;
    }

    private async Task OnTyping(ChangeEventArgs args)
    {
        _inputText = args.Value?.ToString();
        if (DateTimeOffset.Now - _lastTypingSent < TimeSpan.FromSeconds(3) ||
            string.IsNullOrWhiteSpace(_inputText) ||
            Session.DeviceId is not { } deviceId)
        {
            return;
        }
        _lastTypingSent = DateTimeOffset.Now;
        await CircuitConnection.SendChat(deviceId, string.Empty, [], "typing");
    }

    private async Task EvaluateInputKeydown(KeyboardEventArgs args)
    {
        if (args.Key == "Enter")
        {
            await SendMessage();
        }
    }

    private async Task SendMessage()
    {
        var text = (_inputText ?? string.Empty).Trim();
        if ((string.IsNullOrWhiteSpace(text) && _pending.Count == 0) || Session.DeviceId is not { } deviceId)
        {
            return;
        }
        if (text.Length > ChatLimits.MaxTextLength)
        {
            text = text[..ChatLimits.MaxTextLength];
        }

        var attachments = _pending.ToList();
        var item = new ChatHistoryItem
        {
            Origin = ChatHistoryItemOrigin.Self,
            Message = text,
            Attachments = attachments
        };

        var sent = await CircuitConnection.SendChat(deviceId, text, attachments, "message", item.Id);
        if (!sent)
        {
            ToastService.ShowToast2("O computador não está conectado.", Enums.ToastType.Warning);
            return;
        }

        Session.ChatHistory.Enqueue(item);
        _inputText = string.Empty;
        _pending.Clear();
        _showEmojis = false;
        JsInterop.ScrollToEnd(_chatMessagesWindow);
    }

    private async Task CloseChatCard()
    {
        await CircuitConnection.SendChat(string.Empty, $"{Session.DeviceId}", true);
        _ = ChatSessionStore.TryRemove($"{Session.DeviceId}", out _);
        var message = new ChatSessionsChangedMessage();
        await Messenger.Send(message, CircuitConnection.ConnectionId);
    }

    private async Task HeaderClicked()
    {
        Session.IsExpanded = !Session.IsExpanded;
        if (Session.IsExpanded)
        {
            Session.MissedChats = 0;
            var unread = Session.UnreadIncomingId;
            Session.UnreadIncomingId = null;
            await SendRead(unread);
        }
    }

    private static string? FileUrl(ChatAttachmentDto file) =>
        string.IsNullOrWhiteSpace(file.FileId) ? null : $"/api/chat-files/{Uri.EscapeDataString(file.FileId)}";

    private static bool IsOnlyEmoji(string text)
    {
        var trimmed = string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
        return trimmed.Length is > 0 and <= 16 &&
            !trimmed.Any(c => char.IsLetterOrDigit(c) || char.IsPunctuation(c)) &&
            trimmed.Any(char.IsSurrogate);
    }

    private static string FileIcon(string name) => Path.GetExtension(name ?? string.Empty).ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" => "fa-file-image",
        ".pdf" => "fa-file-pdf",
        ".doc" or ".docx" or ".odt" or ".rtf" => "fa-file-word",
        ".xls" or ".xlsx" or ".csv" or ".ods" => "fa-file-excel",
        ".ppt" or ".pptx" => "fa-file-powerpoint",
        ".zip" or ".rar" or ".7z" => "fa-file-zipper",
        ".txt" or ".log" or ".md" => "fa-file-lines",
        _ => "fa-file"
    };
}
