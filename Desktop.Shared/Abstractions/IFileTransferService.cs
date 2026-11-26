using Remotely.Desktop.Shared.Services;
using Remotely.Desktop.Shared.ViewModels;

namespace Remotely.Desktop.Shared.Abstractions;

public interface IFileTransferService
{
    string GetBaseDirectory();

    /// <summary>
    /// Recebe um arquivo do visualizador. <paramref name="destination"/> vem no primeiro pedaço:
    /// vazio, "drop:x;y" (pasta sob o ponto onde foi solto) ou "path:..." (pasta escolhida).
    /// </summary>
    Task ReceiveFile(byte[] buffer, string fileName, string messageId, bool endOfFile, bool startOfFile, string? destination, IViewer viewer);

    /// <summary>Pastas de um caminho do computador, para o visualizador escolher onde salvar.</summary>
    Remotely.Shared.Models.Dtos.FolderListDto ListFolders(string? path);
    void OpenFileTransferWindow(IViewer viewer);
    Task UploadFile(FileUpload file, IViewer viewer, Action<double> progressUpdateCallback, CancellationToken cancelToken);
}
