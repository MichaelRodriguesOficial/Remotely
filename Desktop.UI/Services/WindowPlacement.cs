using Avalonia;
using Avalonia.Controls;

namespace Remotely.Desktop.UI.Services;

/// <summary>
/// Coloca janelas no canto inferior direito da área de trabalho (acima da barra de tarefas).
/// WorkingArea vem em pixels físicos e o tamanho da janela em pixels lógicos: sem converter pela escala
/// do monitor (125%, 150%...), a janela ficava cortada ou fora do canto.
/// </summary>
public static class WindowPlacement
{
    /// <summary>Controle do posicionamento de uma janela.</summary>
    public sealed class Anchor
    {
        internal Action<bool>? ResetAction { get; set; }

        /// <summary>Volta ao canto (mesmo que o usuário tenha arrastado). flushRight: colada na borda direita.</summary>
        public void Reset(bool flushRight = false) => ResetAction?.Invoke(flushRight);
    }

    public static Anchor KeepBottomRight(Window window, int margin = 12, int bottomOffset = 0)
    {
        var anchor = new Anchor();
        var userMoved = false;
        var flush = false;

        void Place()
        {
            if (userMoved)
            {
                return;
            }

            var screen = window.Screens.Primary ?? window.Screens.All.FirstOrDefault();
            if (screen is null)
            {
                return;
            }

            var size = window.FrameSize ?? window.ClientSize;
            if (size.Width <= 0 || size.Height <= 0)
            {
                return;
            }

            var scaling = screen.Scaling > 0 ? screen.Scaling : 1;
            var width = (int)Math.Ceiling(size.Width * scaling);
            var height = (int)Math.Ceiling(size.Height * scaling);
            var area = screen.WorkingArea;
            var gap = (int)Math.Round(margin * scaling);

            window.Position = new PixelPoint(
                Math.Max(area.X, area.Right - width - (flush ? 0 : gap)),
                Math.Max(area.Y, area.Bottom - height - gap - (int)Math.Round(bottomOffset * scaling)));
        }

        anchor.ResetAction = flushRight =>
        {
            userMoved = false;
            flush = flushRight;
            Place();
            // O tamanho muda depois do layout (SizeToContent): posiciona de novo.
            Avalonia.Threading.Dispatcher.UIThread.Post(Place, Avalonia.Threading.DispatcherPriority.Background);
        };

        window.Opened += (_, _) =>
        {
            Place();
            // O tamanho final só sai depois do primeiro layout.
            Avalonia.Threading.Dispatcher.UIThread.Post(Place, Avalonia.Threading.DispatcherPriority.Background);
        };
        window.SizeChanged += (_, _) => Place();
        window.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(window).Properties.IsLeftButtonPressed)
            {
                userMoved = true;
            }
        };

        return anchor;
    }
}
