using System.Text.RegularExpressions;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;

namespace Remotely.Desktop.UI.Views;

/// <summary>Texto da mensagem com links clicáveis, como no visualizador. Selecionável para copiar.</summary>
public partial class ChatText : SelectableTextBlock
{
    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<ChatText, string?>(nameof(Message));

    public static readonly StyledProperty<ICommand?> LinkCommandProperty =
        AvaloniaProperty.Register<ChatText, ICommand?>(nameof(LinkCommand));

    private readonly List<(int Start, int End, string Url)> _links = [];

    static ChatText()
    {
        MessageProperty.Changed.AddClassHandler<ChatText>((x, _) => x.BuildInlines());
    }

    protected override Type StyleKeyOverride => typeof(SelectableTextBlock);

    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public ICommand? LinkCommand
    {
        get => GetValue(LinkCommandProperty);
        set => SetValue(LinkCommandProperty, value);
    }

    [GeneratedRegex(@"https?://[^\s<]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Cursor = LinkAt(e.GetPosition(this)) is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left || !string.IsNullOrEmpty(SelectedText))
        {
            return;
        }
        if (LinkAt(e.GetPosition(this)) is { } url && LinkCommand?.CanExecute(url) == true)
        {
            LinkCommand.Execute(url);
        }
    }

    private string? LinkAt(Point point)
    {
        if (_links.Count == 0)
        {
            return null;
        }
        var hit = TextLayout.HitTestPoint(new Point(point.X - Padding.Left, point.Y - Padding.Top));
        if (!hit.IsInside)
        {
            return null;
        }
        foreach (var (start, end, url) in _links)
        {
            if (hit.TextPosition >= start && hit.TextPosition < end)
            {
                return url;
            }
        }
        return null;
    }

    private void BuildInlines()
    {
        _links.Clear();
        var text = Message ?? string.Empty;
        var inlines = new InlineCollection();
        var last = 0;
        foreach (Match match in UrlPattern().Matches(text))
        {
            if (match.Index > last)
            {
                inlines.Add(new Run(text[last..match.Index]));
            }
            inlines.Add(new Run(match.Value) { TextDecorations = Avalonia.Media.TextDecorations.Underline });
            _links.Add((match.Index, match.Index + match.Length, match.Value));
            last = match.Index + match.Length;
        }
        if (last < text.Length)
        {
            inlines.Add(new Run(text[last..]));
        }
        Inlines = inlines;
    }
}
