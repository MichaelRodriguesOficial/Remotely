using Avalonia.Controls;
using Remotely.Desktop.Shared.Reactive;
using Microsoft.Extensions.Logging;
using System.Windows.Input;
using Remotely.Desktop.Shared.Services;


namespace Remotely.Desktop.UI.ViewModels;

public interface IPromptForAccessWindowViewModel : IBrandedViewModelBase
{
    string OrganizationName { get; set; }
    bool PromptResult { get; set; }
    string RequesterName { get; set; }
    string RequestMessage { get; }
    string CountdownText { get; }
    bool HasCountdown { get; }
    ICommand SetResultNo { get; }
    ICommand SetResultYes { get; }
}

public class PromptForAccessWindowViewModel : BrandedViewModelBase, IPromptForAccessWindowViewModel
{
    public PromptForAccessWindowViewModel(
        string requesterName,
        string organizationName,
        IBrandingProvider brandingProvider,
        IUiDispatcher dispatcher,
        ILogger<BrandedViewModelBase> logger,
        int autoAcceptSeconds = 0)
        : base(brandingProvider, dispatcher, logger)
    {
        _remaining = autoAcceptSeconds;
        if (autoAcceptSeconds > 0)
        {
            _timer = new System.Threading.Timer(_ => _dispatcher.Post(Tick), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        if (!string.IsNullOrWhiteSpace(requesterName))
        {
            RequesterName = requesterName;
        }

        if (!string.IsNullOrWhiteSpace(organizationName))
        {
            OrganizationName = organizationName;
        }
    }

    public string OrganizationName
    {
        get => Get<string>() ?? "your IT provider";
        set
        {
            Set(value);
            NotifyPropertyChanged(nameof(RequestMessage));
        }

    }

    public bool PromptResult { get; set; }

    public string RequesterName
    {
        get => Get<string>() ?? "um técnico";
        set
        {
            Set(value);
            NotifyPropertyChanged(nameof(RequestMessage));
        }
    }

    private readonly System.Threading.Timer? _timer;
    private int _remaining;

    public bool HasCountdown => _timer is not null;

    public string CountdownText => _remaining > 0
        ? $"Sem resposta, o acesso será permitido em {_remaining} s."
        : "Permitindo o acesso...";

    private void Tick()
    {
        if (_remaining > 0)
        {
            _remaining--;
        }
        if (_remaining <= 0)
        {
            _timer?.Dispose();
        }
        NotifyPropertyChanged(nameof(CountdownText));
    }

    public string RequestMessage
    {
        get
        {
            return $"Permitir que {RequesterName} ({OrganizationName}) veja e controle este computador? Você pode encerrar a qualquer momento pelo painel no canto da tela.";
        }
    }
    public ICommand SetResultNo => new RelayCommand<Window>(window =>
    {
        PromptResult = false;
        if (window is not null)
        {
            window.Close();
        }
    });

    public ICommand SetResultYes => new RelayCommand<Window>(window =>
    {
        PromptResult = true;
        if (window is not null)
        {
            window.Close();
        }
    });
}
