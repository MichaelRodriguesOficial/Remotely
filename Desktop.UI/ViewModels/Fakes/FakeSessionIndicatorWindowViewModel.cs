namespace Remotely.Desktop.UI.ViewModels.Fakes;
internal class FakeSessionIndicatorWindowViewModel : FakeBrandedViewModelBase, ISessionIndicatorWindowViewModel
{
    public string TechnicianName => "Michael Rodrigues";
    public string Initials => "MR";
    public string StatusText => "Controlando este computador";
    public string ElapsedText => "Conectado há 12 min";
    public bool IsExpanded => true;
    public bool IsCollapsed => false;
    public bool IsConfirming => false;
    public bool IsEnding => false;
    public System.Windows.Input.ICommand EndSessionCommand { get; } = new Remotely.Desktop.Shared.Reactive.RelayCommand(() => { });
    public System.Windows.Input.ICommand ConfirmEndCommand { get; } = new Remotely.Desktop.Shared.Reactive.RelayCommand(() => { });
    public System.Windows.Input.ICommand CancelEndCommand { get; } = new Remotely.Desktop.Shared.Reactive.RelayCommand(() => { });
    public System.Windows.Input.ICommand HideCommand { get; } = new Remotely.Desktop.Shared.Reactive.RelayCommand(() => { });
    public System.Windows.Input.ICommand ShowCommand { get; } = new Remotely.Desktop.Shared.Reactive.RelayCommand(() => { });

    public Task PromptForExit()
    {
        return Task.CompletedTask;
    }
}
