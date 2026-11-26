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

    public FileTransferServiceLinux(
        IViewModelFactory viewModelFactory,
        IUiDispatcher dispatcher,
        IDialogProvider dialogProvider,
        ILogger<FileTransferServiceLinux> logger)
    {
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

    public async Task ReceiveFile(byte[] buffer, string fileName, string messageId, bool endOfFile, bool startOfFile, string? destination, IViewer viewer)
    {
        string? savedPath = null;
        try
        {
            await _writeLock.WaitAsync();

            if (startOfFile)
            {
                var directory = ReceivedFiles.TryParsePath(destination) ?? GetBaseDirectory();
                var filePath = ReceivedFiles.GetAvailablePath(directory, ReceivedFiles.SafeFileName(fileName));
                var fs = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write);
                _partialTransfers.AddOrUpdate(messageId, fs, (k, v) => fs);
            }

            if (!_partialTransfers.TryGetValue(messageId, out var fileStream))
            {
                return;
            }

            if (buffer?.Length > 0)
            {
                await fileStream.WriteAsync(buffer);
            }

            if (endOfFile)
            {
                savedPath = fileStream.Name;
                fileStream.Close();
                _partialTransfers.Remove(messageId, out _);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while receiving file.");
        }
        finally
        {
            _writeLock.Release();
        }

        if (savedPath is not null)
        {
            await viewer.SendDto(new FileSavedDto
            {
                FileName = Path.GetFileName(savedPath),
                FullPath = savedPath,
                Success = true
            }, DtoType.FileSaved);
        }
    }

    public FolderListDto ListFolders(string? path)
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
        return ReceivedFiles.BuildListing(path, places);
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
