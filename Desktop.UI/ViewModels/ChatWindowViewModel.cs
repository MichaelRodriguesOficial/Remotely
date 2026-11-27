using System.Threading;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;
using Remotely.Desktop.Shared.Abstractions;
using Remotely.Desktop.Shared.Reactive;
using Remotely.Desktop.Shared.Services;
using Remotely.Desktop.UI.Views;
using Remotely.Shared.Models.Dtos;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Remotely.Desktop.UI.ViewModels;

/// <summary>Por onde o chat conversa: canal da tela (sessão) ou agente (chat avulso).</summary>
public interface IChatChannel
{
    bool IsSession { get; }

    /// <summary>Envia a mensagem. Anexos chegam com Data preenchido.</summary>
    Task<bool> Send(ChatMessageDto message);

    Task SendTyping();

    Task SendRead(string messageId);

    /// <summary>Conteúdo de um anexo recebido sem os bytes (chat avulso).</summary>
    Task<byte[]?> LoadAttachment(ChatAttachmentDto attachment);
}

public interface IChatWindowViewModel : IBrandedViewModelBase
{
    string PeerName { get; }
    string StatusText { get; }
    string WindowTitle { get; }
    string EmptyText { get; }
    ObservableCollection<ChatItemViewModel> Messages { get; }
    ObservableCollection<PendingAttachmentViewModel> Pending { get; }
    bool HasPending { get; }
    bool HasNoMessages { get; }
    bool CanHide { get; }
    string InputText { get; set; }
    IReadOnlyList<EmojiGroupViewModel> EmojiGroups { get; }
    EmojiGroupViewModel SelectedEmojiGroup { get; }
    ICommand SendCommand { get; }
    ICommand AttachCommand { get; }
    ICommand HideCommand { get; }

    Task SendChatMessage();
    void InsertEmoji(string emoji, int caretIndex);
    Task AddFiles(IEnumerable<string> paths);
    void NotifyTyping();
    void MarkRead();
}

public class ChatWindowViewModel : BrandedViewModelBase, IChatWindowViewModel
{
    /// <summary>Os mesmos grupos e emojis do visualizador (ChatPanel.ts, EmojiGroups).</summary>
    public static readonly IReadOnlyList<(string Name, string Icon, string[] Emojis)> EmojiCatalog =
    [
        ("Rostos", "😀", ["😀", "😃", "😄", "😁", "😆", "😅", "😂", "🤣", "😊", "🙂", "😉", "😍", "😘", "😎", "🤓", "🤔", "🤨", "😐", "😑", "🙄", "😏", "😬", "😴", "😌", "😕", "🙁", "😟", "😢", "😭", "😤", "😡", "🤯", "😳", "😱", "🥲", "🥳", "🤗", "🤝", "🫡", "😇"]),
        ("Gestos", "👍", ["👍", "👎", "👌", "✌️", "🤞", "🙏", "👏", "🙌", "👋", "✋", "👉", "👈", "👆", "👇", "💪", "🫶", "❤️", "💙", "💚", "💜", "🔥", "✨", "⭐", "💯", "✅", "❌", "⚠️", "❓", "❗", "💡"]),
        ("TI", "💻", ["💻", "🖥️", "⌨️", "🖱️", "🖨️", "📱", "📞", "☎️", "🔌", "🔋", "💾", "📀", "🧰", "🛠️", "🔧", "🔨", "⚙️", "🔒", "🔓", "🔑", "🛡️", "🌐", "📶", "📡", "🐛", "🚀", "📦", "🗑️", "♻️", "⏳"]),
        ("Trabalho", "📄", ["📄", "📃", "📑", "📊", "📈", "📉", "📋", "📌", "📎", "🖇️", "📁", "📂", "🗂️", "📅", "🗓️", "⏰", "⏱️", "✉️", "📧", "📨", "📝", "✏️", "🖊️", "🔍", "💬", "🗨️", "☕", "🍕", "🎉", "🏁"])
    ];

