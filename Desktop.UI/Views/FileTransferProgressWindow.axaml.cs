using Avalonia.Controls;

namespace Remotely.Desktop.UI.Views;

public partial class FileTransferProgressWindow : Window
{
    public FileTransferProgressWindow()
    {
        InitializeComponent();
        // Acima do painel do canto (que mostra quem está conectado).
        Remotely.Desktop.UI.Services.WindowPlacement.KeepBottomRight(this, bottomOffset: 170);
        PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        };
    }
}
