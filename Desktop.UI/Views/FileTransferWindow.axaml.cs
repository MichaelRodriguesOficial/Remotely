using Avalonia;
using Avalonia.Controls;

namespace Remotely.Desktop.UI.Views;

public partial class FileTransferWindow : Window
{
    public FileTransferWindow()
    {
        InitializeComponent();
        Remotely.Desktop.UI.Services.WindowPlacement.KeepBottomRight(this);
    }

    public IFileTransferWindowViewModel? ViewModel => DataContext as IFileTransferWindowViewModel;

}
