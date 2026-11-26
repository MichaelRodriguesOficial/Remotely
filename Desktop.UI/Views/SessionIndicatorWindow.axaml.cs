using Avalonia;
using Avalonia.Controls;
using System.ComponentModel;

namespace Remotely.Desktop.UI.Views;

public partial class SessionIndicatorWindow : Window
{
    private readonly Remotely.Desktop.UI.Services.WindowPlacement.Anchor _anchor;
    private ISessionIndicatorWindowViewModel? _viewModel;

    public SessionIndicatorWindow()
    {
        InitializeComponent();

        Closing += SessionIndicatorWindow_Closing;
        PointerPressed += SessionIndicatorWindow_PointerPressed;
        DataContextChanged += SessionIndicatorWindow_DataContextChanged;
        _anchor = Remotely.Desktop.UI.Services.WindowPlacement.KeepBottomRight(this);
    }

    private void SessionIndicatorWindow_DataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is INotifyPropertyChanged old)
        {
            old.PropertyChanged -= ViewModel_PropertyChanged;
        }

        _viewModel = DataContext as ISessionIndicatorWindowViewModel;
        if (_viewModel is INotifyPropertyChanged current)
        {
            current.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Ocultar/mostrar: volta para o canto (a aba fica colada na borda direita).
        if (e.PropertyName == nameof(ISessionIndicatorWindowViewModel.IsCollapsed) && _viewModel is not null)
        {
            _anchor.Reset(flushRight: _viewModel.IsCollapsed);
        }
    }

    private void SessionIndicatorWindow_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.PointerUpdateKind == Avalonia.Input.PointerUpdateKind.LeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private async void SessionIndicatorWindow_Closing(object? sender, CancelEventArgs e)
    {
        // This event appears to fire in design mode too.
        if (Design.IsDesignMode)
        {
            return;
        }

        if (DataContext is ISessionIndicatorWindowViewModel viewModel && !viewModel.IsEnding)
        {
            e.Cancel = true;
            await viewModel.PromptForExit();
        }
    }
}
