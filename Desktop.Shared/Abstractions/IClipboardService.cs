namespace Remotely.Desktop.Shared.Abstractions;

public interface IClipboardService
{
    event EventHandler<string> ClipboardTextChanged;

    /// <summary>O usuário copiou arquivos (caminhos completos).</summary>
    event EventHandler<string[]>? ClipboardFilesChanged;

    void BeginWatching();

    Task SetText(string clipboardText);
}
