using Remotely.Desktop.Shared.Abstractions;
using Microsoft.Extensions.Logging;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace Remotely.Desktop.UI.Services;

public class ClipboardService : IClipboardService
{
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<ClipboardService> _logger;
    private Task? _watcherTask;

    public event EventHandler<string>? ClipboardTextChanged;
    public event EventHandler<string[]>? ClipboardFilesChanged;

    private string _clipboardFiles = string.Empty;

    public ClipboardService(
        IUiDispatcher dispatcher,
        ILogger<ClipboardService> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    private string ClipboardText { get; set; } = string.Empty;

    public void BeginWatching()
    {
        if (_watcherTask?.Status == TaskStatus.Running)
        {
            return;
        }

        _watcherTask = Task.Run(
            async () => await WatchClipboard(_dispatcher.ApplicationExitingToken),
            _dispatcher.ApplicationExitingToken);
    }

    public async Task SetText(string clipboardText)
    {
        try
        {
            if (_dispatcher?.Clipboard is null)
            {
                _logger.LogWarning("Clipboard is null.");
                return;
            }

            if (string.IsNullOrWhiteSpace(clipboardText))
            {
                await _dispatcher.Clipboard.ClearAsync();
            }
            else
            {
                await _dispatcher.Clipboard.SetTextAsync(clipboardText);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while setting text.");
        }
    }

    /// <summary>Arquivos copiados no Explorador: avisa o técnico, que pode baixá-los.</summary>
    private async Task CheckClipboardFiles()
    {
        try
        {
            var clipboard = _dispatcher.Clipboard;
            if (clipboard is null)
            {
                return;
            }

            var formats = await clipboard.GetFormatsAsync();
            if (formats is null || !formats.Contains(DataFormats.Files))
            {
                _clipboardFiles = string.Empty;
                return;
            }

#pragma warning disable CS0618 // GetDataAsync: API estável no Avalonia 11.
            var data = await clipboard.GetDataAsync(DataFormats.Files);
#pragma warning restore CS0618
            var paths = (data as IEnumerable<IStorageItem>)?
                .Select(x => x.TryGetLocalPath())
                .Where(x => !string.IsNullOrWhiteSpace(x) && File.Exists(x))
                .Select(x => x!)
                .ToArray() ?? [];

            var signature = string.Join("|", paths);
            if (paths.Length > 0 && signature != _clipboardFiles)
            {
                _clipboardFiles = signature;
                ClipboardFilesChanged?.Invoke(this, paths);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error while reading files from the clipboard.");
        }
    }

    private async Task WatchClipboard(CancellationToken cancelToken)
    {
        while (
            !cancelToken.IsCancellationRequested &&
            !Environment.HasShutdownStarted)
        {
            try
            {
                if (_dispatcher?.Clipboard is null)
                {
                    continue;
                }

                var currentText = await _dispatcher.Clipboard.GetTextAsync();
                if (!string.IsNullOrEmpty(currentText) && currentText != ClipboardText)
                {
                    ClipboardText = currentText;
                    ClipboardTextChanged?.Invoke(this, ClipboardText);
                }

                await CheckClipboardFiles();
            }
            finally
            {
                Thread.Sleep(500);
            }
        }
    }
}
