using Remotely.Desktop.Shared.Abstractions;
using Remotely.Desktop.Shared.Services;
using Remotely.Desktop.Shared.ViewModels;
using Remotely.Desktop.UI.Controls.Dialogs;
using Remotely.Desktop.UI.Services;
using Remotely.Desktop.UI.Views;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Remotely.Shared.Extensions;
using System.IO;
using Remotely.Shared.Models.Dtos;

namespace Remotely.Desktop.Win.Services;

public class FileTransferServiceWin : IFileTransferService
{
    private static readonly ConcurrentDictionary<string, FileTransferWindow> _fileTransferWindows =
        new();

    private static readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<FileTransferServiceWin> _logger;
    private readonly IViewModelFactory _viewModelFactory;
    private readonly IDialogProvider _dialogProvider;

    public FileTransferServiceWin(
        IUiDispatcher dispatcher,
        IViewModelFactory viewModelFactory,
        IDialogProvider dialogProvider,
        ILogger<FileTransferServiceWin> logger)
    {
        _dispatcher = dispatcher;
        _viewModelFactory = viewModelFactory;
        _dialogProvider = dialogProvider;
        _logger = logger;
    }

    public string GetBaseDirectory()
    {
        var programDataPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return Directory.CreateDirectory(Path.Combine(programDataPath, "RemoteControl", "Shared")).FullName;
    }

    public void OpenFileTransferWindow(IViewer viewer)
    {
        _dispatcher.Invoke(() =>
        {
            if (_fileTransferWindows.TryGetValue(viewer.ViewerConnectionId, out var window))
            {
                window.Activate();
            }
            else
            {
                var viewModel = _viewModelFactory.CreateFileTransferWindowViewModel(viewer);
                window = new FileTransferWindow()
                {
                    DataContext = viewModel
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

    private sealed record PartialFile(FileStream Stream, string Path, string FileName);
    private static readonly ConcurrentDictionary<string, PartialFile> _partialFiles = new();

    [SupportedOSPlatform("windows")]
    public async Task ReceiveFile(byte[] buffer, string fileName, string messageId, bool endOfFile, bool startOfFile, string? destination, IViewer viewer)
    {
        PartialFile? finished = null;
        string? failure = null;
        try
        {
            await _writeLock.WaitAsync();

            if (startOfFile)
            {
                var safeName = ReceivedFiles.SafeFileName(fileName);
                var directory = ResolveDirectory(destination, viewer);
                string filePath;
                FileStream stream;
                try
                {
                    filePath = ReceivedFiles.GetAvailablePath(directory, safeName);
                    stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write);
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    // Sem permissão na pasta escolhida: salva na pasta compartilhada do app.
                    _logger.LogWarning(ex, "Could not write to {directory}. Using the shared folder.", directory);
                    var baseDir = GetBaseDirectory();
                    SetFileOrFolderPermissions(baseDir);
                    filePath = ReceivedFiles.GetAvailablePath(baseDir, safeName);
                    stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write);
                }
                _partialFiles[messageId] = new PartialFile(stream, filePath, safeName);
            }

            if (!_partialFiles.TryGetValue(messageId, out var partial))
            {
                return;
            }

            if (buffer?.Length > 0)
            {
                await partial.Stream.WriteAsync(buffer);
            }

            if (endOfFile)
            {
                await partial.Stream.DisposeAsync();
                _partialFiles.TryRemove(messageId, out _);
                finished = partial;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while receiving file.");
            failure = ex.Message;
            if (_partialFiles.TryRemove(messageId, out var broken))
            {
                await broken.Stream.DisposeAsync();
            }
        }
        finally
        {
            _writeLock.Release();
        }

        // Avisa o técnico onde o arquivo foi parar (sem janela na tela do usuário: o arquivo aparece na pasta).
        if (finished is not null)
        {
            await viewer.SendDto(new FileSavedDto
            {
                FileName = finished.FileName,
                FullPath = finished.Path,
                Success = true
            }, DtoType.FileSaved);
        }
        else if (failure is not null && (startOfFile || endOfFile))
        {
            await viewer.SendDto(new FileSavedDto
            {
                FileName = ReceivedFiles.SafeFileName(fileName),
                Success = false,
                Message = "Não foi possível salvar o arquivo no computador."
            }, DtoType.FileSaved);
        }
    }

    [SupportedOSPlatform("windows")]
    private string ResolveDirectory(string? destination, IViewer viewer)
    {
        if (ReceivedFiles.TryParsePath(destination) is { } chosen)
        {
            return chosen;
        }

        if (ReceivedFiles.TryParseDrop(destination, out var percentX, out var percentY))
        {
            try
            {
                var bounds = viewer.Capturer.CurrentScreenBounds;
                var x = bounds.Left + (int)Math.Round(bounds.Width * percentX);
                var y = bounds.Top + (int)Math.Round(bounds.Height * percentY);
                if (UserShellFolders.GetFolderAtPoint(x, y) is { } dropped)
                {
                    return dropped;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not resolve the folder where the file was dropped.");
            }
        }

        // Solto fora de uma pasta (ou sem destino): na Área de Trabalho do usuário, onde ele vê.
        return UserShellFolders.GetKnownFolder(UserShellFolders.Desktop)
            ?? UserShellFolders.GetKnownFolder(UserShellFolders.Downloads)
            ?? GetBaseDirectory();
    }

    [SupportedOSPlatform("windows")]
    public FolderListDto ListFolders(string? path)
    {
        var places = new List<FolderItemDto>();
        void AddPlace(Guid id, string name, string kind)
        {
            if (UserShellFolders.GetKnownFolder(id) is { } folder)
            {
                places.Add(new FolderItemDto { Name = name, Path = folder, Kind = kind });
            }
        }

        AddPlace(UserShellFolders.Desktop, "Área de Trabalho", "desktop");
        AddPlace(UserShellFolders.Downloads, "Downloads", "downloads");
        AddPlace(UserShellFolders.Documents, "Documentos", "documents");
        AddPlace(UserShellFolders.PublicDesktop, "Área de Trabalho pública", "public");
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

    [SupportedOSPlatform("windows")]
    private static void SetFileOrFolderPermissions(string path)
    {
        FileSystemSecurity ds;

        var aclSections = AccessControlSections.Access | AccessControlSections.Group | AccessControlSections.Owner;
        if (File.Exists(path))
        {
            ds = new FileSecurity(path, aclSections);
        }
        else if (Directory.Exists(path))
        {
            ds = new DirectorySecurity(path, aclSections);
        }
        else
        {
            return;
        }

        var sid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var account = (NTAccount)sid.Translate(typeof(NTAccount));

        var accessAlreadySet = false;

        foreach (FileSystemAccessRule rule in ds.GetAccessRules(true, true, typeof(NTAccount)))
        {
            if (rule.IdentityReference == account &&
                rule.FileSystemRights.HasFlag(FileSystemRights.Modify) &&
                rule.AccessControlType == AccessControlType.Allow)
            {
                accessAlreadySet = true;
                break;
            }
        }

        if (!accessAlreadySet)
        {
            ds.AddAccessRule(new FileSystemAccessRule(account, FileSystemRights.Modify, AccessControlType.Allow));
            if (File.Exists(path))
            {
                new FileInfo(path).SetAccessControl((FileSecurity)ds);
            }
            else if (Directory.Exists(path))
            {
                new DirectoryInfo(path).SetAccessControl((DirectorySecurity)ds);
            }
        }
    }
}
