using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using DiffForge.App.Editor;
using DiffForge.App.Services;
using DiffForge.Core.Compare;
using DiffForge.Core.Diff;
using DiffForge.Core.Documents;

namespace DiffForge.App.Views;

public enum ViewFilter { All, Differences, Same, Context, None }

/// <summary>
/// Sesión de Text Compare: dos editores alineados, miniatura, panel de detalles, búsqueda y
/// barra de estado. La lógica de comandos está en los archivos parciales.
/// </summary>
public sealed partial class TextCompareView : UserControl
{
    private readonly DispatcherTimer _debounce;
    private readonly Stack<Snapshot> _undo = new();
    private readonly Stack<Snapshot> _redo = new();
    private readonly Grid _panesGrid = new();
    private readonly ThumbnailControl _thumbnail = new();
    private readonly Border _leftHost = new();
    private readonly Border _rightHost = new();
    private readonly GridSplitter _splitter = new();
    private readonly SideHeader _leftHeader;
    private readonly SideHeader _rightHeader;
    private readonly TextBlock _statusSections = StatusText();
    private readonly TextBlock _statusRow = StatusText();
    private readonly TextBlock _statusPosition = StatusText();
    private readonly TextBlock _statusMode = StatusText();
    private readonly TextBlock _statusMessage = StatusText();
    private readonly TextBlock _statusTiming = StatusText();

    private double _fontSize = AppSettings.Current.Options.EditorFontSize;
    private int _version;
    private bool _leftDirty;
    private bool _rightDirty;
    private bool _editBurstOpen;
    private bool _syncingScroll;
    private bool _firstResult = true;
    private (Side Side, int Line)? _alignWithPending;

    private record Snapshot(TextFile Left, TextFile Right, bool LeftModified, bool RightModified);

    public TextCompareView()
    {
        var options = AppSettings.Current.Options;
        Settings = AppSettings.DeepClone(AppSettings.Current.DefaultCompareSettings);
        RenderState = new CompareRenderState { SyntaxHighlighting = options.SyntaxHighlighting, UseStripes = options.UseStripes };
        LeftPane = new DiffPane(Side.Left, RenderState);
        RightPane = new DiffPane(Side.Right, RenderState);
        ActivePane = LeftPane;

        ShowLineNumbers = options.ShowLineNumbers;
        ShowThumbnail = options.ShowThumbnail;
        ShowTextDetails = options.ShowTextDetails;
        ShowFileInfo = options.ShowFileInfo;

        _leftHeader = new SideHeader(this, Side.Left);
        _rightHeader = new SideHeader(this, Side.Right);

        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _debounce.Tick += async (_, _) => await RecompareAsync();

        foreach (var pane in new[] { LeftPane, RightPane })
        {
            pane.UserEdited += OnUserEdited;
            pane.TextView.ScrollOffsetChanged += (_, _) => OnScrolled(pane);
            pane.Editor.TextArea.GotFocus += (_, _) =>
            {
                ActivePane = pane;
                // Solo el panel activo resalta su línea actual; el otro mostraría una fila sin relación.
                pane.Editor.Options.HighlightCurrentLine = true;
                OtherPane.Editor.Options.HighlightCurrentLine = false;
                UpdateStatus();
            };
            pane.Editor.TextArea.Caret.PositionChanged += (_, _) => { if (pane == ActivePane) OnCaretMoved(); };
            pane.Editor.TextArea.AddHandler(PointerReleasedEvent, (_, _) => OnPanePointerReleased(pane), RoutingStrategies.Tunnel, handledEventsToo: true);
            pane.Editor.TextArea.AddHandler(KeyDownEvent, (s, e) => OnEditorKeyDown(pane, e), RoutingStrategies.Tunnel);
            DragDrop.SetAllowDrop(pane.Editor, true);
            pane.Editor.AddHandler(DragDrop.DropEvent, (_, e) => OnDrop(pane.Side, e));
            pane.Editor.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None);
        }

        _thumbnail.State = RenderState;
        _thumbnail.Navigate += fraction =>
        {
            var tv = LeftPane.TextView;
            LeftPane.Editor.ScrollToVerticalOffset(fraction * tv.DocumentHeight - tv.Bounds.Height / 2);
        };

