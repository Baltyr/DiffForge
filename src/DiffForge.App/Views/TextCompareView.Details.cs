using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DiffForge.Core.Compare;
using DiffForge.Core.Diff;

namespace DiffForge.App.Views;

/// <summary>Panel inferior: línea actual de cada lado (texto, hex o alineación carácter a carácter) y regla.</summary>
public sealed partial class TextCompareView
{
    private Border _detailsPanel = null!;
    private SelectableTextBlock _detailRuler = null!;
    private SelectableTextBlock _detailLeft = null!;
    private SelectableTextBlock _detailRight = null!;
    private SelectableTextBlock _hexLeft = null!;
    private SelectableTextBlock _hexRight = null!;
    private SelectableTextBlock _alignLeft = null!;
    private SelectableTextBlock _alignRight = null!;

    private Control BuildDetailsPanel()
    {
        SelectableTextBlock Line() => new() { TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(6, 0) };
        _detailRuler = Line();
        _detailRuler.Opacity = 0.5;
        _detailLeft = Line();
        _detailRight = Line();
        _hexLeft = Line();
        _hexRight = Line();
        _alignLeft = Line();
        _alignRight = Line();

        _detailsPanel = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(6, 6),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = new StackPanel { Children = { _detailRuler, _detailLeft, _detailRight, _hexLeft, _hexRight, _alignLeft, _alignRight } },
            },
        };
        _detailsPanel.Classes.Add("chrome");
        return _detailsPanel;
    }

    private void UpdateDetails()
    {
        if (_detailsPanel is null) return;
        bool any = ShowTextDetails || ShowHexDetails || ShowAlignmentDetails || ShowRuler;
        _detailsPanel.IsVisible = any;
        if (!any) return;

        var font = new FontFamily(Services.AppSettings.Current.Options.EditorFont);
        foreach (var tb in new[] { _detailRuler, _detailLeft, _detailRight, _hexLeft, _hexRight, _alignLeft, _alignRight })
        {
            tb.FontFamily = font;
            tb.FontSize = _fontSize;
        }

        int row = CurrentRow;
        var dr = RenderState.RowAt(row);
        string leftText = dr is { Left: >= 0 } && Result is { } r1 && dr.Value.Left < r1.Left.Length ? r1.Left[dr.Value.Left].Text : string.Empty;
        string rightText = dr is { Right: >= 0 } && Result is { } r2 && dr.Value.Right < r2.Right.Length ? r2.Right[dr.Value.Right].Text : string.Empty;
        var intra = RenderState.Intra(row);
        bool important = dr?.Important ?? false;

        _detailRuler.IsVisible = ShowRuler;
        if (ShowRuler) _detailRuler.Text = Ruler(Math.Max(leftText.Length, rightText.Length) + 10);

        _detailLeft.IsVisible = _detailRight.IsVisible = ShowTextDetails;
        if (ShowTextDetails)
        {
            _detailLeft.Inlines = BuildInlines(leftText, intra?.Left, RenderState.Colors, important);
            _detailRight.Inlines = BuildInlines(rightText, intra?.Right, RenderState.Colors, important);
        }

        _hexLeft.IsVisible = _hexRight.IsVisible = ShowHexDetails;
        if (ShowHexDetails)
        {
            _hexLeft.Text = "◀ " + Hex(leftText, Left.Current.Encoding);
            _hexRight.Text = "▶ " + Hex(rightText, Right.Current.Encoding);
        }

        _alignLeft.IsVisible = _alignRight.IsVisible = ShowAlignmentDetails;
        if (ShowAlignmentDetails)
        {
            var (l, r) = AlignmentStrings(leftText, rightText, intra?.CharAlignment);
            _alignLeft.Text = l;
            _alignRight.Text = r;
        }
    }

    private static string Ruler(int length)
    {
        var sb = new StringBuilder(length);
        for (int i = 1; i <= length; i++) sb.Append(i % 10 == 0 ? ((i / 10) % 10).ToString()[0] : i % 5 == 0 ? '+' : '·');
        return sb.ToString();
    }

    private static string Hex(string text, Encoding encoding)
    {
        var bytes = encoding.GetBytes(text);
        var sb = new StringBuilder(bytes.Length * 3);
        foreach (byte b in bytes.Take(4096)) sb.Append(b.ToString("X2")).Append(' ');
        return sb.ToString();
    }

    /// <summary>Ambas líneas con huecos (·) donde un carácter no tiene par, para ver la alineación.</summary>
    private static (string Left, string Right) AlignmentStrings(string left, string right, IReadOnlyList<EditOp>? ops)
    {
        if (ops is null) return (left, right);
        var l = new StringBuilder();
        var r = new StringBuilder();
        foreach (var op in ops)
        {
            switch (op.Kind)
            {
                case EditKind.Equal: l.Append(left[op.LeftIndex]); r.Append(right[op.RightIndex]); break;
                case EditKind.Delete: l.Append(left[op.LeftIndex]); r.Append('·'); break;
                case EditKind.Insert: l.Append('·'); r.Append(right[op.RightIndex]); break;
            }
        }
        return (l.ToString(), r.ToString());
    }
}