    private static readonly IBrush TypingBrush = new SolidColorBrush(Color.Parse("#A5B4FC"));
    private static readonly IBrush IdleBrush = new SolidColorBrush(Color.Parse("#64748B"));

    private readonly IChatChannel _channel;
    private readonly IUserFiles _userFiles;
    private string _myName;
    private DateTimeOffset _lastTypingSent;
    private string? _lastIncomingId;
    private System.Threading.Timer? _typingTimer;

    public ChatWindowViewModel(
        IChatChannel channel,
        string peerName,
        string myName,
        IUserFiles userFiles,
        IBrandingProvider brandingProvider,
        IUiDispatcher dispatcher,
        ILogger<ChatWindowViewModel> logger)
        : base(brandingProvider, dispatcher, logger)
    {
        _channel = channel;
        _userFiles = userFiles;
        _myName = myName;
        PeerName = string.IsNullOrWhiteSpace(peerName) ? "Suporte de TI" : peerName;
        SendCommand = new AsyncRelayCommand(SendChatMessage);
        AttachCommand = new AsyncRelayCommand(PickAndAttach);
        HideCommand = new RelayCommand(() => HideRequested?.Invoke(this, EventArgs.Empty));
        OpenLinkCommand = new RelayCommand<string>(OpenLink);
        EmojiGroups = EmojiCatalog.Select(x => new EmojiGroupViewModel(x.Name, x.Icon, x.Emojis, SelectEmojiGroup)).ToList();
        SelectEmojiGroup(EmojiGroups[0]);
        Pending.CollectionChanged += (_, _) => NotifyPropertyChanged(nameof(HasPending));
        Messages.CollectionChanged += (_, _) => NotifyPropertyChanged(nameof(HasNoMessages));
    }

    public string PeerName
    {
        get => Get<string>() ?? "Suporte de TI";
        private set
        {
            Set(value);
            NotifyPropertyChanged(nameof(WindowTitle));
        }
    }

    /// <summary>"Fulano está digitando..." ou o estado da conversa.</summary>
    public string StatusText
    {
        get => Get<string>() ?? IdleStatus;
        private set
        {
            Set(value);
            NotifyPropertyChanged(nameof(IsPeerTyping));
            NotifyPropertyChanged(nameof(StatusBrush));
            NotifyPropertyChanged(nameof(StatusFontStyle));
        }
    }

    public bool IsPeerTyping => StatusText != IdleStatus;

    public IBrush StatusBrush => IsPeerTyping ? TypingBrush : IdleBrush;

    public FontStyle StatusFontStyle => IsPeerTyping ? FontStyle.Italic : FontStyle.Normal;

    public string EmptyText => _channel.IsSession
        ? "Converse com o técnico sem parar o que está fazendo. Arraste ou cole imagens e documentos aqui."
        : "Escreva para o suporte de TI. Arraste ou cole imagens e documentos aqui.";

    /// <summary>Chat da sessão: dá para ocultar e voltar pelo painel. O avulso fecha pela janela.</summary>
    public bool CanHide => _channel.IsSession;

    public event EventHandler? HideRequested;

    private string IdleStatus => _channel.IsSession ? "Conectado a este computador" : "Suporte de TI";

    public string WindowTitle => $"Chat com {PeerName}";

    public ObservableCollection<ChatItemViewModel> Messages { get; } = [];

    public ObservableCollection<PendingAttachmentViewModel> Pending { get; } = [];

    public bool HasPending => Pending.Count > 0;

    public bool HasNoMessages => Messages.Count == 0;

    public string InputText
    {
        get => Get<string>() ?? string.Empty;
        set => Set(value);
    }

    public IReadOnlyList<EmojiGroupViewModel> EmojiGroups { get; }

    public EmojiGroupViewModel SelectedEmojiGroup
    {
        get => Get<EmojiGroupViewModel>()!;
        private set => Set(value);
    }

    public ICommand SendCommand { get; }

    public ICommand AttachCommand { get; }

    public ICommand HideCommand { get; }