        BuildLayout();
        ApplyAppearance();
        AppSettings.Changed += ApplyAppearance;
        ActualThemeVariantChanged += (_, _) => ApplyAppearance();
        DetachedFromVisualTree += (_, _) => AppSettings.Changed -= ApplyAppearance;
    }

    // ---------- Estado público ----------

    public SideDocument Left { get; private set; } = new();
    public SideDocument Right { get; private set; } = new();
    public CompareSettings Settings { get; private set; }
    public CompareRenderState RenderState { get; }
    public DiffPane LeftPane { get; }
    public DiffPane RightPane { get; }
    public DiffPane ActivePane { get; private set; }
    public DiffPane OtherPane => ActivePane == LeftPane ? RightPane : LeftPane;

    public ViewFilter Filter { get; private set; } = ViewFilter.All;
    public bool OverUnder { get; private set; }
    public bool WordWrap { get; private set; }
    public bool VisibleWhitespace { get; private set; }
    public bool ShowLineNumbers { get; private set; }
    public bool ShowThumbnail { get; private set; }
    public bool ShowTextDetails { get; private set; }
    public bool ShowHexDetails { get; private set; }
    public bool ShowAlignmentDetails { get; private set; }
    public bool ShowRuler { get; private set; }
    public bool ShowFileInfo { get; private set; }
    public bool Locked { get; set; }
    public bool DisableEditing { get; set; }
    public bool LeftReadOnlyArg { get; set; }
    public bool RightReadOnlyArg { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? SessionPath { get; set; }
    public ComparisonResult? Result => RenderState.Result;

    public bool IsModified => Left.IsModified || Right.IsModified || _leftDirty || _rightDirty;

    public string TabTitle
    {
        get
        {
            string l = (Left.IsModified || _leftDirty ? "*" : "") + Left.DisplayName;
            string r = (Right.IsModified || _rightDirty ? "*" : "") + Right.DisplayName;
            return SessionPath is not null ? Path.GetFileNameWithoutExtension(SessionPath) : $"{l} ↔ {r}";
        }
    }

    public event Action? TitleChanged;
    public event Action<SideDocument, SideDocument>? NewComparisonRequested;

    private Window? OwnerWindow => TopLevel.GetTopLevel(this) as Window;

    // ---------- Layout ----------

    private static TextBlock StatusText() => new() { Margin = new Thickness(8, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };

    private void BuildLayout()
    {
        var root = new DockPanel();

        var findBar = BuildFindPanel();
        DockPanel.SetDock(findBar, Dock.Top);
        root.Children.Add(findBar);

        var status = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(4, 4),
            Child = new DockPanel
            {
                Children =
                {
                    Docked(_statusTiming, Avalonia.Controls.Dock.Right),
                    Docked(_statusMode, Avalonia.Controls.Dock.Right),
                    Docked(_statusPosition, Avalonia.Controls.Dock.Right),
                    Docked(_statusSections, Avalonia.Controls.Dock.Left),
                    Docked(_statusRow, Avalonia.Controls.Dock.Left),
                    _statusMessage,
                },
            },
        };
        status.Classes.Add("chrome");
        foreach (var t in new[] { _statusRow, _statusPosition, _statusMode, _statusTiming }) t.Classes.Add("muted");
        DockPanel.SetDock(status, Avalonia.Controls.Dock.Bottom);
        root.Children.Add(status);

        var details = BuildDetailsPanel();
        DockPanel.SetDock(details, Avalonia.Controls.Dock.Bottom);
        root.Children.Add(details);

        DockPanel.SetDock(_thumbnail, Avalonia.Controls.Dock.Left);
        root.Children.Add(_thumbnail);

        _leftHost.Child = SidePanel(_leftHeader, LeftPane);
        _rightHost.Child = SidePanel(_rightHeader, RightPane);
        _splitter.Classes.Add("panes");
        root.Children.Add(_panesGrid);
        ArrangePanes();

        Content = root;
    }

    private static Control Docked(Control c, Dock dock)
    {
        DockPanel.SetDock(c, dock);
        return c;
    }

    private static Control SidePanel(SideHeader header, DiffPane pane)
    {
        var panel = new DockPanel();
        DockPanel.SetDock(header, Avalonia.Controls.Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(pane.Editor);
        return panel;
    }

    private void ArrangePanes()
    {
        _panesGrid.Children.Clear();
        _panesGrid.RowDefinitions.Clear();
        _panesGrid.ColumnDefinitions.Clear();
        if (OverUnder)
        {
            _panesGrid.RowDefinitions = new RowDefinitions("*,3,*");
            _splitter.ResizeDirection = GridResizeDirection.Rows;
            Grid.SetRow(_leftHost, 0); Grid.SetColumn(_leftHost, 0);
            Grid.SetRow(_splitter, 1); Grid.SetColumn(_splitter, 0);
            Grid.SetRow(_rightHost, 2); Grid.SetColumn(_rightHost, 0);
        }
        else
        {
            _panesGrid.ColumnDefinitions = new ColumnDefinitions("*,3,*");
            _splitter.ResizeDirection = GridResizeDirection.Columns;
            Grid.SetColumn(_leftHost, 0); Grid.SetRow(_leftHost, 0);
            Grid.SetColumn(_splitter, 1); Grid.SetRow(_splitter, 0);
            Grid.SetColumn(_rightHost, 2); Grid.SetRow(_rightHost, 0);
        }
        _panesGrid.Children.Add(_leftHost);
        _panesGrid.Children.Add(_splitter);
        _panesGrid.Children.Add(_rightHost);
    }

    private void ApplyAppearance()
    {
        var options = AppSettings.Current.Options;
        bool dark = ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        RenderState.SetColors(dark ? options.Colors.Dark : options.Colors.Light);
        RenderState.UseStripes = options.UseStripes;
        foreach (var pane in new[] { LeftPane, RightPane })
        {
            pane.Editor.FontFamily = new FontFamily(options.EditorFont);
            pane.Editor.FontSize = _fontSize;
            pane.Editor.TextArea.IndentationStrategy = options.AutoIndent ? new AvaloniaEdit.Indentation.DefaultIndentationStrategy() : null;
        }
        ApplyViewToggles();
        RedrawAll();
    }


    private void ApplyViewToggles()
    {
        foreach (var (pane, side) in new[] { (LeftPane, Left), (RightPane, Right) })
        {
            pane.Editor.WordWrap = WordWrap;
            pane.Editor.Options.ShowSpaces = VisibleWhitespace;
            pane.Editor.Options.ShowTabs = VisibleWhitespace;
            pane.Editor.Options.ShowEndOfLine = VisibleWhitespace;
            pane.Editor.Options.IndentationSize = side.Format.TabStop;
            pane.Editor.Options.ConvertTabsToSpaces = side.Format.InsertSpacesInsteadOfTabs;
            pane.Editor.IsReadOnly = IsSideReadOnly(pane.Side);
            pane.Margin.ShowNumbers = ShowLineNumbers;
            pane.Margin.InvalidateMeasure();
        }
        _thumbnail.IsVisible = ShowThumbnail;
        _leftHeader.ShowInfo = ShowFileInfo;
        _rightHeader.ShowInfo = ShowFileInfo;
        UpdateDetails();
    }

    public bool IsSideReadOnly(Side side) =>
        DisableEditing || (side == Side.Left ? Left.SourceReadOnly || LeftReadOnlyArg : Right.SourceReadOnly || RightReadOnlyArg);

    // ---------- Carga ----------

    public async Task LoadAsync(string? leftSpec, string? rightSpec)
    {
        if (!string.IsNullOrWhiteSpace(leftSpec)) Left = await LoadSideAsync(leftSpec, Left);
        if (!string.IsNullOrWhiteSpace(rightSpec)) Right = await LoadSideAsync(rightSpec, Right);
        ResetHistory();
        _firstResult = true;
        await RecompareAsync(forceRebuild: true);
    }

    public async Task SetDocumentsAsync(SideDocument left, SideDocument right)
    {
        Left = left;
        Right = right;
        ResetHistory();
        _firstResult = true;
        await RecompareAsync(forceRebuild: true);
    }

    private async Task<SideDocument> LoadSideAsync(string spec, SideDocument previous)
    {
        try
        {
            spec = spec.Trim().Trim('"');
            // Rutas locales en forma canónica del sistema (C:/a/b → C:\a\b); las URL quedan igual.
            if (!spec.Contains("://")) spec = Path.GetFullPath(spec);
            var doc = await DocumentService.LoadAsync(spec, previous.FormatOverride, previous.EncodingOverride);
            AddRecent(doc.Spec);
            return doc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or InvalidOperationException or InvalidDataException or ArgumentException)
        {
            SetMessage($"No se pudo abrir '{spec}': {ex.Message}");
            return new SideDocument { Spec = spec, LoadError = ex.Message, SourceReadOnly = true };
        }
    }

    public async Task OpenIntoSideAsync(Side side, string spec)
    {
        if (Locked) { SetMessage("La sesión está bloqueada."); return; }
        await FlushAsync();
        var current = side == Side.Left ? Left : Right;
        if (!await ConfirmDiscardAsync(current)) return;
        var doc = await LoadSideAsync(spec, current);
        if (side == Side.Left) Left = doc; else Right = doc;
        ResetHistory();
        _firstResult = true;
        await RecompareAsync(forceRebuild: true);
    }

    public async Task SetSideTextAsync(Side side, string text, string title)
    {
        await FlushAsync();
        var current = side == Side.Left ? Left : Right;
        if (!await ConfirmDiscardAsync(current)) return;
        var doc = DocumentService.FromText(text, title);
        if (side == Side.Left) Left = doc; else Right = doc;
        ResetHistory();
        await RecompareAsync(forceRebuild: true);
    }

    private static void AddRecent(string spec)
    {
        var recent = AppSettings.Current.RecentFiles;
        recent.Remove(spec);
        recent.Insert(0, spec);
        if (recent.Count > 20) recent.RemoveRange(20, recent.Count - 20);
    }

    private void ResetHistory()
    {
        _undo.Clear();
        _redo.Clear();
        _editBurstOpen = false;
        _alignWithPending = null;
        RenderState.Bookmarks.Clear();
    }

    private async void OnDrop(Side side, DragEventArgs e)
    {
        var files = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).Where(p => p is not null).Cast<string>().ToList();
        if (files is null || files.Count == 0) return;
        if (files.Count >= 2) await LoadAsync(files[0], files[1]);
        else await OpenIntoSideAsync(side, files[0]);
    }

    // ---------- Comparación y edición ----------

    private void OnUserEdited(DiffPane pane)
    {
        if (!_editBurstOpen)
        {
            PushUndo();
            _editBurstOpen = true;
        }
        if (pane.Side == Side.Left) _leftDirty = true; else _rightDirty = true;
        _version++; // invalida cualquier comparación en curso basada en texto viejo
        _debounce.Stop();
        _debounce.Start();
        TitleChanged?.Invoke();
    }

    /// <summary>Si hay ediciones pendientes, recompara ya para que los comandos operen sobre el texto vigente.</summary>
    public async Task FlushAsync()
    {
        if (_leftDirty || _rightDirty || _debounce.IsEnabled) await RecompareAsync();
    }

    private void ExtractDirtyPanes()
    {
        if (_leftDirty)
        {
            Left.Current = LeftPane.Extract(Left.Current);
            Left.IsModified = true;
            _leftDirty = false;
        }
        if (_rightDirty)
        {
            Right.Current = RightPane.Extract(Right.Current);
            Right.IsModified = true;
            _rightDirty = false;
        }
    }

    public async Task RecompareAsync(bool forceRebuild = false)
    {
        _debounce.Stop();
        ExtractDirtyPanes();
        int version = ++_version;
        var (lf, rf) = (Left.Current, Right.Current);
        var settings = Settings.Clone();
        var (lfmt, rfmt) = (Left.Format, Right.Format);

        SetMessage("Comparando…");
        ComparisonResult result;
        try
        {
            result = await Task.Run(() => TextComparer.Compare(lf, rf, settings, lfmt, rfmt));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            SetMessage($"Error al comparar: {ex.Message}");
            return;
        }
        if (version != _version) return;

        ApplyResult(result, lf, rf, forceRebuild);
        _editBurstOpen = false;
        SetMessage(Left.LoadError ?? Right.LoadError ?? string.Empty);
    }

    private void ApplyResult(ComparisonResult result, TextFile lf, TextFile rf, bool forceRebuild)
    {
        var caret = CaptureCaret();
        double vOffset = LeftPane.TextView.VerticalOffset;
        double hOffset = LeftPane.TextView.HorizontalOffset;

        RenderState.SetResult(result);
        RenderState.SearchHits.Clear();
        bool rebuilt = false;
        if (forceRebuild || !LeftPane.LayoutMatches(result.Rows, lf)) { LeftPane.Rebuild(result.Rows, lf); rebuilt = true; }
        if (forceRebuild || !RightPane.LayoutMatches(result.Rows, rf)) { RightPane.Rebuild(result.Rows, rf); rebuilt = true; }

        UpdateEditedLines();
        ApplyFilter();
        ApplyViewToggles();

        if (rebuilt)
        {
            RestoreCaret(caret);
            LeftPane.Editor.ScrollToVerticalOffset(vOffset);
            LeftPane.Editor.ScrollToHorizontalOffset(hOffset);
            SyncScroll(LeftPane, RightPane);
        }

        _leftHeader.Refresh();
        _rightHeader.Refresh();
        RedrawAll();
        UpdateStatus();
        TitleChanged?.Invoke();

        if (_firstResult)
        {
            _firstResult = false;
            if (AppSettings.Current.Options.GoToFirstDifferenceOnLoad && result.Sections.Count > 0)
                Dispatcher.UIThread.Post(() => GoToSection(NavigableSections().FirstOrDefault()), DispatcherPriority.Background);
        }
    }

    private void UpdateEditedLines()
    {
        RenderState.EditedLeft = Edited(Left);
        RenderState.EditedRight = Edited(Right);

        static HashSet<int> Edited(SideDocument side)
        {
            if (!side.IsModified || ReferenceEquals(side.Original, side.Current)) return [];
            var before = side.Original.Lines.Select(l => l.Text).ToArray();
            var after = side.Current.Lines.Select(l => l.Text).ToArray();
            return MyersDiff.Compute(before, after, StringComparer.Ordinal)
                .Where(o => o.Kind == EditKind.Insert)
                .Select(o => o.RightIndex)
                .ToHashSet();
        }
    }

    private void RedrawAll()
    {
        LeftPane.Redraw();
        RightPane.Redraw();
        UpdateThumbnail();
    }

    private void PushUndo()
    {
        _undo.Push(new Snapshot(Left.Current, Right.Current, Left.IsModified, Right.IsModified));
        _redo.Clear();
        if (_undo.Count > 200)
        {
            var keep = _undo.Take(200).Reverse().ToList();
            _undo.Clear();
            foreach (var s in keep) _undo.Push(s);
        }
    }

    /// <summary>Aplica una operación sobre el modelo (copiar a otro lado, convertir, etc.) con deshacer.</summary>
    private async Task ApplyModelEditAsync(Side side, Func<TextFile, TextFile> edit)
    {
        await FlushAsync();
        if (IsSideReadOnly(side)) { SetMessage("Ese lado es de solo lectura."); return; }
        PushUndo();
        var doc = side == Side.Left ? Left : Right;
        doc.Current = edit(doc.Current);
        doc.IsModified = true;
        await RecompareAsync(forceRebuild: true);
    }

    private async Task UndoAsync()
    {
        await FlushAsync();
        if (_undo.Count == 0) { SetMessage("Nada para deshacer."); return; }
        _redo.Push(new Snapshot(Left.Current, Right.Current, Left.IsModified, Right.IsModified));
        Restore(_undo.Pop());
        await RecompareAsync(forceRebuild: true);
    }

    private async Task RedoAsync()
    {
        await FlushAsync();
        if (_redo.Count == 0) { SetMessage("Nada para rehacer."); return; }
        _undo.Push(new Snapshot(Left.Current, Right.Current, Left.IsModified, Right.IsModified));
        Restore(_redo.Pop());
        await RecompareAsync(forceRebuild: true);
    }

    private void Restore(Snapshot s)
    {
        Left.Current = s.Left;
        Right.Current = s.Right;
        Left.IsModified = s.LeftModified;
        Right.IsModified = s.RightModified;
    }

    // ---------- Filtro de vista ----------

    private void ApplyFilter()
    {
        var hidden = HiddenRuns();
        bool counts = AppSettings.Current.Options.ShowFilteredLineCounts;
        LeftPane.RunProgrammatic(() => LeftPane.ApplyFilter(hidden, counts));
        RightPane.RunProgrammatic(() => RightPane.ApplyFilter(hidden, counts));
    }

    private List<(int Start, int End)> HiddenRuns()
    {
        var rows = RenderState.Rows;
        var runs = new List<(int, int)>();
        if (Filter == ViewFilter.All || rows.Count == 0) return runs;

        var visible = new bool[rows.Count];
        int context = AppSettings.Current.Options.ContextLines;
        for (int i = 0; i < rows.Count; i++)
        {
            bool diff = RenderState.IsDisplayedAsDifference(rows[i]);
            switch (Filter)
            {
                case ViewFilter.Differences: visible[i] |= diff; break;
                case ViewFilter.Same: visible[i] |= !diff; break;
                case ViewFilter.Context when diff:
                    for (int k = Math.Max(0, i - context); k <= Math.Min(rows.Count - 1, i + context); k++) visible[k] = true;
                    break;
            }
        }

        int start = -1;
        for (int i = 0; i <= rows.Count; i++)
        {
            bool hide = i < rows.Count && !visible[i];
            if (hide && start < 0) start = i;
            if (!hide && start >= 0) { runs.Add((start, i)); start = -1; }
        }
        return runs;
    }

    private void SetFilter(ViewFilter filter)
    {
        Filter = filter;
        ApplyFilter();
        RedrawAll();
    }

    // ---------- Scroll, caret y estado ----------

    private void OnScrolled(DiffPane source)
    {
        if (_syncingScroll) return;
        SyncScroll(source, source == LeftPane ? RightPane : LeftPane);
        UpdateThumbnail();
    }

    private void SyncScroll(DiffPane from, DiffPane to)
    {
        _syncingScroll = true;
        try
        {
            var tv = from.TextView;
            double y = tv.VerticalOffset;
            var line = tv.GetDocumentLineByVisualTop(y);
            if (line is not null && line.LineNumber <= to.Document.LineCount)
            {
                double delta = y - tv.GetVisualTopByDocumentLine(line.LineNumber);
                to.Editor.ScrollToVerticalOffset(to.TextView.GetVisualTopByDocumentLine(line.LineNumber) + delta);
            }
            else
            {
                to.Editor.ScrollToVerticalOffset(y);
            }
            to.Editor.ScrollToHorizontalOffset(tv.HorizontalOffset);
        }
        finally
        {
            _syncingScroll = false;
        }
    }

    private void UpdateThumbnail()
    {
        var tv = LeftPane.TextView;
        double docHeight = Math.Max(tv.DocumentHeight, 1);
        _thumbnail.ViewportTop = tv.VerticalOffset / docHeight;
        _thumbnail.ViewportHeight = tv.Bounds.Height / docHeight;
        _thumbnail.CurrentRow = CurrentRow;
        _thumbnail.InvalidateVisual();
    }

    public int CurrentRow => ActivePane.Editor.TextArea.Caret.Line - 1;

    private void OnCaretMoved()
    {
        UpdateStatus();
        UpdateDetails();
        _thumbnail.CurrentRow = CurrentRow;
        _thumbnail.InvalidateVisual();
        UpdateCurrentSectionHighlight();
    }

    private void UpdateCurrentSectionHighlight()
    {
        var section = SectionAt(CurrentRow);
        var newValue = section is { } s ? (s.Start, s.End) : ((int, int)?)null;
        if (RenderState.CurrentSection != newValue)
        {
            RenderState.CurrentSection = newValue;
            LeftPane.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Background);
            RightPane.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Background);
        }
    }

    private (Side Side, int Real, int Row, int Column) CaptureCaret()
    {
        var pane = ActivePane;
        var caret = pane.Editor.TextArea.Caret;
        int real = -1, count = 0;
        foreach (var line in pane.Document.Lines)
        {
            if (line.LineNumber == caret.Line) { real = pane.IsFiller(line) ? -1 : count; break; }
            if (!pane.IsFiller(line)) count++;
        }
        return (pane.Side, real, caret.Line - 1, caret.Column);
    }

    private void RestoreCaret((Side Side, int Real, int Row, int Column) caret)
    {
        var pane = caret.Side == Side.Left ? LeftPane : RightPane;
        int row = caret.Row;
        if (caret.Real >= 0)
        {
            var rows = RenderState.Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                if ((caret.Side == Side.Left ? rows[i].Left : rows[i].Right) == caret.Real) { row = i; break; }
            }
        }
        row = Math.Clamp(row, 0, Math.Max(0, pane.Document.LineCount - 1));
        var docLine = pane.Document.GetLineByNumber(row + 1);
        pane.Editor.TextArea.Caret.Position = new AvaloniaEdit.TextViewPosition(row + 1, Math.Min(caret.Column, docLine.Length + 1));
    }

    private void UpdateStatus()
    {
        var result = Result;
        if (result is null)
        {
            _statusSections.Text = string.Empty;
            return;
        }
        int important = result.Stats.ImportantSections;
        int minor = result.Stats.UnimportantSections;
        _statusSections.Text = important == 0 && minor == 0
            ? "Archivos idénticos"
            : $"{important} sección(es) con diferencias" + (minor > 0 && !RenderState.IgnoreUnimportant ? $" · {minor} menor(es)" : "");

        int row = CurrentRow;
        _statusRow.Text = RenderState.RowAt(row) switch
        {
            null => string.Empty,
            { Kind: RowKind.Same } => "Igual",
            { Important: true, Kind: RowKind.LeftOnly } => "Diferencia importante · solo izquierda",
            { Important: true, Kind: RowKind.RightOnly } => "Diferencia importante · solo derecha",
            { Important: true } => "Diferencia importante",
            _ when Settings.IgnoredLeft.Contains(RenderState.RowAt(row)!.Value.Left) || Settings.IgnoredRight.Contains(RenderState.RowAt(row)!.Value.Right) => "Ignorado",
            _ => "Diferencia sin importancia",
        };

        var caret = ActivePane.Editor.TextArea.Caret;
        int real = ActivePane.LineIndexOfRow(row);
        string side = ActivePane.Side == Side.Left ? "Izq" : "Der";
        _statusPosition.Text = real >= 0 ? $"{side} · Ln {real + 1}, Col {caret.Column}" : $"{side} · (hueco)";
        _statusMode.Text = IsSideReadOnly(ActivePane.Side) ? "Solo lectura" : ActivePane.Editor.TextArea.OverstrikeMode ? "SOB" : "INS";
        _statusTiming.Text = $"{result.Stats.Elapsed.TotalMilliseconds:0} ms";
    }

    public string StatusMessage => _statusMessage.Text ?? string.Empty;

    public void SetMessage(string message)
    {
        _statusMessage.Text = message;
        _statusMessage.Foreground = message.StartsWith("Error") || message.StartsWith("No se pudo") ? Brushes.IndianRed : null;
    }

    private async Task<bool> ConfirmDiscardAsync(SideDocument doc)
    {
        if (!doc.IsModified || OwnerWindow is not { } owner) return true;
        string answer = await MessageBoxShow(owner, $"'{doc.DisplayName}' tiene cambios sin guardar.", "Guardar", "Descartar", "Cancelar");
        if (answer == "Cancelar") return false;
        if (answer == "Guardar") return await SaveSideAsync(doc == Left ? Side.Left : Side.Right, saveAs: false);
        return true;
    }

    /// <summary>Pregunta por cambios sin guardar antes de cerrar la pestaña.</summary>
    public async Task<bool> ConfirmCloseAsync()
    {
        await FlushAsync();
        return await ConfirmDiscardAsync(Left) && await ConfirmDiscardAsync(Right);
    }

    private static Task<string> MessageBoxShow(Window owner, string message, params string[] buttons) =>
        Dialogs.MessageBox.ShowAsync(owner, "DiffForge", message, buttons);

    // ---------- Encabezado de cada lado ----------

    private sealed class SideHeader : Border
    {
        private readonly TextCompareView _view;
        private readonly Side _side;
        private readonly TextBox _path = new() { Watermark = "Ruta, archivo dentro de .zip o URL http(s)…" };
        private readonly TextBlock _info = new() { FontSize = 11.5, Margin = new Thickness(6, 4, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis };

        public SideHeader(TextCompareView view, Side side)
        {
            _view = view;
            _side = side;
            Padding = new Thickness(8, 8, 8, 6);
            BorderThickness = new Thickness(0, 0, 0, 1);
            Classes.Add("chrome");
            _path.Classes.Add("path");
            _info.Classes.Add("muted");
            var browseIcon = new TextBlock { Text = OperatingSystem.IsWindows() ? "" : "…", FontSize = 15 };
            if (OperatingSystem.IsWindows()) browseIcon.Classes.Add("icon");
            var browse = new Button { Content = browseIcon, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(9, 6), VerticalAlignment = VerticalAlignment.Stretch };
            browse.Classes.Add("subtle");
            ToolTip.SetTip(browse, "Examinar");
            browse.Click += async (_, _) => await _view.BrowseAsync(_side);
            _path.KeyDown += async (_, e) =>
            {
                if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(_path.Text)) await _view.OpenIntoSideAsync(_side, _path.Text);
            };

            var top = new DockPanel();
            DockPanel.SetDock(browse, Avalonia.Controls.Dock.Right);
            top.Children.Add(browse);
            top.Children.Add(_path);
            Child = new StackPanel { Children = { top, _info } };
        }

        public bool ShowInfo { set => _info.IsVisible = value; }

        public void Refresh()
        {
            var doc = _side == Side.Left ? _view.Left : _view.Right;
            _path.Text = doc.IsUntitled ? doc.Title : doc.Spec;
            _path.FontWeight = doc.IsModified ? FontWeight.Bold : FontWeight.Normal;

            if (doc.LoadError is not null)
            {
                _info.Text = $"⚠ {doc.LoadError}";
                return;
            }
            var parts = new List<string>();
            if (doc.LastWriteTime is { } t) parts.Add(t.ToString("dd-MM-yyyy HH:mm:ss"));
            if (!doc.IsUntitled) parts.Add(FormatSize(doc.SizeBytes));
            parts.Add($"{doc.Current.Lines.Count} líneas");
            parts.Add(doc.Current.Encoding.WebName.ToUpperInvariant() + (doc.Current.HasBom ? " BOM" : ""));
            parts.Add(doc.Current.LineEndingSummary);
            parts.Add(doc.Format.Name);
            if (_view.IsSideReadOnly(_side)) parts.Add("🔒 solo lectura");
            if (doc.IsModified) parts.Add("modificado");
            _info.Text = string.Join("  ·  ", parts);
        }

        private static string FormatSize(long bytes) => bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
            _ => $"{bytes / 1024.0 / 1024.0:0.#} MB",
        };
    }

    public async Task BrowseAsync(Side side)
    {
        if (OwnerWindow is not { } owner) return;
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = side == Side.Left ? "Abrir archivo izquierdo" : "Abrir archivo derecho",
            AllowMultiple = false,
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) await OpenIntoSideAsync(side, path);
    }

    // Construye Inlines con resaltado de tramos (panel de detalles).
    private static InlineCollection BuildInlines(string text, IReadOnlyList<DiffSpan>? spans, Palette colors, bool important)
    {
        var inlines = new InlineCollection();
        int pos = 0;
        foreach (var span in spans ?? [])
        {
            if (span.Start > pos) inlines.Add(new Run(text[pos..span.Start]));
            var brush = span.Kind == SpanKind.Replacement ? colors.ReplacementText : span.Kind == SpanKind.Important && important ? colors.ImportantText : colors.UnimportantText;
            inlines.Add(new Run(text.Substring(span.Start, Math.Min(span.Length, text.Length - span.Start)))
            {
                Foreground = brush,
                Background = span.Kind == SpanKind.Important && important ? colors.ImportantTextBack : null,
            });
            pos = Math.Min(text.Length, span.Start + span.Length);
        }
        if (pos < text.Length) inlines.Add(new Run(text[pos..]));
        return inlines;
    }

    // Utilidad: línea de documento de una fila.
    private DocumentLine DocLine(DiffPane pane, int row) =>
        pane.Document.GetLineByNumber(Math.Clamp(row + 1, 1, pane.Document.LineCount));
}
