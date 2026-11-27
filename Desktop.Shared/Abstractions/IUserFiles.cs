using System.Diagnostics;

namespace Remotely.Desktop.Shared.Abstractions;

/// <summary>
/// Arquivos do usuário logado para o chat. No acesso pelo painel o app roda como SYSTEM: escolher
/// arquivos e abrir pastas acontece como o usuário (nunca uma janela de arquivos com poder de SYSTEM).
/// </summary>
public interface IUserFiles
{
    /// <summary>Janela "Abrir" do usuário. Vazio quando ele cancela.</summary>
    Task<IReadOnlyList<string>> PickFiles();

    /// <summary>Salva na pasta Downloads do usuário e devolve o caminho.</summary>
    string? SaveToDownloads(string fileName, byte[] data);

    /// <summary>Mostra o arquivo selecionado no Explorador (ou gerenciador de arquivos).</summary>
    void ShowInFolder(string path);

    /// <summary>Abre o arquivo com o programa padrão, como o usuário.</summary>
    void Open(string path);
}

/// <summary>Linux e modo sem SYSTEM: o app já roda como o usuário.</summary>
public class DefaultUserFiles : IUserFiles
{
    public async Task<IReadOnlyList<string>> PickFiles()
    {
        if (OperatingSystem.IsWindows())
        {
            return [];
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo("zenity",
                "--file-selection --multiple --separator=\"|\" --title=\"Anexar no chat\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false
            });
            if (process is null)
            {
                return [];
            }
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            return output.Trim().Split('|', StringSplitOptions.RemoveEmptyEntries).Where(File.Exists).ToList();
        }
        catch
        {
            return [];
        }
    }

    public string? SaveToDownloads(string fileName, byte[] data)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(folder);
        var path = Services.ReceivedFiles.GetAvailablePath(folder, Services.ReceivedFiles.SafeFileName(fileName));
        File.WriteAllBytes(path, data);
        return path;
    }

    public void ShowInFolder(string path) => Start(Path.GetDirectoryName(path) ?? path);

    public void Open(string path) => Start(path);

    private static void Start(string target)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                Process.Start(new ProcessStartInfo("xdg-open", $"\"{target}\"") { UseShellExecute = false });
                return;
            }
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch
        {
            // Sem programa associado.
        }
    }
}
