using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using DiffForge.Core.Compare;

namespace DiffForge.App.Editor;

/// <summary>Fondo de línea por tipo de diferencia, huecos, sección actual y búsqueda.</summary>
internal sealed class DiffBackgroundRenderer(DiffPane pane) : IBackgroundRenderer
{
    private static readonly IPen SectionPen = new Pen(new SolidColorBrush(Color.FromArgb(160, 128, 128, 128)), 1);

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext dc)
    {
        var state = pane.State;
        var colors = state.Colors;
        if (!textView.VisualLinesValid) return;
        double width = Math.Max(textView.Bounds.Width, textView.DocumentHeight > 0 ? textView.Bounds.Width : 0);

        foreach (var vl in textView.VisualLines)
        {
            var line = vl.FirstDocumentLine;
            int rowIndex = pane.RowOf(line);
            var rect = new Rect(0, vl.VisualTop - textView.VerticalOffset, width, vl.Height);

            if (state.UseStripes && rowIndex % 2 == 1) dc.FillRectangle(colors.Stripes, rect);
            if (state.RowAt(rowIndex) is not { } row) continue;

            if (pane.IsFiller(line))
            {
                dc.FillRectangle(colors.Filler, rect);
                continue;
            }

            if (state.IsDisplayedAsDifference(row))
                dc.FillRectangle(row.Important ? colors.ImportantLine : colors.UnimportantLine, rect);

            if (row.Important && row.Kind == RowKind.Changed && state.Intra(rowIndex) is { } intra && LineMatches(rowIndex, line))
            {
                var spans = pane.Side == Side.Left ? intra.Left : intra.Right;
                foreach (var span in spans)
                {
                    if (span.Kind != SpanKind.Important) continue;
                    FillSegment(textView, dc, colors.ImportantTextBack, line.Offset + span.Start, span.Length, line);
                }
            }
        }

        foreach (var hit in state.SearchHits)
        {
            if (hit.Side != pane.Side || hit.Row >= pane.Document.LineCount) continue;
            var line = pane.Document.GetLineByNumber(hit.Row + 1);
            FillSegment(textView, dc, colors.SearchMatch, line.Offset + hit.Start, hit.Length, line);
        }

        if (state.CurrentSection is { } section && section.End > section.Start && section.End <= pane.Document.LineCount)
        {
            double top = textView.GetVisualTopByDocumentLine(section.Start + 1) - textView.VerticalOffset;
            double bottom = textView.GetVisualTopByDocumentLine(section.End) - textView.VerticalOffset
                + textView.DefaultLineHeight;
            dc.DrawRectangle(null, SectionPen, new Rect(0.5, top + 0.5, Math.Max(0, width - 1), Math.Max(0, bottom - top - 1)));
        }
    }

    private bool LineMatches(int rowIndex, DocumentLine line) =>
        pane.LineInfoOfRow(rowIndex) is { } info && info.Text.Length == line.Length;

    private static void FillSegment(TextView textView, DrawingContext dc, IBrush brush, int offset, int length, DocumentLine line)
    {
        if (length <= 0 || offset + length > line.EndOffset) return;
        var builder = new BackgroundGeometryBuilder { AlignToWholePixels = true };
        builder.AddSegment(textView, new TextSegment { StartOffset = offset, Length = length });
        if (builder.CreateGeometry() is { } geometry) dc.DrawGeometry(brush, null, geometry);
    }
}

