using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Remotely.Desktop.Native.Windows;
using Remotely.Desktop.Shared.Abstractions;

namespace Remotely.Desktop.Win.Services;

/// <summary>
/// Área de transferência do Windows direto pela API Win32. No acesso pelo painel o app roda sem janela
/// principal do Avalonia, e a área de transferência dele ficava nula: copiar e colar não funcionava em
/// nenhum sentido. Aqui uma thread própria com uma janela invisível lê e grava a área de transferência
/// da sessão do usuário (texto e arquivos copiados no Explorador).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ClipboardServiceWin : IClipboardService, IDisposable
{
    private const uint CF_UNICODETEXT = 13;
    private const uint CF_HDROP = 15;
    private const uint GMEM_MOVEABLE = 0x0002;
    private static readonly nint HWND_MESSAGE = -3;

    private readonly BlockingCollection<Action> _queue = new();
    private readonly ILogger<ClipboardServiceWin> _logger;
    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;
    private nint _window;
    private uint _lastSequence;
    private string _lastText = string.Empty;
    private string _lastFiles = string.Empty;

    public ClipboardServiceWin(ILogger<ClipboardServiceWin> logger)
    {
        _logger = logger;
    }

    public event EventHandler<string>? ClipboardTextChanged;
    public event EventHandler<string[]>? ClipboardFilesChanged;

    public void BeginWatching()
    {
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(Run) { IsBackground = true, Name = "Clipboard" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task SetText(string clipboardText)
    {
        BeginWatching();
        var done = new TaskCompletionSource();
        _queue.Add(() =>
        {
            try
            {
                WriteText(clipboardText ?? string.Empty);
                // O que nós mesmos colocamos não volta para o técnico.
                _lastText = clipboardText ?? string.Empty;
                _lastSequence = GetClipboardSequenceNumber();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while setting clipboard text.");
            }
            finally
            {
                done.TrySetResult();
            }
        });
        return done.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private void Run()
    {
        try
        {
            Win32Interop.SwitchToInputDesktop();
            _window = CreateWindowEx(0, "STATIC", "RemotelyClipboard", 0, 0, 0, 0, 0, HWND_MESSAGE, 0, 0, 0);
            _lastSequence = GetClipboardSequenceNumber();
            _lastText = ReadText() ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not start the clipboard thread.");
        }

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                if (_queue.TryTake(out var action, 500, _cts.Token))
                {
                    action();
                    continue;
                }

                CheckForChanges();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error while watching the clipboard.");
            }
        }
    }

    private void CheckForChanges()
    {
        var sequence = GetClipboardSequenceNumber();
        if (sequence == _lastSequence)
        {
            return;
        }
        _lastSequence = sequence;

        var files = ReadFiles();
        if (files.Length > 0)
        {
            var signature = string.Join("|", files);
            if (signature != _lastFiles)
            {
                _lastFiles = signature;
                ClipboardFilesChanged?.Invoke(this, files);
            }
            return;
        }
        _lastFiles = string.Empty;

        var text = ReadText();
        if (!string.IsNullOrEmpty(text) && text != _lastText)
        {
            _lastText = text;
            ClipboardTextChanged?.Invoke(this, text);
        }
    }

    private bool Open()
    {
        // Outro programa pode estar com a área de transferência aberta: tenta algumas vezes.
        for (var i = 0; i < 10; i++)
        {
            if (User32.OpenClipboard(_window))
            {
                return true;
            }
            Thread.Sleep(20);
        }
        return false;
    }

    private string? ReadText()
    {
        if (!IsClipboardFormatAvailable(CF_UNICODETEXT) || !Open())
        {
            return null;
        }

        try
        {
            var handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == 0)
            {
                return null;
            }
            var pointer = GlobalLock(handle);
            if (pointer == 0)
            {
                return null;
            }
            try
            {
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    private string[] ReadFiles()
    {
        if (!IsClipboardFormatAvailable(CF_HDROP) || !Open())
        {
            return [];
        }

        try
        {
            var handle = GetClipboardData(CF_HDROP);
            if (handle == 0)
            {
                return [];
            }

            var count = DragQueryFile(handle, 0xFFFFFFFF, null, 0);
            var files = new List<string>();
            for (uint i = 0; i < count && i < 100; i++)
            {
                var length = DragQueryFile(handle, i, null, 0);
                var buffer = new char[length + 1];
                DragQueryFile(handle, i, buffer, (uint)buffer.Length);
                var path = new string(buffer, 0, (int)length);
                if (File.Exists(path))
                {
                    files.Add(path);
                }
            }
            return [.. files];
        }
        finally
        {
            CloseClipboard();
        }
    }

    private void WriteText(string text)
    {
        if (!Open())
        {
            _logger.LogWarning("Could not open the clipboard to set text.");
            return;
        }

        try
        {
            EmptyClipboard();
            if (text.Length == 0)
            {
                return;
            }

            var bytes = (text.Length + 1) * 2;
            var handle = GlobalAlloc(GMEM_MOVEABLE, (nuint)bytes);
            if (handle == 0)
            {
                return;
            }
            var pointer = GlobalLock(handle);
            try
            {
                Marshal.Copy(text.ToCharArray(), 0, pointer, text.Length);
                Marshal.WriteInt16(pointer, text.Length * 2, 0);
            }
            finally
            {
                GlobalUnlock(handle);
            }

            if (SetClipboardData(CF_UNICODETEXT, handle) == 0)
            {
                GlobalFree(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _queue.CompleteAdding();
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetClipboardData(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetClipboardData(uint format, nint memory);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalFree(nint memory);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(nint drop, uint index, char[]? file, uint length);
}
