using Remotely.Desktop.Native.Windows;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Remotely.Desktop.Win.Services;

/// <summary>
/// Complementa o BlockInput do Windows. O BlockInput não segura touchpads de precisão,
/// telas de toque e alguns teclados/mouses HID em notebooks. Aqui ganchos de baixo nível
/// (WH_MOUSE_LL e WH_KEYBOARD_LL) descartam toda entrada física enquanto o bloqueio estiver
/// ligado. A entrada do técnico chega por SendInput com a assinatura <see cref="RemotelyInputSignature"/>
/// e passa; o resto (inclusive teclado virtual ou outros programas simulando teclas) é descartado.
/// Os ganchos rodam numa thread própria com fila de mensagens, como o Windows exige.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class PhysicalInputBlocker : IDisposable
{
    /// <summary>Marca (dwExtraInfo) da entrada simulada pelo Remotely, para os ganchos deixarem passar.</summary>
    public const nuint RemotelyInputSignature = 0x524D5459;

    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const uint LLKHF_INJECTED = 0x10;
    private const uint LLKHF_LOWER_IL_INJECTED = 0x02;
    private const uint LLMHF_INJECTED = 0x01;
    private const uint LLMHF_LOWER_IL_INJECTED = 0x02;
    private const uint WM_QUIT = 0x0012;

    private readonly ILogger _logger;
    private readonly object _lock = new();
    private readonly LowLevelProc _keyboardProc;
    private readonly LowLevelProc _mouseProc;
    private Thread? _thread;
    private uint _threadId;
    private nint _keyboardHook;
    private nint _mouseHook;

    public PhysicalInputBlocker(ILogger logger)
    {
        _logger = logger;
        // Os delegates ficam em campos para o GC não coletar enquanto o Windows os chama.
        _keyboardProc = KeyboardHook;
        _mouseProc = MouseHook;
    }

    public bool IsActive { get; private set; }

    public void SetBlocked(bool blocked)
    {
        lock (_lock)
        {
            if (blocked == IsActive)
            {
                return;
            }

            if (blocked)
            {
                Start();
            }
            else
            {
                Stop();
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            Stop();
        }
    }

    private void Start()
    {
        using var ready = new ManualResetEventSlim(false);
        _thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            Win32Interop.SwitchToInputDesktop();

            var module = GetModuleHandle(null);
            _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, module, 0);
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module, 0);

            if (_keyboardHook == nint.Zero || _mouseHook == nint.Zero)
            {
                _logger.LogWarning(
                    "Could not install low-level input hooks.  Keyboard: {keyboard}.  Mouse: {mouse}.  Win32 error: {error}",
                    _keyboardHook != nint.Zero,
                    _mouseHook != nint.Zero,
                    Marshal.GetLastPInvokeError());
            }

            IsActive = true;
            ready.Set();

            while (GetMessage(out var msg, nint.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            if (_keyboardHook != nint.Zero)
            {
                UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = nint.Zero;
            }
            if (_mouseHook != nint.Zero)
            {
                UnhookWindowsHookEx(_mouseHook);
                _mouseHook = nint.Zero;
            }
        })
        {
            IsBackground = true,
            Name = "PhysicalInputBlocker"
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
        _logger.LogInformation("Physical input blocker started (touchpad, touch and HID devices).");
    }

    private void Stop()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WM_QUIT, nint.Zero, nint.Zero);
        }
        _thread?.Join(TimeSpan.FromSeconds(3));
        _thread = null;
        _threadId = 0;
        _logger.LogInformation("Physical input blocker stopped.");
    }

    private nint KeyboardHook(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && IsActive)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (!IsRemotelyInput(data.flags & (LLKHF_INJECTED | LLKHF_LOWER_IL_INJECTED), data.dwExtraInfo))
            {
                return 1;
            }
        }
        return CallNextHookEx(nint.Zero, code, wParam, lParam);
    }

    private nint MouseHook(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && IsActive)
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if (!IsRemotelyInput(data.flags & (LLMHF_INJECTED | LLMHF_LOWER_IL_INJECTED), data.dwExtraInfo))
            {
                return 1;
            }
        }
        return CallNextHookEx(nint.Zero, code, wParam, lParam);
    }

    private static bool IsRemotelyInput(uint injectedFlags, nint extraInfo)
    {
        return injectedFlags != 0 && (nuint)extraInfo == RemotelyInputSignature;
    }

    private delegate nint LowLevelProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? lpModuleName);
}