    public ICommand OpenLinkCommand { get; }

    public void SetPeer(string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            PeerName = name;
        }
    }

    public void AddIncoming(ChatMessageDto message, string senderName)
    {
        SetPeer(senderName);
        _typingTimer?.Dispose();
        StatusText = IdleStatus;
        _lastIncomingId = message.Id;
        Messages.Add(ChatItemViewModel.Create(message, false, PeerName, this));
    }

    public void AddSystem(string text)
    {
        Messages.Add(ChatItemViewModel.SystemNote(text));
    }

    public void ShowTyping(string senderName)
    {
        StatusText = $"{(string.IsNullOrWhiteSpace(senderName) ? PeerName : senderName)} está digitando...";
        _typingTimer?.Dispose();
        _typingTimer = new System.Threading.Timer(_ => _dispatcher.Post(() => StatusText = IdleStatus),
            null, TimeSpan.FromSeconds(4.5), Timeout.InfiniteTimeSpan);
    }

    public void MarkReadUpTo(string messageId)
    {
        var reached = false;
        for (var i = Messages.Count - 1; i >= 0; i--)
        {
            var item = Messages[i];
            if (item.Id == messageId)
            {
                reached = true;
            }
            if (reached && item.IsMine)
            {
                item.IsRead = true;
            }
        }
    }

    public void MarkRead()
    {
        if (_lastIncomingId is { } id)
        {
            _lastIncomingId = null;
            _ = _channel.SendRead(id);
        }
    }

    public void NotifyTyping()
    {
        if (DateTimeOffset.Now - _lastTypingSent < TimeSpan.FromSeconds(3) || string.IsNullOrWhiteSpace(InputText))
        {
            return;
        }
        _lastTypingSent = DateTimeOffset.Now;
        _ = _channel.SendTyping();
    }

    public void InsertEmoji(string emoji, int caretIndex)
    {
        var text = InputText;
        var index = Math.Clamp(caretIndex, 0, text.Length);
        InputText = text.Insert(index, emoji);
    }

    public async Task AddFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                if (Pending.Count >= ChatLimits.MaxAttachmentsPerMessage)
                {
                    AddSystem($"Até {ChatLimits.MaxAttachmentsPerMessage} anexos por mensagem.");
                    break;
                }

                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    continue;
                }
                if (info.Length > ChatLimits.MaxAttachmentBytes)
                {
                    AddSystem($"{info.Name} passa de 25 MB e não pode ser enviado pelo chat.");
                    continue;
                }

                var data = await File.ReadAllBytesAsync(path);
                Pending.Add(new PendingAttachmentViewModel(info.Name, data, this));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not attach {path}.", path);
                AddSystem($"Não foi possível anexar {Path.GetFileName(path)}.");
            }
        }
    }

    public void RemovePending(PendingAttachmentViewModel item) => Pending.Remove(item);

    public async Task SendChatMessage()
    {
        var text = InputText.Trim();
        if (text.Length > ChatLimits.MaxTextLength)
        {
            text = text[..ChatLimits.MaxTextLength];
        }
        if (string.IsNullOrWhiteSpace(text) && Pending.Count == 0)
        {
            return;
        }

        var message = new ChatMessageDto
        {
            SenderName = _myName,
            Text = text,
            Attachments = Pending.Select(x => new ChatAttachmentDto
            {
                FileName = x.FileName,
                ContentType = ChatLimits.ContentTypeFor(x.FileName),
                Size = x.Data.LongLength,
                Data = x.Data
            }).ToList()
        };

        InputText = string.Empty;
        Pending.Clear();
        var item = ChatItemViewModel.Create(message, true, _myName, this);
        item.IsSending = true;
        Messages.Add(item);

        bool sent;
        try
        {
            sent = await _channel.Send(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while sending chat message.");
            sent = false;
        }

        item.IsSending = false;
        item.Failed = !sent;
        if (!sent)
        {
            AddSystem(_channel.IsSession
                ? "A mensagem não foi enviada: o técnico não está mais conectado."
                : "A mensagem não foi enviada. Tente de novo.");
        }
    }

    internal Task<byte[]?> LoadAttachment(ChatAttachmentDto attachment) => _channel.LoadAttachment(attachment);

    internal IUserFiles UserFiles => _userFiles;

    internal IUiDispatcher Dispatcher => _dispatcher;

    internal void Log(Exception ex, string message) => _logger.LogWarning(ex, "{message}", message);

    private void SelectEmojiGroup(EmojiGroupViewModel group)
    {
        foreach (var item in EmojiGroups)
        {
            item.IsSelected = item == group;
        }
        SelectedEmojiGroup = group;
    }

    private void OpenLink(string? url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            _userFiles.Open(uri.AbsoluteUri);
        }
    }

    private async Task PickAndAttach()
    {
        var paths = await _userFiles.PickFiles();
        await AddFiles(paths);
    }
}

