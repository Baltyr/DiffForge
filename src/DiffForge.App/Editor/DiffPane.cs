using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Indentation;
using AvaloniaEdit.Rendering;
using DiffForge.Core.Compare;
using DiffForge.Core.Documents;

namespace DiffForge.App.Editor;

/// <summary>
/// Un lado de la comparación sobre un <see cref="TextEditor"/>. El documento del editor tiene una
/// línea por fila alineada: donde el otro lado tiene líneas que aquí no existen se inserta una
/// línea "hueco" vacía. Los huecos se rastrean por referencia de <see cref="DocumentLine"/>, que
/// AvaloniaEdit mantiene estable mientras el usuario edita.
/// </summary>
public sealed class DiffPane
{
    private readonly HashSet<DocumentLine> _fillers = [];
    private readonly Dictionary<DocumentLine, LineEnding> _endings = [];
    private readonly List<CollapsedLineSection> _collapsed = [];
    private FoldingManager? _folding;
    private DocumentLine? _originalLast;
    private bool _fileEndsWithoutNewline;
    private LineEnding _dominantEnding = LineEnding.CrLf;
    private int _suppress;

    public DiffPane(Side side, CompareRenderState state)
    {
        Side = side;
        State = state;
        Editor = new TextEditor
        {
            ShowLineNumbers = false,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"),
            FontSize = 13,
        };
        Editor.Options.AllowScrollBelowDocument = false;
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.Options.HighlightCurrentLine = true;
        Editor.Options.EnableTextDragDrop = true;
        Editor.Document.UndoStack.SizeLimit = 0;

        Margin = new DiffLineNumberMargin(this);
        Editor.TextArea.LeftMargins.Insert(0, Margin);
        Editor.TextArea.TextView.BackgroundRenderers.Add(new DiffBackgroundRenderer(this));
        Editor.TextArea.TextView.LineTransformers.Add(new DiffColorizer(this));
        Editor.TextArea.IndentationStrategy = new DefaultIndentationStrategy();

        Editor.Document.Changed += (_, _) =>
        {
            if (_suppress == 0) UserEdited?.Invoke(this);
        };
    }

    public Side Side { get; }
    public CompareRenderState State { get; }
    public TextEditor Editor { get; }
    public DiffLineNumberMargin Margin { get; }
    public TextView TextView => Editor.TextArea.TextView;
    public TextDocument Document => Editor.Document;

    /// <summary>Se dispara en cada edición hecha por el usuario (no por la reconstrucción).</summary>
    public event Action<DiffPane>? UserEdited;

    public bool IsFiller(DocumentLine line) => _fillers.Contains(line) && line.Length == 0;

    public int RowOf(DocumentLine line) => line.LineNumber - 1;

    /// <summary>Índice de línea real (base 0) de la fila, o -1 si en este lado es un hueco.</summary>
    public int LineIndexOfRow(int row) =>
        State.RowAt(row) is { } r ? (Side == Side.Left ? r.Left : r.Right) : -1;

    public LineInfo? LineInfoOfRow(int row)
    {
        int idx = LineIndexOfRow(row);
        var lines = Side == Side.Left ? State.Result?.Left : State.Result?.Right;
        return lines is not null && idx >= 0 && idx < lines.Length ? lines[idx] : null;
    }

