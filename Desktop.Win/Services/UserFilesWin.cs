using System.IO;
using System.Runtime.Versioning;
using Remotely.Desktop.Shared.Abstractions;
using Remotely.Desktop.Shared.Services;

namespace Remotely.Desktop.Win.Services;

/// <summary>Arquivos do chat no Windows: seletor, pasta Downloads e Explorador sempre como o usuário logado.</summary>
[SupportedOSPlatform("windows")]
public class UserFilesWin : IUserFiles
{
    private static string Explorer => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");

    public Task<IReadOnlyList<string>> PickFiles() =>
        Task.Run(() => UserShellFolders.PickFilesAsUser("Anexar no chat"));

    public string? SaveToDownloads(string fileName, byte[] data)
    {
        var folder = UserShellFolders.GetKnownFolder(UserShellFolders.Downloads)
            ?? UserShellFolders.GetKnownFolder(UserShellFolders.Desktop);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return null;
        }

        var path = ReceivedFiles.GetAvailablePath(folder, ReceivedFiles.SafeFileName(fileName));
        File.WriteAllBytes(path, data);
        return path;
    }

    public void ShowInFolder(string path) =>
        UserShellFolders.StartAsLoggedOnUser(Explorer, $"/select,\"{path}\"");

    public void Open(string path) =>
        UserShellFolders.StartAsLoggedOnUser(Explorer, $"\"{path}\"");
}