public class ChatItemViewModel : ObservableObject
{
    private static readonly IBrush MineBrush = new SolidColorBrush(Color.Parse("#4F46E5"));
    private static readonly IBrush TheirsBrush = new SolidColorBrush(Color.Parse("#1E293B"));
    private static readonly IBrush SystemBrush = new SolidColorBrush(Color.Parse("#1F94A3B8"));
    private static readonly IBrush MineText = Brushes.White;
    private static readonly IBrush TheirsText = new SolidColorBrush(Color.Parse("#E2E8F0"));
    private static readonly IBrush SystemText = new SolidColorBrush(Color.Parse("#94A3B8"));
    private static readonly IBrush ReadBrush = new SolidColorBrush(Color.Parse("#67E8F9"));
    private static readonly IBrush FailedBrush = new SolidColorBrush(Color.Parse("#FCA5A5"));

    public string Id { get; private init; } = string.Empty;
    public bool IsMine { get; private init; }
    public bool IsSystem { get; private init; }
    public string Sender { get; private init; } = string.Empty;
    public string Text { get; private init; } = string.Empty;
    public string TimeText { get; private init; } = string.Empty;
    public ICommand? OpenLinkCommand { get; private init; }
    public ObservableCollection<ChatAttachmentViewModel> Attachments { get; } = [];

    public bool HasText => !string.IsNullOrWhiteSpace(Text);
    public bool ShowSender => !IsMine && !IsSystem;
    public bool ShowStatus => IsMine;
    public double TextSize => IsSystem ? 12 : IsOnlyEmoji(Text) ? 30 : 13.5;
    public TextAlignment TextAlignment => IsSystem ? TextAlignment.Center : TextAlignment.Left;

    public HorizontalAlignment Alignment => IsSystem ? HorizontalAlignment.Center : IsMine ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    public IBrush BubbleBrush => IsSystem ? SystemBrush : IsMine ? MineBrush : TheirsBrush;
    public IBrush TextBrush => IsSystem ? SystemText : IsMine ? MineText : TheirsText;
    public Avalonia.CornerRadius BubbleCorners => IsSystem ? new(10) : IsMine ? new(14, 14, 4, 14) : new(14, 14, 14, 4);

    public bool IsSending
    {
        get => Get<bool>();
        set
        {
            Set(value);
            NotifyStatus();
        }
    }

    public bool IsRead
    {
        get => Get<bool>();
        set
        {
            Set(value);
            NotifyStatus();
        }
    }

    public bool Failed
    {
        get => Get<bool>();
        set
        {
            Set(value);
            NotifyStatus();
        }
    }

    public Geometry StatusIcon => Failed ? ChatIcons.Warning : ChatIcons.CheckDouble;

    public IBrush StatusBrush => Failed ? FailedBrush : IsRead ? ReadBrush : MineText;

    public double StatusOpacity => Failed || IsRead ? 1 : IsSending ? 0.35 : 0.75;

    public string StatusTip => Failed ? "Não enviada" : IsSending ? "Enviando..." : IsRead ? "Lida" : "Enviada";

