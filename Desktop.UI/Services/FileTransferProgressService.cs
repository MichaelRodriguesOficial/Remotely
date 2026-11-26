using Microsoft.Extensions.Logging;
using Remotely.Desktop.Shared.Abstractions;

namespace Remotely.Desktop.UI.Services;

/// <summary>Mostra a janela de progresso das transferências na tela do usuário.</summary>
public class FileTransferProgressService : IFileTransferProgressService
{
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<FileTransferProgressService> _logger;

    public FileTransferProgressService(IUiDispatcher dispatcher, ILogger<FileTransferProgressService> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public IFileTransferProgress Begin(bool incoming, string technicianName, long totalBytes, int fileCount)
    {
        return new Handle(_dispatcher, _logger, incoming, technicianName, totalBytes, fileCount);
    }

    private sealed class Handle : IFileTransferProgress
    {
        private readonly IUiDispatcher _dispatcher;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _cts = new();
        private FileTransferProgressViewModel? _viewModel;
        private FileTransferProgressWindow? _window;
        private long _pendingBytes;
        private DateTimeOffset _lastUpdate = DateTimeOffset.MinValue;
        private bool _completed;

        public Handle(IUiDispatcher dispatcher, ILogger logger, bool incoming, string technicianName, long totalBytes, int fileCount)
        {
            _dispatcher = dispatcher;
            _logger = logger;
            _dispatcher.Post(() =>
            {
                try
                {
                    _viewModel = new FileTransferProgressViewModel(incoming, technicianName, totalBytes, fileCount, () => _cts.Cancel());
                    _window = new FileTransferProgressWindow { DataContext = _viewModel };
                    _viewModel.CloseRequested += (_, _) => _window?.Close();
                    _window.Show();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not show the file transfer window.");
                }
            });
        }

        public CancellationToken CancellationToken => _cts.Token;

        public void FileStarted(string fileName, long size) =>
            _dispatcher.Post(() => _viewModel?.FileStarted(fileName, size));

        public void AddBytes(long bytes)
        {
            // Atualiza a tela no máximo 5 vezes por segundo.
            var pending = Interlocked.Add(ref _pendingBytes, bytes);
            var now = DateTimeOffset.Now;
            if (now - _lastUpdate < TimeSpan.FromMilliseconds(200))
            {
                return;
            }
            _lastUpdate = now;
            Interlocked.Add(ref _pendingBytes, -pending);
            _dispatcher.Post(() => _viewModel?.AddBytes(pending));
        }

        public void FileFinished(string fileName, bool success)
        {
            var pending = Interlocked.Exchange(ref _pendingBytes, 0);
            _dispatcher.Post(() =>
            {
                _viewModel?.AddBytes(pending);
                _viewModel?.FileFinished(fileName, success);
            });
        }

        public void Complete(string? message = null)
        {
            if (_completed)
            {
                return;
            }
            _completed = true;
            var pending = Interlocked.Exchange(ref _pendingBytes, 0);
            _dispatcher.Post(() =>
            {
                _viewModel?.AddBytes(pending);
                _viewModel?.Complete(message);
            });

            // Fecha sozinha depois de alguns segundos (o usuário também pode fechar).
            _ = Task.Delay(TimeSpan.FromSeconds(6)).ContinueWith(_ => _dispatcher.Post(() => _window?.Close()));
        }

        public void Dispose()
        {
            if (!_completed)
            {
                Complete();
            }
        }
    }
}
