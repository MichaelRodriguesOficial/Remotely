using Remotely.Desktop.Shared.Abstractions;
using Remotely.Desktop.UI.Controls.Dialogs;
using Microsoft.Extensions.Logging;
using Remotely.Desktop.Shared.Services;
using Remotely.Desktop.Shared.Reactive;
using System.Windows.Input;

namespace Remotely.Desktop.UI.ViewModels;

public interface ISessionIndicatorWindowViewModel : IBrandedViewModelBase
{
    string TechnicianName { get; }
    string Initials { get; }
    string StatusText { get; }
    string ElapsedText { get; }
    bool IsExpanded { get; }
    bool IsCollapsed { get; }
    bool IsConfirming { get; }
    /// <summary>Encerrando de verdade: a janela pode fechar.</summary>
    bool IsEnding { get; }
    ICommand EndSessionCommand { get; }
    ICommand ConfirmEndCommand { get; }
    ICommand CancelEndCommand { get; }
    ICommand HideCommand { get; }
    ICommand ShowCommand { get; }
    /// <summary>Abre o chat (pela prévia da mensagem nova).</summary>
    ICommand OpenChatCommand { get; }
    /// <summary>Botão Chat: abre ou oculta a janela do chat.</summary>
    ICommand ToggleChatCommand { get; }
    bool IsChatOpen { get; }
    int UnreadChats { get; }
    bool HasUnreadChats { get; }
    string UnreadText { get; }
    /// <summary>"Fulano: texto" da mensagem não vista, ou "Fulano está digitando...".</summary>
    string? ChatPreview { get; }
    bool HasChatPreview { get; }
    Task PromptForExit();
}

/// <summary>
/// Painel do canto da tela: quem está conectado, o que está fazendo, há quanto tempo e o botão Encerrar.
/// </summary>
public class SessionIndicatorWindowViewModel : BrandedViewModelBase, ISessionIndicatorWindowViewModel
{
    private readonly IShutdownService _shutdownService;
    private readonly IDialogProvider _dialogProvider;
    private readonly IAppState _appState;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.Now;
    private readonly System.Threading.Timer _timer;
    private readonly IChatUiService _chatUi;