    public static ChatItemViewModel Create(ChatMessageDto message, bool mine, string sender, ChatWindowViewModel owner)
    {
        var time = DateTimeOffset.TryParse(message.Timestamp, out var parsed) ? parsed.ToLocalTime() : DateTimeOffset.Now;
        var item = new ChatItemViewModel
        {
            Id = message.Id,
            IsMine = mine,
            Sender = string.IsNullOrWhiteSpace(message.SenderName) ? sender : message.SenderName,
            Text = message.Text ?? string.Empty,
            TimeText = time.ToString("HH:mm"),
            OpenLinkCommand = owner.OpenLinkCommand
        };
        foreach (var attachment in message.Attachments ?? [])
        {
            item.Attachments.Add(new ChatAttachmentViewModel(attachment, mine, owner));
        }
        return item;
    }

    public static ChatItemViewModel SystemNote(string text) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        IsSystem = true,
        Text = text,
        TimeText = DateTimeOffset.Now.ToString("HH:mm")
    };

    /// <summary>Só emojis (sem letras nem números): aparecem maiores, como nos apps de mensagem.</summary>
    internal static bool IsOnlyEmoji(string text)
    {
        var trimmed = string.Concat((text ?? string.Empty).Where(c => !char.IsWhiteSpace(c)));
        return trimmed.Length is > 0 and <= 16 &&
            !trimmed.Any(c => char.IsLetterOrDigit(c) || char.IsPunctuation(c)) &&
            trimmed.Any(char.IsSurrogate);
    }

    private void NotifyStatus()
    {
        NotifyPropertyChanged(nameof(StatusIcon));
        NotifyPropertyChanged(nameof(StatusBrush));
        NotifyPropertyChanged(nameof(StatusOpacity));
        NotifyPropertyChanged(nameof(StatusTip));
    }
}

public class ChatAttachmentViewModel : ObservableObject
{
    private readonly ChatAttachmentDto _attachment;
    private readonly ChatWindowViewModel _owner;
    private byte[]? _data;
    private string? _savedPath;

    public ChatAttachmentViewModel(ChatAttachmentDto attachment, bool mine, ChatWindowViewModel owner)
    {
        _attachment = attachment;
        _owner = owner;
        _data = attachment.Data;
        IsMine = mine;
        SaveCommand = new AsyncRelayCommand(Save);
        OpenCommand = new AsyncRelayCommand(Open);
        if (attachment.IsImage)
        {
            _ = LoadImage();
        }
    }

    public bool IsMine { get; }
    public IBrush TextBrush => IsMine ? Brushes.White : new SolidColorBrush(Color.Parse("#E2E8F0"));
    public string FileName => string.IsNullOrWhiteSpace(_attachment.FileName) ? "arquivo" : _attachment.FileName;
    public string SizeText => ChatLimits.FormatSize(_attachment.Size > 0 ? _attachment.Size : _data?.LongLength ?? 0);
    public Geometry Icon => FileIcon(FileName);
    public bool IsImage => _attachment.IsImage;

    public Bitmap? Image
    {
        get => Get<Bitmap?>();
        private set
        {
            Set(value);
            NotifyPropertyChanged(nameof(HasImage));
            NotifyPropertyChanged(nameof(ShowFileCard));
        }
    }

    public bool HasImage => Image is not null;
    public bool ShowFileCard => !HasImage;

