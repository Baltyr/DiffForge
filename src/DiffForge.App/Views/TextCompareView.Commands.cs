using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using AvaloniaEdit.Document;
using DiffForge.App.Commands;
using DiffForge.App.Dialogs;
using DiffForge.App.Editor;
using DiffForge.App.Services;
using DiffForge.Core.Compare;
using DiffForge.Core.Documents;

namespace DiffForge.App.Views;

public sealed partial class TextCompareView
{
    public bool? IsChecked(Cmd cmd) => cmd switch
    {
        Cmd.Locked => Locked,
        Cmd.ShowAll => Filter == ViewFilter.All,
        Cmd.ShowDifferences => Filter == ViewFilter.Differences,
        Cmd.ShowSame => Filter == ViewFilter.Same,
        Cmd.ShowContext => Filter == ViewFilter.Context,
        Cmd.ShowNone => Filter == ViewFilter.None,
        Cmd.IgnoreUnimportant => RenderState.IgnoreUnimportant,
        Cmd.VisibleWhitespace => VisibleWhitespace,
        Cmd.LineNumbers => ShowLineNumbers,
        Cmd.SyntaxHighlighting => RenderState.SyntaxHighlighting,
        Cmd.WordWrap => WordWrap,
        Cmd.SideBySide => !OverUnder,
        Cmd.OverUnder => OverUnder,
        Cmd.Thumbnail => ShowThumbnail,
        Cmd.TextDetails => ShowTextDetails,
        Cmd.HexDetails => ShowHexDetails,
        Cmd.AlignmentDetails => ShowAlignmentDetails,
        Cmd.Ruler => ShowRuler,
        Cmd.FileInfo => ShowFileInfo,
        _ => null,
    };

    public bool CanExecute(Cmd cmd) => cmd switch
    {
        Cmd.Undo => _undo.Count > 0 || _editBurstOpen,
        Cmd.Redo => _redo.Count > 0,
        Cmd.SaveFile => IsActiveModified(),
        Cmd.OpenWithAssociated or Cmd.Explorer => !ActiveDoc.IsUntitled && File.Exists(ActiveDoc.Spec),
        Cmd.ClearSession or Cmd.SwapSides or Cmd.OpenFile or Cmd.OpenFileLeft or Cmd.OpenFileRight or Cmd.OpenClipboard => !Locked,
        _ => true,
    };

    private SideDocument ActiveDoc => ActivePane.Side == Side.Left ? Left : Right;

    private bool IsActiveModified() => ActiveDoc.IsModified || (ActivePane.Side == Side.Left ? _leftDirty : _rightDirty);

    public async Task ExecuteAsync(Cmd cmd)
    {
        try
        {
            await ExecuteCoreAsync(cmd);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            SetMessage($"Error: {ex.Message}");
        }
    }

    private async Task ExecuteCoreAsync(Cmd cmd)
    {
        switch (cmd)
        {
            // ----- Sesión -----
            case Cmd.SaveSession: await SaveSessionAsync(saveAs: SessionPath is null); break;
            case Cmd.SaveSessionAs: await SaveSessionAsync(saveAs: true); break;
            case Cmd.SessionSettings: await ShowSessionSettingsAsync(); break;
            case Cmd.Locked: Locked = !Locked; break;
            case Cmd.ClearSession:
                if (await ConfirmCloseAsync()) await SetDocumentsAsync(new SideDocument(), new SideDocument());
                break;
            case Cmd.SwapSides:
                await FlushAsync();
                (Left, Right) = (Right, Left);
                (LeftReadOnlyArg, RightReadOnlyArg) = (RightReadOnlyArg, LeftReadOnlyArg);
                (Settings.IsolatedLeft, Settings.IsolatedRight) = (Settings.IsolatedRight, Settings.IsolatedLeft);
                (Settings.IgnoredLeft, Settings.IgnoredRight) = (Settings.IgnoredRight, Settings.IgnoredLeft);
                Settings.ManualAlignments = Settings.ManualAlignments.Select(a => (a.Right, a.Left)).ToList();
                ResetHistory();
                await RecompareAsync(forceRebuild: true);
                break;
            case Cmd.ReloadFiles:
                if (await ConfirmCloseAsync()) await LoadAsync(Left.IsUntitled ? null : Left.Spec, Right.IsUntitled ? null : Right.Spec);
                break;
            case Cmd.RecompareFiles: await RecompareAsync(forceRebuild: true); break;
            case Cmd.Report: await ShowReportAsync(); break;
            case Cmd.Info: await ShowInfoAsync(); break;

            // ----- Archivo -----
            case Cmd.OpenFile: await BrowseAsync(ActivePane.Side); break;
            case Cmd.OpenFileLeft: await BrowseAsync(Side.Left); break;
            case Cmd.OpenFileRight: await BrowseAsync(Side.Right); break;
            case Cmd.OpenClipboard:
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } cb && await cb.GetTextAsync() is { } clip)
                    await SetSideTextAsync(ActivePane.Side, clip, "Portapapeles");
                break;
            case Cmd.OpenWithAssociated:
                Process.Start(new ProcessStartInfo(ActiveDoc.Spec) { UseShellExecute = true });
                break;
            case Cmd.Explorer:
                if (OperatingSystem.IsWindows()) Process.Start("explorer.exe", $"/select,\"{ActiveDoc.Spec}\"");
                else Process.Start(new ProcessStartInfo(Path.GetDirectoryName(ActiveDoc.Spec)!) { UseShellExecute = true });
                break;
            case Cmd.SaveFile: await SaveSideAsync(ActivePane.Side, saveAs: false); break;
            case Cmd.SaveFileAs: await SaveSideAsync(ActivePane.Side, saveAs: true); break;
            case Cmd.SaveAll:
                await FlushAsync();
                if (Left.IsModified) await SaveSideAsync(Side.Left, false);
                if (Right.IsModified) await SaveSideAsync(Side.Right, false);
                break;