/// <summary>Colorea sintaxis (elementos de gramática) y texto con diferencias.</summary>
internal sealed class DiffColorizer(DiffPane pane) : DocumentColorizingTransformer
{
    protected override void ColorizeLine(DocumentLine line)
    {
        var state = pane.State;
        int rowIndex = pane.RowOf(line);
        if (pane.LineInfoOfRow(rowIndex) is not { } info || info.Text.Length != line.Length) return;
        if (CurrentContext.Document.GetText(line) != info.Text) return;

        var colors = state.Colors;
        if (state.SyntaxHighlighting)
        {
            foreach (var span in info.Grammar)
            {
                if (colors.Element(span.Element) is not { } brush) continue;
                int start = line.Offset + span.Start;
                int end = Math.Min(line.EndOffset, start + span.Length);
                if (end > start) ChangeLinePart(start, end, e => e.TextRunProperties.SetForegroundBrush(brush));
            }
        }

        if (state.RowAt(rowIndex) is not { } row || !state.IsDisplayedAsDifference(row)) return;

        if (row.Kind is RowKind.LeftOnly or RowKind.RightOnly)
        {
            if (line.Length > 0)
            {
                var brush = row.Important ? colors.ImportantText : colors.UnimportantText;
                ChangeLinePart(line.Offset, line.EndOffset, e => e.TextRunProperties.SetForegroundBrush(brush));
            }
            return;
        }

        if (state.Intra(rowIndex) is not { } intra) return;
        foreach (var span in pane.Side == Side.Left ? intra.Left : intra.Right)
        {
            var brush = span.Kind switch
            {
                SpanKind.Replacement => colors.ReplacementText,
                SpanKind.Important when row.Important => colors.ImportantText,
                _ => colors.UnimportantText,
            };
            if (span.Kind == SpanKind.Unimportant && state.IgnoreUnimportant) continue;
            int start = line.Offset + span.Start;
            int end = Math.Min(line.EndOffset, start + span.Length);
            if (end > start) ChangeLinePart(start, end, e => e.TextRunProperties.SetForegroundBrush(brush));
        }
    }
}

/// <summary>Números de línea reales (los huecos no numeran), marca de editado y marcadores.</summary>
public sealed class DiffLineNumberMargin : AbstractMargin
{
    private readonly DiffPane _pane;
    private double _width = 40;

    public DiffLineNumberMargin(DiffPane pane)
    {
        _pane = pane;
        ClipToBounds = true;
    }

    public bool ShowNumbers { get; set; } = true;

    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView is not null) oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null) newTextView.VisualLinesChanged += OnVisualLinesChanged;
        InvalidateVisual();
    }

    private void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

    protected override Size MeasureOverride(Size availableSize)
    {
        if (!ShowNumbers) return new Size(14, 0);
        int maxLine = Math.Max(_pane.Document.LineCount, 1);
        var text = Format(new string('9', maxLine.ToString().Length));
        _width = text.Width + 22;
        return new Size(_width, 0);
    }

    public override void Render(DrawingContext context)
    {
        var textView = TextView;
        if (textView is null || !textView.VisualLinesValid) return;

        var state = _pane.State;
        var edited = state.Edited(_pane.Side);
        var foreground = new SolidColorBrush(Color.FromArgb(150, 128, 128, 128));
        double width = Bounds.Width;

        foreach (var vl in textView.VisualLines)
        {
            var line = vl.FirstDocumentLine;
            int row = _pane.RowOf(line);
            int real = _pane.IsFiller(line) ? -1 : _pane.LineIndexOfRow(row);
            double y = vl.VisualTop - textView.VerticalOffset;

            if (real >= 0 && edited.Contains(real))
                context.FillRectangle(state.Colors.EditedMarker, new Rect(width - 4, y, 3, vl.Height));

            foreach (var (digit, mark) in state.Bookmarks)
            {
                if (mark.Side == _pane.Side && mark.Line == real && real >= 0)
                {
                    context.FillRectangle(Brushes.SteelBlue, new Rect(1, y + 1, 10, vl.Height - 2));
                    context.DrawText(Format(digit.ToString(), Brushes.White, 9), new Point(3, y + 2));
                }
            }

            if (ShowNumbers && real >= 0)
            {
                var text = Format((real + 1).ToString(), foreground);
                context.DrawText(text, new Point(width - 8 - text.Width, y + (vl.Height - text.Height) / 2));
            }
        }
    }

    private FormattedText Format(string text, IBrush? brush = null, double? size = null) =>
        new(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(_pane.Editor.FontFamily), size ?? _pane.Editor.FontSize, brush ?? Brushes.Gray);
}
