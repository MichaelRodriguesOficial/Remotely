using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Remotely.Desktop.Shared.Abstractions;
using Remotely.Desktop.UI.Services;

namespace Remotely.Desktop.Win.Services;

/// <summary>
/// Tela preta no Windows: uma janela preta por cima de todos os monitores, que não entra na captura
/// de tela (SetWindowDisplayAffinity com WDA_EXCLUDEFROMCAPTURE, Windows 10 2004 ou mais novo) e deixa
/// os cliques passarem. O técnico continua vendo e usando a área de trabalho; o usuário vê só o aviso.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PrivacyScreenWin : IPrivacyScreen
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint LWA_ALPHA = 0x2;
    private const uint WDA_NONE = 0x0;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly nint HWND_TOPMOST = -1;

    private readonly IUiDispatcher _dispatcher;
    private readonly ILogger<PrivacyScreenWin> _logger;
    private Window? _window;
    private nint _hwnd;
    private Avalonia.Threading.DispatcherTimer? _topmostTimer;

    public PrivacyScreenWin(IUiDispatcher dispatcher, ILogger<PrivacyScreenWin> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public bool IsEnabled => _window is not null;

    public Task<(bool Enabled, string? Error)> SetEnabled(bool enabled)
    {
        return _dispatcher.InvokeAsync<(bool, string?)>(() =>
        {
            try
            {
                return Task.FromResult(enabled ? Show() : Hide());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while toggling the privacy screen.");
                Hide();
                return Task.FromResult<(bool, string?)>((false, "Não foi possível ligar a tela preta."));
            }
        });
    }

    private (bool, string?) Show()
    {
        if (_window is not null)
        {
            return (true, null);
        }

        var window = new Window
        {
            Title = "Manutenção em andamento",
            Background = Brushes.Black,
            SystemDecorations = SystemDecorations.None,
            ShowInTaskbar = false,
            Topmost = true,
            CanResize = false,
            ShowActivated = false,
            Width = 800,
            Height = 600,
            Content = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Manutenção em andamento",
                        FontSize = 30,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = "O suporte de TI está trabalhando neste computador. Não desligue nem reinicie.",
                        FontSize = 16,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xA1, 0xA1, 0xAA)),
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            }
        };

        window.Show();
        var hwnd = window.TryGetPlatformHandle()?.Handle ?? 0;
        if (hwnd == 0)
        {
            window.Close();
            return (false, "Não foi possível ligar a tela preta.");
        }

        // Sem isto a janela preta apareceria também para o técnico: não liga.
        if (!SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE))
        {
            window.Close();
            return (false, "Este Windows não permite a tela preta (precisa do Windows 10 versão 2004 ou mais novo).");
        }

        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
        CoverVirtualScreen(hwnd);

        _window = window;
        _hwnd = hwnd;

        // Outras janelas "sempre visíveis" podem ficar por cima: reafirma a cada 2 s.
        _topmostTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _topmostTimer.Tick += (_, _) => CoverVirtualScreen(_hwnd);
        _topmostTimer.Start();

        _logger.LogInformation("Privacy screen enabled.");
        return (true, null);
    }

    private (bool, string?) Hide()
    {
        _topmostTimer?.Stop();
        _topmostTimer = null;
        if (_window is not null)
        {
            try
            {
                SetWindowDisplayAffinity(_hwnd, WDA_NONE);
                _window.Close();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while closing the privacy screen.");
            }
            _logger.LogInformation("Privacy screen disabled.");
        }
        _window = null;
        _hwnd = 0;
        return (false, null);
    }

    /// <summary>Todos os monitores (a "tela virtual"), em pixels físicos.</summary>
    private static void CoverVirtualScreen(nint hwnd)
    {
        if (hwnd == 0)
        {
            return;
        }
        var x = GetSystemMetrics(76);  // SM_XVIRTUALSCREEN
        var y = GetSystemMetrics(77);  // SM_YVIRTUALSCREEN
        var width = GetSystemMetrics(78);  // SM_CXVIRTUALSCREEN
        var height = GetSystemMetrics(79); // SM_CYVIRTUALSCREEN
        SetWindowPos(hwnd, HWND_TOPMOST, x, y, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(nint hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
