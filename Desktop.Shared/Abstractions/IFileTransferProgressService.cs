namespace Remotely.Desktop.Shared.Abstractions;

/// <summary>
/// Janela na tela do usuário com os arquivos sendo transferidos, barra de progresso, tempo
/// estimado e botão Cancelar. Uma janela por lote (vários arquivos enviados juntos).
/// </summary>
public interface IFileTransferProgressService
{
    /// <param name="incoming">true: o técnico está enviando para este computador; false: copiando daqui.</param>
    IFileTransferProgress Begin(bool incoming, string technicianName, long totalBytes, int fileCount);
}

public interface IFileTransferProgress : IDisposable
{
    /// <summary>Cancelado pelo usuário (botão Cancelar).</summary>
    CancellationToken CancellationToken { get; }

    void FileStarted(string fileName, long size);
    void AddBytes(long bytes);
    void FileFinished(string fileName, bool success);

    /// <summary>Terminou (ou falhou): mostra o resultado e fecha sozinha em alguns segundos.</summary>
    void Complete(string? message = null);
}

/// <summary>Sem interface gráfica (ou sem tela): não mostra nada.</summary>
public sealed class NullFileTransferProgressService : IFileTransferProgressService
{
    public IFileTransferProgress Begin(bool incoming, string technicianName, long totalBytes, int fileCount) => new NullProgress();

    private sealed class NullProgress : IFileTransferProgress
    {
        private readonly CancellationTokenSource _cts = new();
        public CancellationToken CancellationToken => _cts.Token;
        public void AddBytes(long bytes) { }
        public void Complete(string? message = null) { }
        public void Dispose() => _cts.Dispose();
        public void FileFinished(string fileName, bool success) { }
        public void FileStarted(string fileName, long size) { }
    }
}
