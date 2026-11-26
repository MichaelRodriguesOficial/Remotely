namespace Remotely.Desktop.UI.ViewModels.Fakes;
internal class FakeSessionIndicatorWindowViewModel : FakeBrandedViewModelBase, ISessionIndicatorWindowViewModel
{
    public string TechnicianName => "Michael Rodrigues";
    public string Initials => "MR";
    public string StatusText => "Controlando este computador";
    public string ElapsedText => "há 12 min";
    public System.Windows.Input.ICommand EndSessionCommand { get; } = new Remotely.Desktop.Shared.Reactive.RelayCommand(() => { });

    public Task PromptForExit()
    {
        return Task.CompletedTask;
    }
}
