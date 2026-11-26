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
    ICommand EndSessionCommand { get; }
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
    private bool _prompting;

    public SessionIndicatorWindowViewModel(
        IBrandingProvider brandingProvider,
        IUiDispatcher dispatcher,
        IShutdownService shutdownService,
        IDialogProvider dialogProvider,
        IAppState appState,
        ILogger<BrandedViewModelBase> logger)
        : base(brandingProvider, dispatcher, logger)
    {
        _shutdownService = shutdownService;
        _dialogProvider = dialogProvider;
        _appState = appState;
        EndSessionCommand = new AsyncRelayCommand(PromptForExit);
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

    public ICommand EndSessionCommand { get; }

    public async Task PromptForExit()
    {
        if (_prompting)
        {
            return;
        }

        _prompting = true;
        try
        {
            var result = await _dialogProvider.Show(
                "Encerrar o acesso remoto agora? O técnico será desconectado.",
                "Encerrar acesso remoto",
                MessageBoxType.YesNo);
            if (result == MessageBoxResult.Yes)
            {
                await _timer.DisposeAsync();
                await _shutdownService.Shutdown();
            }
        }
        finally
        {
            _prompting = false;
        }
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
            ? $"há {(int)elapsed.TotalHours} h {elapsed.Minutes:00} min"
            : elapsed.TotalMinutes >= 1
                ? $"há {(int)elapsed.TotalMinutes} min"
                : "agora";
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
