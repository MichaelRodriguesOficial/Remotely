using Remotely.Desktop.Shared.Abstractions;
using Remotely.Desktop.Shared.Services;
using Remotely.Desktop.Shared.ViewModels;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Remotely.Desktop.UI.Controls.Dialogs;
using Remotely.Desktop.UI.Views;
using Remotely.Desktop.UI.Services;
using System.Threading;
using System.IO;
using Remotely.Shared.Models.Dtos;

namespace Remotely.Desktop.Linux.Services;

public class FileTransferServiceLinux : IFileTransferService
{
    private static readonly ConcurrentDictionary<string, FileTransferWindow> _fileTransferWindows = new();
    private static readonly ConcurrentDictionary<string, FileStream> _partialTransfers = new();
    private static readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly IViewModelFactory _viewModelFactory;
    private readonly IUiDispatcher _dispatcher;
    private readonly IDialogProvider _dialogProvider;
    private readonly ILogger<FileTransferServiceLinux> _logger;
    private readonly IFileTransferProgressService _progressService;

    public FileTransferServiceLinux(
        IViewModelFactory viewModelFactory,
        IUiDispatcher dispatcher,
        IDialogProvider dialogProvider,
        IFileTransferProgressService progressService,
        ILogger<FileTransferServiceLinux> logger)
    {
        _progressService = progressService;
        _viewModelFactory = viewModelFactory;
        _dispatcher = dispatcher;
        _dialogProvider = dialogProvider;
        _logger = logger;
    }

    public string GetBaseDirectory()
    {
        var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (Directory.Exists(desktopDir))
        {
            return desktopDir;
        }

        return Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "RemoteControl")).FullName;
    }

    public void OpenFileTransferWindow(IViewer viewer)
    {
        _dispatcher.Post(() =>
        {
            if (_fileTransferWindows.TryGetValue(viewer.ViewerConnectionId, out var window))
            {
                window.Activate();
            }
            else
            {
                window = new FileTransferWindow
                {
                    DataContext = _viewModelFactory.CreateFileTransferWindowViewModel(viewer)
                };
                window.Closed += (sender, arg) =>
                {
                    _fileTransferWindows.Remove(viewer.ViewerConnectionId, out _);
                };
                _fileTransferWindows.AddOrUpdate(viewer.ViewerConnectionId, window, (k, v) => window);
                window.Show();
            }
        });
    }

    public Task ReceiveFile(FileDto dto, IViewer viewer)
    {
        return FileTransferCore.ReceiveFile(
            dto,
            viewer,
            (destination, _) => ReceivedFiles.TryParsePath(destination) ?? GetBaseDirectory(),
            GetBaseDirectory,
            _progressService,
            _logger);
    }

    public Task SendFilesToViewer(IReadOnlyList<string> paths, IViewer viewer)
    {
        return FileTransferCore.SendFilesToViewer(paths, viewer, _progressService, _logger);
    }

    public FolderListDto ListFolders(string? path, bool includeFiles = false)
    {
        var places = new List<FolderItemDto>
        {
            new() { Name = "Pasta do app", Path = GetBaseDirectory(), Kind = "folder" }
        };
        if (Directory.Exists("/home"))
        {
            places.Add(new FolderItemDto { Name = "/home", Path = "/home", Kind = "folder" });
        }
        places.Add(new FolderItemDto { Name = "/tmp", Path = "/tmp", Kind = "folder" });
        ReceivedFiles.AddDrives(places);
        return ReceivedFiles.BuildListing(path, places, includeFiles);
    }

    public async Task UploadFile(
        FileUpload fileUpload,
        IViewer viewer,
        Action<double> progressUpdateCallback,
        CancellationToken cancelToken)
    {
        try
        {
            await viewer.SendFile(fileUpload, progressUpdateCallback, cancelToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while uploading file.");
        }
    }
}
