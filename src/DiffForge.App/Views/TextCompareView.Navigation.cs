using AvaloniaEdit;
using DiffForge.App.Editor;
using DiffForge.App.Services;
using DiffForge.Core.Compare;

namespace DiffForge.App.Views;

public sealed partial class TextCompareView
{
    /// <summary>Secciones a las que navega "siguiente diferencia" según el modo de importancia.</summary>
    private List<DiffSection> NavigableSections() =>
        Result?.Sections.Where(s => s.Important || !RenderState.IgnoreUnimportant).ToList() ?? [];

    private DiffSection? SectionAt(int row)
    {
        foreach (var s in NavigableSections())
        {
            if (row >= s.Start && row < s.End) return s;
            if (s.Start > row) break;
        }
        return null;
    }

    /// <summary>Posiciona el caret en una fila y la deja a un tercio de la altura visible.</summary>
    public void GoToRow(int row, DiffPane? pane = null, int column = 1, bool select = false, int length = 0)
    {
        pane ??= ActivePane;
        if (pane.Document.LineCount == 0) return;
        row = Math.Clamp(row, 0, pane.Document.LineCount - 1);
        var line = pane.Document.GetLineByNumber(row + 1);
        column = Math.Clamp(column, 1, line.Length + 1);

        pane.Editor.TextArea.Caret.Position = new TextViewPosition(row + 1, column);
        if (select && length > 0)
            pane.Editor.Select(line.Offset + column - 1, Math.Min(length, line.EndOffset - (line.Offset + column - 1)));
        else
            pane.Editor.TextArea.ClearSelection();

        var tv = pane.TextView;
        double top = tv.GetVisualTopByDocumentLine(row + 1);
        if (top < tv.VerticalOffset || top > tv.VerticalOffset + tv.Bounds.Height - tv.DefaultLineHeight * 2)
            pane.Editor.ScrollToVerticalOffset(Math.Max(0, top - tv.Bounds.Height / 3));
        pane.Editor.TextArea.Focus();
        ActivePane = pane;
        SyncScroll(pane, pane == LeftPane ? RightPane : LeftPane);
        OnCaretMoved();
    }

    private void GoToSection(DiffSection? section)
    {
        if (section is { } s) GoToRow(s.Start);
    }

    private void NoMore(string what)
    {
        if (AppSettings.Current.Options.ShowNoMoreDifferencesMessage) SetMessage($"No hay más {what}.");
    }

    private void NextSection(bool forward)
    {
        var sections = NavigableSections();
        if (sections.Count == 0) { NoMore("diferencias"); return; }
        int row = CurrentRow;
        var current = SectionAt(row);

        int index = forward
            ? sections.FindIndex(s => s.Start > row && (current is null || s.Start >= current.Value.End))
            : sections.FindLastIndex(s => s.End <= (current?.Start ?? row + 1) && s.Start < row);
        DiffSection? target = index >= 0 ? sections[index] : null;
        if (target is null)
        {
            if (AppSettings.Current.Options.WrapAroundDifferences)
                target = forward ? sections[0] : sections[^1];
            else
            {
                NoMore("diferencias en esa dirección");
                return;
            }
        }
        GoToSection(target);
    }

    /// <summary>Siguiente/anterior tramo con diferencia, incluso dentro de una misma línea larga.</summary>
    private void NextDifference(bool forward)
    {
        var rows = RenderState.Rows;
        var pane = ActivePane;
        int row = CurrentRow;
        int col = pane.Editor.TextArea.Caret.Column - 1;

        IEnumerable<int> order = forward
            ? Enumerable.Range(row, Math.Max(0, rows.Count - row))
            : Enumerable.Range(0, Math.Min(rows.Count, row + 1)).Reverse();

        foreach (int r in order)
        {
            var dr = rows[r];
            if (!RenderState.IsDisplayedAsDifference(dr)) continue;
            var starts = SpanStarts(pane, r);
            int? target = forward
                ? starts.Where(s => r > row || s > col).Cast<int?>().FirstOrDefault()
                : starts.Where(s => r < row || s < col).Cast<int?>().LastOrDefault();
            if (target is { } t) { GoToRow(r, pane, t + 1); return; }
        }
        NoMore("diferencias");
    }