    /// <summary>Baixando, salvo ou erro. Vazio enquanto nada aconteceu.</summary>
    public string? StatusText
    {
        get => Get<string?>();
        private set
        {
            Set(value);
            NotifyPropertyChanged(nameof(HasStatus));
        }
    }

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);

    public ICommand SaveCommand { get; }
    public ICommand OpenCommand { get; }

    private async Task LoadImage()
    {
        try
        {
            var data = await GetData();
            if (data is null || data.Length == 0)
            {
                return;
            }
            using var stream = new MemoryStream(data);
            var bitmap = Bitmap.DecodeToWidth(stream, 560);
            await _owner.Dispatcher.InvokeAsync(() => { Image = bitmap; });
        }
        catch (Exception ex)
        {
            _owner.Log(ex, "Could not show chat image.");
        }
    }

    private async Task<byte[]?> GetData()
    {
        if (_data is null || _data.Length == 0)
        {
            _data = await _owner.LoadAttachment(_attachment);
        }
        return _data;
    }

    /// <summary>Salva em Downloads do usuário e mostra o arquivo na pasta.</summary>
    private async Task Save()
    {
        var path = await SaveToDownloads();
        if (path is not null)
        {
            _owner.UserFiles.ShowInFolder(path);
        }
    }

    private async Task Open()
    {
        var path = await SaveToDownloads();
        if (path is not null)
        {
            _owner.UserFiles.Open(path);
        }
    }

    private async Task<string?> SaveToDownloads()
    {
        if (_savedPath is not null && File.Exists(_savedPath))
        {
            return _savedPath;
        }

        try
        {
            StatusText = "Baixando...";
            var data = await GetData();
            if (data is null)
            {
                StatusText = "Não foi possível baixar.";
                return null;
            }
            _savedPath = _owner.UserFiles.SaveToDownloads(FileName, data);
            StatusText = _savedPath is null ? "Não foi possível salvar." : "Salvo em Downloads";
            return _savedPath;
        }
        catch (Exception ex)
        {
            _owner.Log(ex, "Could not save chat attachment.");
            StatusText = "Não foi possível salvar.";
            return null;
        }
    }

    /// <summary>O mesmo ícone por tipo de arquivo do visualizador (ChatPanel.ts, FileIcon).</summary>
    internal static Geometry FileIcon(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" => ChatIcons.FileImage,
        ".pdf" => ChatIcons.FilePdf,
        ".doc" or ".docx" or ".odt" or ".rtf" => ChatIcons.FileWord,
        ".xls" or ".xlsx" or ".csv" or ".ods" => ChatIcons.FileExcel,
        ".ppt" or ".pptx" => ChatIcons.FilePowerpoint,
        ".zip" or ".rar" or ".7z" => ChatIcons.FileZipper,
        ".txt" or ".log" or ".md" => ChatIcons.FileLines,
        _ => ChatIcons.File
    };
}

public class PendingAttachmentViewModel
{
    private readonly ChatWindowViewModel _owner;

    public PendingAttachmentViewModel(string fileName, byte[] data, ChatWindowViewModel owner)
    {
        FileName = fileName;
        Data = data;
        _owner = owner;
        RemoveCommand = new RelayCommand(() => _owner.RemovePending(this));
        if (ChatLimits.IsImage(null, fileName))
        {
            try
            {
                using var stream = new MemoryStream(data);
                Thumbnail = Bitmap.DecodeToWidth(stream, 68);
            }
            catch
            {
                // Imagem que não abre: fica com o ícone do tipo de arquivo.
            }
        }
    }

    public string FileName { get; }
    public byte[] Data { get; }
    public Bitmap? Thumbnail { get; }
    public bool HasThumbnail => Thumbnail is not null;
    public Geometry Icon => ChatAttachmentViewModel.FileIcon(FileName);
    public string Label => $"{FileName} · {ChatLimits.FormatSize(Data.LongLength)}";
    public ICommand RemoveCommand { get; }
}

/// <summary>Uma aba do seletor de emojis.</summary>
public class EmojiGroupViewModel : ObservableObject
{
    public EmojiGroupViewModel(string name, string icon, IReadOnlyList<string> emojis, Action<EmojiGroupViewModel> select)
    {
        Name = name;
        Icon = icon;
        Emojis = emojis;
        SelectCommand = new RelayCommand(() => select(this));
    }

    public string Name { get; }
    public string Icon { get; }
    public IReadOnlyList<string> Emojis { get; }
    public ICommand SelectCommand { get; }

    public bool IsSelected
    {
        get => Get<bool>();
        set => Set(value);
    }
}
