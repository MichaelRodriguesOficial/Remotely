using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.Collections.Specialized;

namespace Remotely.Desktop.UI.Views;

public partial class ChatWindow : Window
{
    private ChatWindowViewModel? _subscribed;

    public ChatWindow()
    {
        InitializeComponent();

        InputBox.AddHandler(KeyDownEvent, InputBox_KeyDown, RoutingStrategies.Tunnel);
        InputBox.TextChanged += (_, _) => ViewModel?.NotifyTyping();
        AddHandler(DragDrop.DragEnterEvent, (_, e) => DropHint.IsVisible = e.Data.Contains(DataFormats.Files));
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => DropHint.IsVisible = false);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DataContextChanged += (_, _) => Subscribe();
        Opened += (_, _) =>
        {
            ScrollToEnd();
            InputBox.Focus();
        };
        Closed += (_, _) =>
        {
            if (ExitOnClose)
            {
                Environment.Exit(0);
            }
        };
    }

    /// <summary>Chat avulso: fechar a janela encerra o app (e avisa o técnico).</summary>
    public bool ExitOnClose { get; set; }

    /// <summary>Chat da sessão: normalmente fechar só esconde.</summary>
    public bool ForceClose { get; set; }

    private ChatWindowViewModel? ViewModel => DataContext as ChatWindowViewModel;

    private void Subscribe()
    {
        if (_subscribed is not null)
        {
            _subscribed.Messages.CollectionChanged -= Messages_CollectionChanged;
            _subscribed.HideRequested -= ViewModel_HideRequested;
        }
        _subscribed = ViewModel;
        if (_subscribed is not null)
        {
            _subscribed.Messages.CollectionChanged += Messages_CollectionChanged;
            _subscribed.HideRequested += ViewModel_HideRequested;
        }
    }

    private void ViewModel_HideRequested(object? sender, EventArgs e) => Close();

    private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScrollToEnd();

    private void ScrollToEnd()
    {
        // Depois do layout (imagens mudam a altura).
        Dispatcher.UIThread.Post(() => MessagesScroll.ScrollToEnd(), DispatcherPriority.Background);
        DispatcherTimer.RunOnce(() => MessagesScroll.ScrollToEnd(), TimeSpan.FromMilliseconds(300));
    }

    private async void InputBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control) && await PasteFiles())
        {
            return;
        }

        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            if (ViewModel is { } vm)
            {
                _ = vm.SendChatMessage();
            }
        }
    }

    private void Emoji_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Content: string emoji } || ViewModel is not { } vm)
        {
            return;
        }

        var caret = InputBox.CaretIndex;
        vm.InsertEmoji(emoji, caret);
        EmojiButton.Flyout?.Hide();
        InputBox.Focus();
        InputBox.CaretIndex = Math.Min(caret + emoji.Length, vm.InputText.Length);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
    }

    /// <summary>Ctrl+V com arquivos copiados no Explorador: anexa, como colar no chat do visualizador.</summary>
    private async Task<bool> PasteFiles()
    {
        if (ViewModel is not { } vm || Clipboard is not { } clipboard)
        {
            return false;
        }
        try
        {
            var formats = await clipboard.GetFormatsAsync();
            if (!formats.Contains(DataFormats.Files) ||
                await clipboard.GetDataAsync(DataFormats.Files) is not IEnumerable<IStorageItem> items)
            {
                return false;
            }
            var paths = items
                .Select(x => x.TryGetLocalPath())
                .Where(x => !string.IsNullOrWhiteSpace(x) && File.Exists(x))
                .Select(x => x!)
                .ToList();
            if (paths.Count == 0)
            {
                return false;
            }
            await vm.AddFiles(paths);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        DropHint.IsVisible = false;
        if (ViewModel is not { } vm)
        {
            return;
        }
        var paths = e.Data.GetFiles()?
            .Select(x => x.TryGetLocalPath())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .ToList() ?? [];
        if (paths.Count > 0)
        {
            await vm.AddFiles(paths);
        }
    }
}