    /// <summary>Texto del documento según el layout de filas.</summary>
    public static string BuildText(IReadOnlyList<DiffRow> rows, TextFile file, Side side)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < rows.Count; i++)
        {
            if (i > 0) sb.Append('\n');
            int idx = side == Side.Left ? rows[i].Left : rows[i].Right;
            if (idx >= 0) sb.Append(file.Lines[idx].Text);
        }
        return sb.ToString();
    }

    /// <summary>¿El documento ya tiene exactamente este layout? Evita reconstruir mientras se escribe.</summary>
    public bool LayoutMatches(IReadOnlyList<DiffRow> rows, TextFile file)
    {
        int expectedLines = Math.Max(rows.Count, 1);
        if (Document.LineCount != expectedLines) return false;
        for (int i = 0; i < rows.Count; i++)
        {
            var line = Document.GetLineByNumber(i + 1);
            int idx = Side == Side.Left ? rows[i].Left : rows[i].Right;
            bool filler = idx < 0;
            if (filler != _fillers.Contains(line)) return false;
            if (!filler && Document.GetText(line) != file.Lines[idx].Text) return false;
        }
        return true;
    }

    /// <summary>Reconstruye el documento sin pasar por el flujo de edición del usuario.</summary>
    public void Rebuild(IReadOnlyList<DiffRow> rows, TextFile file)
    {
        _suppress++;
        try
        {
            ClearFilter();
            Document.Text = BuildText(rows, file, Side);
            _fillers.Clear();
            _endings.Clear();
            _dominantEnding = file.DominantEnding;
            _fileEndsWithoutNewline = file.Lines.Count > 0 && file.Lines[^1].Ending == LineEnding.None;
            _originalLast = null;

            if (rows.Count == 0)
            {
                _fillers.Add(Document.GetLineByNumber(1));
                return;
            }
            for (int i = 0; i < rows.Count; i++)
            {
                var line = Document.GetLineByNumber(i + 1);
                int idx = Side == Side.Left ? rows[i].Left : rows[i].Right;
                if (idx < 0) { _fillers.Add(line); continue; }
                _endings[line] = file.Lines[idx].Ending;
                if (idx == file.Lines.Count - 1) _originalLast = line;
            }
        }
        finally
        {
            _suppress--;
        }
    }

    /// <summary>Contenido real actual del panel (sin huecos), preservando finales de línea.</summary>
    public TextFile Extract(TextFile template)
    {
        var real = new List<DocumentLine>(Document.LineCount);
        foreach (var line in Document.Lines)
        {
            if (!_fillers.Contains(line) || line.Length > 0) real.Add(line);
        }

        var lines = new TextLine[real.Count];
        for (int k = 0; k < real.Count; k++)
        {
            var line = real[k];
            var ending = _endings.TryGetValue(line, out var e) ? e : _dominantEnding;
            bool isLast = k == real.Count - 1;
            if (!isLast && ending == LineEnding.None && line == _originalLast) ending = _dominantEnding;
            if (isLast) ending = _fileEndsWithoutNewline ? LineEnding.None : (ending == LineEnding.None ? _dominantEnding : ending);
            lines[k] = new TextLine(Document.GetText(line), ending);
        }
        return template.WithLines(lines);
    }

    /// <summary>Oculta filas (Show Differences/Same/Context) en este panel.</summary>
    public void ApplyFilter(IReadOnlyList<(int Start, int End)> hiddenRuns, bool showCounts)
    {
        ClearFilter();
        if (hiddenRuns.Count == 0) return;

        if (showCounts)
        {
            _folding ??= FoldingManager.Install(Editor.TextArea);
            foreach (var margin in Editor.TextArea.LeftMargins.OfType<FoldingMargin>().ToList())
                Editor.TextArea.LeftMargins.Remove(margin);

            foreach (var (start, end) in hiddenRuns)
            {
                var first = Document.GetLineByNumber(start + 1);
                var last = Document.GetLineByNumber(end);
                var fold = _folding.CreateFolding(first.Offset, last.EndOffset);
                fold.Title = $"  ⋯ {end - start} línea(s) oculta(s)  ";
                fold.IsFolded = true;
            }
        }
        else
        {
            foreach (var (start, end) in hiddenRuns)
                _collapsed.Add(TextView.CollapseLines(Document.GetLineByNumber(start + 1), Document.GetLineByNumber(end)));
        }
    }

    public void ClearFilter()
    {
        foreach (var section in _collapsed) section.Uncollapse();
        _collapsed.Clear();
        _folding?.Clear();
    }

    public void Redraw()
    {
        TextView.Redraw();
        Margin.InvalidateVisual();
    }

    public void RunProgrammatic(Action action)
    {
        _suppress++;
        try { action(); }
        finally { _suppress--; }
    }
}