            // ----- Editar -----
            case Cmd.Undo: await UndoAsync(); break;
            case Cmd.Redo: await RedoAsync(); break;
            case Cmd.AlignWith: StartAlignWith(); break;
            case Cmd.Isolate: await IsolateAsync(); break;
            case Cmd.AddReplacement: await AddReplacementAsync(); break;
            case Cmd.CopyToRight: await CopyAsync(Side.Left, singleLine: false); break;
            case Cmd.CopyToLeft: await CopyAsync(Side.Right, singleLine: false); break;
            case Cmd.CopyToOtherSide: await CopyAsync(ActivePane.Side, singleLine: false); break;
            case Cmd.CopyLineToRight: await CopyAsync(Side.Left, singleLine: true); break;
            case Cmd.CopyLineToLeft: await CopyAsync(Side.Right, singleLine: true); break;
            case Cmd.CopyLineToOtherSide: await CopyAsync(ActivePane.Side, singleLine: true); break;
            case Cmd.IncreaseIndent: Indent(+1); break;
            case Cmd.DecreaseIndent: Indent(-1); break;
            case Cmd.DeleteLine: EditCaretLine((doc, line, _) => doc.Remove(line.Offset, line.TotalLength > 0 ? line.TotalLength : line.Length)); break;
            case Cmd.DeleteToStartOfLine: EditCaretLine((doc, line, off) => doc.Remove(line.Offset, off - line.Offset)); break;
            case Cmd.DeleteToEndOfLine: EditCaretLine((doc, line, off) => doc.Remove(off, line.EndOffset - off)); break;
            case Cmd.DeleteWord: EditCaretLine((doc, line, off) => { var (s, e) = WordBounds(doc, line, off); doc.Remove(s, e - s); }); break;
            case Cmd.DeleteToStartOfWord: EditCaretLine((doc, line, off) => { var (s, _) = WordBounds(doc, line, off); doc.Remove(s, off - s); }); break;
            case Cmd.DeleteToEndOfWord: EditCaretLine((doc, line, off) => { var (_, e) = WordBounds(doc, line, off); doc.Remove(off, e - off); }); break;
            case Cmd.InsertLineBefore: EditCaretLine((doc, line, _) => doc.Insert(line.Offset, "\n")); break;
            case Cmd.InsertLineAfter:
                EditCaretLine((doc, line, _) => doc.Insert(line.EndOffset, "\n"));
                ActivePane.Editor.TextArea.Caret.Line += 1;
                break;
            case Cmd.SelectAll: ActivePane.Editor.SelectAll(); break;
            case Cmd.SelectSection:
                if (TargetRows() is { } sel)
                {
                    var first = DocLine(ActivePane, sel.Start);
                    var last = DocLine(ActivePane, sel.End - 1);
                    ActivePane.Editor.Select(first.Offset, last.EndOffset - first.Offset);
                }
                break;
            case Cmd.CompareSelectionToClipboard:
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard && await clipboard.GetTextAsync() is { } clipText)
                {
                    string selection = ActivePane.Editor.SelectedText;
                    NewComparisonRequested?.Invoke(DocumentService.FromText(selection, "Selección"), DocumentService.FromText(clipText, "Portapapeles"));
                }
                break;
            case Cmd.ConvertTrimTrailing: await ApplyModelEditAsync(ActivePane.Side, TextTransforms.TrimTrailingWhitespace); break;
            case Cmd.ConvertLeadingSpacesToTabs: await ApplyModelEditAsync(ActivePane.Side, f => TextTransforms.LeadingSpacesToTabs(f, ActiveDoc.Format.TabStop)); break;
            case Cmd.ConvertTabsToSpaces: await ApplyModelEditAsync(ActivePane.Side, f => TextTransforms.TabsToSpaces(f, ActiveDoc.Format.TabStop)); break;
            case Cmd.ConvertEndingsCrLf: await ApplyModelEditAsync(ActivePane.Side, f => TextTransforms.ConvertLineEndings(f, LineEnding.CrLf)); break;
            case Cmd.ConvertEndingsLf: await ApplyModelEditAsync(ActivePane.Side, f => TextTransforms.ConvertLineEndings(f, LineEnding.Lf)); break;
            case Cmd.ConvertEndingsCr: await ApplyModelEditAsync(ActivePane.Side, f => TextTransforms.ConvertLineEndings(f, LineEnding.Cr)); break;

            // ----- Buscar -----
            case Cmd.NextDifference: await FlushAsync(); NextDifference(true); break;
            case Cmd.PreviousDifference: await FlushAsync(); NextDifference(false); break;
            case Cmd.NextSection: await FlushAsync(); NextSection(true); break;
            case Cmd.PreviousSection: await FlushAsync(); NextSection(false); break;
            case Cmd.NextReplacement: NextRowMatching(true, IsReplacementRow, "reemplazos"); break;
            case Cmd.PreviousReplacement: NextRowMatching(false, IsReplacementRow, "reemplazos"); break;
            case Cmd.NextEdit: await FlushAsync(); NextRowMatching(true, IsEditedRow, "ediciones"); break;
            case Cmd.PreviousEdit: await FlushAsync(); NextRowMatching(false, IsEditedRow, "ediciones"); break;
            case Cmd.Find: ShowFind(replace: false); break;
            case Cmd.Replace: ShowFind(replace: true); break;
            case Cmd.FindNext: FindNext(forward: true); break;
            case Cmd.FindPrevious: FindNext(forward: false); break;
            case Cmd.GoTo: await GoToLineAsync(); break;
            case Cmd.ClearBookmarks: RenderState.Bookmarks.Clear(); RedrawAll(); break;
            case >= Cmd.ToggleBookmark0 and <= Cmd.ToggleBookmark9: ToggleBookmark(cmd - Cmd.ToggleBookmark0); break;
            case >= Cmd.GoToBookmark0 and <= Cmd.GoToBookmark9: GoToBookmark(cmd - Cmd.GoToBookmark0); break;

            // ----- Ver -----
            case Cmd.ShowAll: SetFilter(ViewFilter.All); break;
            case Cmd.ShowDifferences: SetFilter(ViewFilter.Differences); break;
            case Cmd.ShowSame: SetFilter(ViewFilter.Same); break;
            case Cmd.ShowContext: SetFilter(ViewFilter.Context); break;
            case Cmd.ShowNone: SetFilter(ViewFilter.None); break;
            case Cmd.IgnoreUnimportant:
                RenderState.IgnoreUnimportant = !RenderState.IgnoreUnimportant;
                ApplyFilter();
                RedrawAll();
                UpdateStatus();
                break;
            case Cmd.Ignored: await ToggleIgnoredAsync(); break;
            case Cmd.VisibleWhitespace: VisibleWhitespace = !VisibleWhitespace; ApplyViewToggles(); break;
            case Cmd.LineNumbers: ShowLineNumbers = !ShowLineNumbers; ApplyViewToggles(); break;
            case Cmd.SyntaxHighlighting: RenderState.SyntaxHighlighting = !RenderState.SyntaxHighlighting; RedrawAll(); break;
            case Cmd.WordWrap: WordWrap = !WordWrap; ApplyViewToggles(); break;
            case Cmd.DisplayFont: await ChooseFontAsync(); break;
            case Cmd.FontIncrease: SetFontSize(_fontSize + 1); break;
            case Cmd.FontDecrease: SetFontSize(_fontSize - 1); break;
            case Cmd.FontReset: SetFontSize(AppSettings.Current.Options.EditorFontSize); break;
            case Cmd.SideBySide: OverUnder = false; ArrangePanes(); break;
            case Cmd.OverUnder: OverUnder = true; ArrangePanes(); break;
            case Cmd.Thumbnail: ShowThumbnail = !ShowThumbnail; ApplyViewToggles(); break;
            case Cmd.TextDetails: ShowTextDetails = !ShowTextDetails; UpdateDetails(); break;
            case Cmd.HexDetails: ShowHexDetails = !ShowHexDetails; UpdateDetails(); break;
            case Cmd.AlignmentDetails: ShowAlignmentDetails = !ShowAlignmentDetails; UpdateDetails(); break;
            case Cmd.Ruler: ShowRuler = !ShowRuler; UpdateDetails(); break;
            case Cmd.FileInfo: ShowFileInfo = !ShowFileInfo; ApplyViewToggles(); break;
        }
        UpdateStatus();
        TitleChanged?.Invoke();
    }

    // ---------- Edición sobre el documento del panel activo ----------

    private void EditCaretLine(Action<TextDocument, DocumentLine, int> edit)
    {
        var pane = ActivePane;
        if (pane.Editor.IsReadOnly) { SetMessage("Ese lado es de solo lectura."); return; }
        var caret = pane.Editor.TextArea.Caret;
        var line = pane.Document.GetLineByNumber(caret.Line);
        pane.Document.BeginUpdate();
        try { edit(pane.Document, line, caret.Offset); }
        finally { pane.Document.EndUpdate(); }
    }

    private static (int Start, int End) WordBounds(TextDocument doc, DocumentLine line, int offset)
    {
        static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';
        int s = offset, e = offset;
        while (s > line.Offset && IsWord(doc.GetCharAt(s - 1))) s--;
        while (e < line.EndOffset && IsWord(doc.GetCharAt(e))) e++;
        if (s == e && e < line.EndOffset) e++;
        return (s, e);
    }

    private void Indent(int direction)
    {
        var pane = ActivePane;
        if (pane.Editor.IsReadOnly) return;
        var format = pane.Side == Side.Left ? Left.Format : Right.Format;
        string unit = format.InsertSpacesInsteadOfTabs ? new string(' ', format.TabStop) : "\t";
        var area = pane.Editor.TextArea;

        int firstLine, lastLine;
        if (area.Selection.IsEmpty) firstLine = lastLine = area.Caret.Line;
        else
        {
            var seg = area.Selection.SurroundingSegment;
            firstLine = pane.Document.GetLineByOffset(seg.Offset).LineNumber;
            var end = pane.Document.GetLineByOffset(seg.EndOffset);
            lastLine = seg.EndOffset == end.Offset && end.LineNumber > firstLine ? end.LineNumber - 1 : end.LineNumber;
        }

        pane.Document.BeginUpdate();
        try
        {
            for (int n = firstLine; n <= lastLine; n++)
            {
                var line = pane.Document.GetLineByNumber(n);
                if (pane.IsFiller(line)) continue;
                if (direction > 0) { pane.Document.Insert(line.Offset, unit); continue; }

                string text = pane.Document.GetText(line);
                int remove = text.StartsWith('\t') ? 1 : text.TakeWhile(c => c == ' ').Take(format.TabStop).Count();
                if (remove > 0) pane.Document.Remove(line.Offset, remove);
            }
        }
        finally
        {
            pane.Document.EndUpdate();
        }
    }

    private void OnEditorKeyDown(DiffPane pane, KeyEventArgs e)
    {
        // "Backspace des-sangra": dentro del margen inicial borra hasta el nivel de sangría anterior.
        if (e.Key != Key.Back || e.KeyModifiers != KeyModifiers.None || !AppSettings.Current.Options.BackspaceUnindents) return;
        var area = pane.Editor.TextArea;
        if (!area.Selection.IsEmpty || pane.Editor.IsReadOnly) return;
        var line = pane.Document.GetLineByNumber(area.Caret.Line);
        int col = area.Caret.Offset - line.Offset;
        string text = pane.Document.GetText(line);
        if (col == 0 || col > text.Length || text[..col].Any(c => c != ' ')) return;

        int tab = (pane.Side == Side.Left ? Left.Format : Right.Format).TabStop;
        int remove = col % tab == 0 ? tab : col % tab;
        pane.Document.Remove(area.Caret.Offset - remove, remove);
        e.Handled = true;
    }

    // ---------- Copiar entre lados ----------

    private async Task CopyAsync(Side from, bool singleLine)
    {
        await FlushAsync();
        var target = from == Side.Left ? Side.Right : Side.Left;
        if (IsSideReadOnly(target)) { SetMessage("El lado destino es de solo lectura."); return; }

        // Si el comando viene del otro panel, la selección relevante es la del panel origen.
        var range = singleLine
            ? (CurrentRow >= 0 && CurrentRow < RenderState.Rows.Count ? (CurrentRow, CurrentRow + 1) : ((int, int)?)null)
            : TargetRows();
        if (range is not { } r) { SetMessage("Ubique el cursor en una sección con diferencias o seleccione líneas."); return; }

        var rows = RenderState.Rows;
        var src = from == Side.Left ? Left.Current : Right.Current;
        int endRow = r.Item2;
        await ApplyModelEditAsync(target, dst =>
        {
            var lines = new List<TextLine>(dst.Lines.Count);
            var defaultEnding = dst.DominantEnding;
            for (int i = 0; i < rows.Count; i++)
            {
                int s = from == Side.Left ? rows[i].Left : rows[i].Right;
                int d = from == Side.Left ? rows[i].Right : rows[i].Left;
                if (i >= r.Item1 && i < endRow)
                {
                    if (s >= 0) lines.Add(new TextLine(src.Lines[s].Text, d >= 0 ? dst.Lines[d].Ending : defaultEnding));
                }
                else if (d >= 0)
                {
                    lines.Add(dst.Lines[d]);
                }
            }
            return dst.WithLines(NormalizeEndings(lines, dst));
        });

        if (AppSettings.Current.Options.GoToNextDifferenceAfterCopy && !singleLine) NextSection(true);
    }

    /// <summary>Solo la última línea puede quedar sin salto; se respeta si el archivo terminaba así.</summary>
    private static List<TextLine> NormalizeEndings(List<TextLine> lines, TextFile original)
    {
        bool endsWithoutNewline = original.Lines.Count > 0 && original.Lines[^1].Ending == LineEnding.None;
        var ending = original.DominantEnding;
        for (int i = 0; i < lines.Count - 1; i++)
        {
            // Un None intermedio solo puede venir de la antigua última línea (o de un corte artificial
            // por límite de caracteres, que se conserva para que al guardar se vuelva a unir).
            if (lines[i].Ending == LineEnding.None && !HasArtificialBreaks(original)) lines[i] = lines[i] with { Ending = ending };
        }
        if (lines.Count > 0)
        {
            var last = lines[^1];
            lines[^1] = last with { Ending = endsWithoutNewline ? LineEnding.None : (last.Ending == LineEnding.None ? ending : last.Ending) };
        }
        return lines;
    }

    private static bool HasArtificialBreaks(TextFile file)
    {
        for (int i = 0; i < file.Lines.Count - 1; i++)
            if (file.Lines[i].Ending == LineEnding.None) return true;
        return false;
    }

    // ---------- Alinear / aislar / ignorar / reemplazo ----------

    private void StartAlignWith()
    {
        int real = ActivePane.LineIndexOfRow(CurrentRow);
        if (real < 0) { SetMessage("Ubique el cursor en una línea (no en un hueco)."); return; }
        _alignWithPending = (ActivePane.Side, real);
        SetMessage("Alinear con: haga clic en la línea del otro lado (Esc para cancelar).");
    }

    private async void OnPanePointerReleased(DiffPane pane)
    {
        if (_alignWithPending is not { } pending || pane.Side == pending.Side) return;
        _alignWithPending = null;
        int real = pane.LineIndexOfRow(pane.Editor.TextArea.Caret.Line - 1);
        if (real < 0) { SetMessage("Ese es un hueco; alineación cancelada."); return; }
        var pair = pending.Side == Side.Left ? (pending.Line, real) : (real, pending.Line);
        Settings.ManualAlignments.RemoveAll(a => a.Left == pair.Item1 || a.Right == pair.Item2);
        Settings.ManualAlignments.Add(pair);
        await RecompareAsync(forceRebuild: true);
        SetMessage($"Líneas {pair.Item1 + 1} y {pair.Item2 + 1} alineadas.");
    }

    private async Task IsolateAsync()
    {
        await FlushAsync();
        if (TargetRows(allowSingleLine: true) is not { } r) return;
        var set = ActivePane.Side == Side.Left ? Settings.IsolatedLeft : Settings.IsolatedRight;
        var lines = Enumerable.Range(r.Start, r.End - r.Start).Select(ActivePane.LineIndexOfRow).Where(i => i >= 0).ToList();
        bool remove = lines.All(set.Contains);
        foreach (int i in lines) { if (remove) set.Remove(i); else set.Add(i); }
        await RecompareAsync(forceRebuild: true);
    }

    private async Task ToggleIgnoredAsync()
    {
        await FlushAsync();
        if (TargetRows(allowSingleLine: true) is not { } r) return;
        var rows = RenderState.Rows;
        var lefts = Enumerable.Range(r.Start, r.End - r.Start).Select(i => rows[i].Left).Where(i => i >= 0).ToList();
        var rights = Enumerable.Range(r.Start, r.End - r.Start).Select(i => rows[i].Right).Where(i => i >= 0).ToList();
        bool remove = lefts.All(Settings.IgnoredLeft.Contains) && rights.All(Settings.IgnoredRight.Contains);
        foreach (int i in lefts) { if (remove) Settings.IgnoredLeft.Remove(i); else Settings.IgnoredLeft.Add(i); }
        foreach (int i in rights) { if (remove) Settings.IgnoredRight.Remove(i); else Settings.IgnoredRight.Add(i); }
        await RecompareAsync(forceRebuild: false);
    }

    private async Task AddReplacementAsync()
    {
        await FlushAsync();
        if (OwnerWindow is not { } owner) return;
        var item = new Replacement();
        int row = CurrentRow;
        if (RenderState.RowAt(row) is { Kind: RowKind.Changed } dr && RenderState.Intra(row) is { } intra
            && intra.Left.FirstOrDefault(s => s.Kind == SpanKind.Important) is { Length: > 0 } ls
            && intra.Right.FirstOrDefault(s => s.Kind == SpanKind.Important) is { Length: > 0 } rs)
        {
            item.Find = Result!.Left[dr.Left].Text.Substring(ls.Start, ls.Length);
            item.ReplaceWith = Result.Right[dr.Right].Text.Substring(rs.Start, rs.Length);
            item.WholeWords = true;
        }
        if (await new ReplacementDialog(item).ShowAsync(owner))
        {
            Settings.Replacements.Add(item);
            await RecompareAsync();
        }
    }

    // ---------- Guardar ----------

    public async Task<bool> SaveSideAsync(Side side, bool saveAs)
    {
        await FlushAsync();
        var doc = side == Side.Left ? Left : Right;
        string? path = doc.CanSaveInPlace && !doc.IsUntitled && !saveAs ? doc.Spec : null;
        if (path is null)
        {
            if (OwnerWindow is not { } owner) return false;
            var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = side == Side.Left ? "Guardar archivo izquierdo como" : "Guardar archivo derecho como",
                SuggestedFileName = doc.IsUntitled ? "sin_titulo.txt" : Path.GetFileName(doc.Spec),
            });
            path = file?.TryGetLocalPath();
            if (path is null) return false;
        }

        await DocumentService.SaveAsync(doc, path);
        SetMessage($"Guardado: {path}");
        await RecompareAsync(forceRebuild: true);
        return true;
    }

    // ---------- Fuente ----------

    private void SetFontSize(double size)
    {
        _fontSize = Math.Clamp(size, 6, 48);
        LeftPane.Editor.FontSize = _fontSize;
        RightPane.Editor.FontSize = _fontSize;
        UpdateDetails();
    }

    private async Task ChooseFontAsync()
    {
        if (OwnerWindow is not { } owner) return;
        var dialog = new FontDialog(AppSettings.Current.Options.EditorFont, _fontSize);
        if (!await dialog.ShowAsync(owner)) return;
        AppSettings.Current.Options.EditorFont = dialog.FontFamilyName;
        AppSettings.Current.Options.EditorFontSize = dialog.SelectedFontSize;
        _fontSize = dialog.SelectedFontSize;
        AppSettings.Save();
    }

    private async Task GoToLineAsync()
    {
        if (OwnerWindow is not { } owner) return;
        var input = await Dialogs.MessageBox.PromptAsync(owner, "Ir a", "Línea[:columna] del panel activo:", "");
        if (string.IsNullOrWhiteSpace(input)) return;
        var parts = input.Split(':', ',');
        if (!int.TryParse(parts[0], out int line)) return;
        int col = parts.Length > 1 && int.TryParse(parts[1], out int c) ? c : 1;
        int row = RowOfLine(ActivePane.Side, Math.Max(0, line - 1));
        if (row < 0) { SetMessage($"La línea {line} no existe."); return; }
        GoToRow(row, ActivePane, col);
    }
}