    public SessionIndicatorWindowViewModel(
        IBrandingProvider brandingProvider,
        IUiDispatcher dispatcher,
        IShutdownService shutdownService,
        IDialogProvider dialogProvider,
        IAppState appState,
        IChatUiService chatUi,
        ILogger<BrandedViewModelBase> logger)
        : base(brandingProvider, dispatcher, logger)
    {
        OpenChatCommand = new RelayCommand(chatUi.OpenSessionChat);
        ToggleChatCommand = new RelayCommand(chatUi.ToggleSessionChat);
        chatUi.SessionChatChanged += (_, state) => _dispatcher.Post(() => ApplyChatState(state));
        _chatUi = chatUi;
        _shutdownService = shutdownService;
        _dialogProvider = dialogProvider;
        _appState = appState;
        EndSessionCommand = new AsyncRelayCommand(PromptForExit);
        ConfirmEndCommand = new AsyncRelayCommand(EndSession);
        CancelEndCommand = new RelayCommand(() => IsConfirming = false);
        HideCommand = new RelayCommand(() => SetCollapsed(true));
        ShowCommand = new RelayCommand(() =>
        {
            SetCollapsed(false);
            // Aba com mensagem nova: mostrar o painel já abre a conversa.
            if (HasUnreadChats)
            {
                _chatUi.OpenSessionChat();
            }
        });
        Refresh();
        _timer = new System.Threading.Timer(_ => _dispatcher.Post(Refresh), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public string TechnicianName
    {
        get => Get<string>() ?? "Suporte de TI";
        private set => Set(value);
    }

    public string Initials
    {
        get => Get<string>() ?? "TI";
        private set => Set(value);
    }

    public string StatusText
    {
        get => Get<string>() ?? "Conectando...";
        private set => Set(value);
    }

    public string ElapsedText
    {
        get => Get<string>() ?? string.Empty;
        private set => Set(value);
    }

    public bool IsCollapsed
    {
        get => Get<bool>();
        private set => Set(value);
    }

    public bool IsExpanded => !IsCollapsed;

    public bool IsConfirming
    {
        get => Get<bool>();
        private set => Set(value);
    }

    public bool IsEnding
    {
        get => Get<bool>();
        private set => Set(value);
    }

    public ICommand EndSessionCommand { get; }
    public ICommand ConfirmEndCommand { get; }
    public ICommand CancelEndCommand { get; }
    public ICommand HideCommand { get; }
    public ICommand ShowCommand { get; }
    public ICommand OpenChatCommand { get; }
    public ICommand ToggleChatCommand { get; }

    public bool IsChatOpen
    {
        get => Get<bool>();
        private set => Set(value);
    }

    public int UnreadChats
    {
        get => Get<int>();
        private set
        {
            Set(value);
            NotifyPropertyChanged(nameof(HasUnreadChats));
            NotifyPropertyChanged(nameof(UnreadText));
        }
    }

    public bool HasUnreadChats => UnreadChats > 0;

    public string UnreadText => UnreadChats > 9 ? "9+" : UnreadChats.ToString();

    public string? ChatPreview
    {
        get => Get<string?>();
        private set
        {
            Set(value);
            NotifyPropertyChanged(nameof(HasChatPreview));
        }
    }

    public bool HasChatPreview => !string.IsNullOrWhiteSpace(ChatPreview);

    /// <summary>"Encerrar acesso" (ou fechar a janela): pede a confirmação no próprio painel.</summary>
    public Task PromptForExit()
    {
        SetCollapsed(false);
        IsConfirming = true;
        return Task.CompletedTask;
    }

    private async Task EndSession()
    {
        IsEnding = true;
        IsConfirming = false;
        await _timer.DisposeAsync();
        await _shutdownService.Shutdown();
    }

    private void ApplyChatState(SessionChatState state)
    {
        IsChatOpen = state.IsOpen;
        UnreadChats = state.Unread;
        ChatPreview = state.Preview;
    }

    private void SetCollapsed(bool collapsed)
    {
        if (collapsed)
        {
            IsConfirming = false;
        }
        IsCollapsed = collapsed;
        NotifyPropertyChanged(nameof(IsExpanded));
    }

    private void Refresh()
    {
        var viewers = _appState.Viewers.Values.ToList();
        var names = viewers
            .Select(x => x.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        if (names.Count == 0 && !string.IsNullOrWhiteSpace(_appState.RequesterName))
        {
            names.Add(_appState.RequesterName);
        }

        TechnicianName = names.Count switch
        {
            0 => "Suporte de TI",
            1 => names[0],
            _ => $"{names[0]} e mais {names.Count - 1}"
        };
        Initials = GetInitials(names.FirstOrDefault());

        StatusText = viewers.Count == 0
            ? "Conectando..."
            : viewers.Any(x => x.HasControl)
                ? "Controlando este computador"
                : "Vendo a tela deste computador";

        var elapsed = DateTimeOffset.Now - _startedAt;
        ElapsedText = elapsed.TotalHours >= 1
            ? $"Conectado há {(int)elapsed.TotalHours} h {elapsed.Minutes:00} min"
            : elapsed.TotalMinutes >= 1
                ? $"Conectado há {(int)elapsed.TotalMinutes} min"
                : "Conectado agora";
    }

    internal static string GetInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "TI";
        }

        // "ums112 (Silva, Michael)" → usa o que está entre parênteses.
        var open = name.IndexOf('(');
        var close = name.LastIndexOf(')');
        if (open >= 0 && close > open)
        {
            name = name[(open + 1)..close];
        }

        var parts = name
            .Split([' ', ',', '.', '@', '_', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Where(x => char.IsLetter(x[0]))
            .ToList();

        return parts.Count switch
        {
            0 => "TI",
            1 => parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant(),
            _ => $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}"
        };
    }
}