    private List<int> SpanStarts(DiffPane pane, int row)
    {
        var dr = RenderState.Rows[row];
        bool hasLine = pane.Side == Side.Left ? dr.Left >= 0 : dr.Right >= 0;
        if (!hasLine || dr.Kind != RowKind.Changed) return [0];
        var intra = RenderState.Intra(row);
        var spans = intra is null ? [] : pane.Side == Side.Left ? intra.Left : intra.Right;
        var starts = spans.Where(s => s.Kind != SpanKind.Unimportant || !RenderState.IgnoreUnimportant).Select(s => s.Start).ToList();
        return starts.Count > 0 ? starts : [0];
    }

    private void NextRowMatching(bool forward, Func<int, bool> predicate, string what)
    {
        var rows = RenderState.Rows;
        int row = CurrentRow;
        // Avanza sobre el bloque actual para no quedarse en la misma edición.
        int i = row;
        while (i >= 0 && i < rows.Count && predicate(i)) i += forward ? 1 : -1;
        for (; i >= 0 && i < rows.Count; i += forward ? 1 : -1)
        {
            if (!predicate(i)) continue;
            if (!forward) while (i > 0 && predicate(i - 1)) i--;
            GoToRow(i);
            return;
        }
        NoMore(what);
    }

    private bool IsEditedRow(int row)
    {
        int real = ActivePane.LineIndexOfRow(row);
        return real >= 0 && RenderState.Edited(ActivePane.Side).Contains(real);
    }

    private bool IsReplacementRow(int row) =>
        RenderState.RowAt(row) is { Kind: RowKind.Changed } && RenderState.Intra(row) is { } intra
        && intra.Left.Concat(intra.Right).Any(s => s.Kind == SpanKind.Replacement);

    private void ToggleBookmark(int digit)
    {
        int real = ActivePane.LineIndexOfRow(CurrentRow);
        if (real < 0) { SetMessage("No se puede marcar un hueco."); return; }
        if (RenderState.Bookmarks.TryGetValue(digit, out var existing) && existing == (ActivePane.Side, real))
            RenderState.Bookmarks.Remove(digit);
        else
            RenderState.Bookmarks[digit] = (ActivePane.Side, real);
        RedrawAll();
    }

    private void GoToBookmark(int digit)
    {
        if (!RenderState.Bookmarks.TryGetValue(digit, out var mark)) { SetMessage($"El marcador {digit} no está definido."); return; }
        var pane = mark.Side == Side.Left ? LeftPane : RightPane;
        int row = RowOfLine(mark.Side, mark.Line);
        if (row >= 0) GoToRow(row, pane);
    }

    private int RowOfLine(Side side, int line)
    {
        var rows = RenderState.Rows;
        for (int i = 0; i < rows.Count; i++)
            if ((side == Side.Left ? rows[i].Left : rows[i].Right) == line) return i;
        return -1;
    }

    /// <summary>Filas afectadas por un comando: la selección (si abarca líneas) o la sección actual.</summary>
    private (int Start, int End)? TargetRows(bool allowSingleLine = false)
    {
        var area = ActivePane.Editor.TextArea;
        if (!area.Selection.IsEmpty)
        {
            var seg = area.Selection.SurroundingSegment;
            int first = ActivePane.Document.GetLineByOffset(seg.Offset).LineNumber - 1;
            var lastLine = ActivePane.Document.GetLineByOffset(seg.EndOffset);
            int last = lastLine.LineNumber - 1;
            if (seg.EndOffset == lastLine.Offset && last > first) last--;
            return (first, last + 1);
        }
        if (Result?.Sections.FirstOrDefault(s => CurrentRow >= s.Start && CurrentRow < s.End) is { End: > 0 } section)
            return (section.Start, section.End);
        if (allowSingleLine && CurrentRow >= 0 && CurrentRow < RenderState.Rows.Count) return (CurrentRow, CurrentRow + 1);
        return null;
    }
}
