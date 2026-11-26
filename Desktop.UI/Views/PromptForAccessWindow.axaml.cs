using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Remotely.Desktop.UI.Views;

public partial class PromptForAccessWindow : Window
{
    public PromptForAccessWindow()
    {
        InitializeComponent();
        Loaded += Window_Loaded;
        // Sem barra de título: arrasta pelo próprio cartão.
        PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        };
    }

    private void Window_Loaded(object? sender, RoutedEventArgs e)
    {
        Topmost = false;
    }
}
