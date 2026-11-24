using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DiffForge.App.Editor;
using DiffForge.App.Services;
using DiffForge.Core.Compare;

namespace DiffForge.App.Views;

public enum SearchSides { Both, Left, Right }

public sealed partial class TextCompareView
{
    private Border _findPanel = null!;
    private AutoCompleteBox _findText = null!;
    private TextBox _replaceText = null!;
    private StackPanel _replaceRow = null!;
    private CheckBox _findRegex = null!, _findCase = null!, _findWhole = null!, _findWrap = null!, _findAutoClose = null!;
    private ComboBox _findSides = null!;
    private TextBlock _findStatus = null!;

    private Control BuildFindPanel()
    {
        _findText = new AutoCompleteBox { Watermark = "Texto o expresión regular", MinWidth = 260, FilterMode = AutoCompleteFilterMode.StartsWith };
        _findText.ItemsSource = AppSettings.Current.FindHistory;
        _findText.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { FindNext(!e.KeyModifiers.HasFlag(KeyModifiers.Shift)); e.Handled = true; }
            if (e.Key == Key.Escape) HideFind();
        };
        _replaceText = new TextBox { Watermark = "Reemplazar con", MinWidth = 260 };
        _findRegex = new CheckBox { Content = "Regex" };
        _findCase = new CheckBox { Content = "Mayúsculas" };
        _findWhole = new CheckBox { Content = "Palabra completa" };
        _findWrap = new CheckBox { Content = "Continuar al inicio", IsChecked = true };
        _findAutoClose = new CheckBox { Content = "Cerrar al buscar" };
        _findSides = new ComboBox { ItemsSource = new[] { "Ambos lados", "Izquierda", "Derecha" }, SelectedIndex = 0 };
        _findStatus = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 };

        var regexHelp = new Button { Content = ".*▾", Padding = new Thickness(6, 2) };
        ToolTip.SetTip(regexHelp, "Insertar elemento de expresión regular");
        var helpMenu = new MenuFlyout();
        foreach (var (label, token) in new[]
        {
            ("Cualquier carácter  .", "."), ("Inicio de línea  ^", "^"), ("Fin de línea  $", "$"),
            ("Dígito  \\d", @"\d"), ("Letra o dígito  \\w", @"\w"), ("Espacio  \\s", @"\s"),
            ("Cero o más  *", "*"), ("Uno o más  +", "+"), ("Opcional  ?", "?"),
            ("Grupo  ( )", "()"), ("Conjunto  [ ]", "[]"), ("Alternativa  |", "|"), ("Límite de palabra  \\b", @"\b"),
        })
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => { _findText.Text += token; _findRegex.IsChecked = true; };
            helpMenu.Items.Add(item);
        }
        regexHelp.Flyout = helpMenu;

        Button B(string text, Action action)
        {
            var b = new Button { Content = text, Padding = new Thickness(10, 3) };
            b.Click += (_, _) => action();
            return b;
        }

        var findRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = "Buscar:", VerticalAlignment = VerticalAlignment.Center, Width = 80 },
                _findText, regexHelp,
                B("Siguiente", () => FindNext(true)),
                B("Anterior", () => FindNext(false)),
                B("Marcar todo", MarkAll),
                _findSides, _findRegex, _findCase, _findWhole, _findWrap, _findAutoClose,
                B("✕", HideFind),
                _findStatus,
            },
        };
        _replaceRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = "Reemplazar:", VerticalAlignment = VerticalAlignment.Center, Width = 80 },
                _replaceText,
                B("Reemplazar", ReplaceCurrent),
                B("Reemplazar todo", ReplaceAll),
            },
        };

        _findPanel = new Border
        {
            IsVisible = false,
            Padding = new Thickness(8, 6),
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)),
            Child = new StackPanel { Spacing = 4, Children = { findRow, _replaceRow } },
        };
        return _findPanel;
    }

    private void ShowFind(bool replace)
    {
        _findPanel.IsVisible = true;
        _replaceRow.IsVisible = replace;
        var editor = ActivePane.Editor;
        if (!string.IsNullOrEmpty(editor.SelectedText) && !editor.SelectedText.Contains('\n'))
            _findText.Text = editor.SelectedText;
        else if (AppSettings.Current.Options.InitFindWithCurrentWord && CurrentWord() is { Length: > 0 } word)
            _findText.Text = word;
        _findText.Focus();
    }

    private void HideFind()
    {
        _findPanel.IsVisible = false;
        RenderState.SearchHits.Clear();
        RedrawAll();
        ActivePane.Editor.TextArea.Focus();
    }

    private string CurrentWord()
    {
        var pane = ActivePane;
        var caret = pane.Editor.TextArea.Caret;
        var line = pane.Document.GetLineByNumber(caret.Line);
        string text = pane.Document.GetText(line);
        int col = Math.Min(caret.Column - 1, text.Length);
        int s = col, e = col;
        while (s > 0 && (char.IsLetterOrDigit(text[s - 1]) || text[s - 1] == '_')) s--;
        while (e < text.Length && (char.IsLetterOrDigit(text[e]) || text[e] == '_')) e++;
        return text[s..e];
    }

    private Regex? BuildSearchRegex()
    {
        string text = _findText.Text ?? string.Empty;
        if (text.Length == 0) return null;
        string pattern = _findRegex.IsChecked == true ? text : Regex.Escape(text);
        if (_findWhole.IsChecked == true) pattern = $@"\b(?:{pattern})\b";
        try
        {
            return new Regex(pattern, (_findCase.IsChecked == true ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException ex)
        {
            _findStatus.Text = $"Regex inválida: {ex.Message}";
            return null;
        }
    }

    private IEnumerable<DiffPane> SearchPanes() => (SearchSides)_findSides.SelectedIndex switch
    {
        SearchSides.Left => [LeftPane],
        SearchSides.Right => [RightPane],
        _ => [LeftPane, RightPane],
    };

    private void RememberSearch()
    {
        string text = _findText.Text ?? string.Empty;
        var history = AppSettings.Current.FindHistory;
        history.Remove(text);
        history.Insert(0, text);
        if (history.Count > 30) history.RemoveAt(history.Count - 1);
    }

    private void FindNext(bool forward)
    {
        if (!_findPanel.IsVisible && string.IsNullOrEmpty(_findText.Text)) { ShowFind(false); return; }
        if (BuildSearchRegex() is not { } regex) return;
        RememberSearch();

        var panes = SearchPanes().ToList();
        if (!panes.Contains(ActivePane)) ActivePane = panes[0];
        int lineCount = ActivePane.Document.LineCount;
        var caret = ActivePane.Editor.TextArea.Caret;
        int startRow = caret.Line - 1;
        int startCol = ActivePane.Editor.SelectionLength > 0 && forward
            ? ActivePane.Editor.SelectionStart + ActivePane.Editor.SelectionLength - ActivePane.Document.GetLineByNumber(caret.Line).Offset
            : caret.Column - 1;
        if (!forward && ActivePane.Editor.SelectionLength > 0)
            startCol = ActivePane.Editor.SelectionStart - ActivePane.Document.GetLineByNumber(caret.Line).Offset;

        // Orden de recorrido: filas en la dirección pedida; dentro de cada fila, izquierda y luego derecha.
        bool wrap = _findWrap.IsChecked == true;
        for (int step = 0; step <= lineCount; step++)
        {
            int row = forward ? startRow + step : startRow - step;
            if (row >= lineCount || row < 0)
            {
                if (!wrap) break;
                row = (row % lineCount + lineCount) % lineCount;
            }

            var ordered = forward ? panes : Enumerable.Reverse(panes).ToList();
            foreach (var pane in ordered)
            {
                var line = pane.Document.GetLineByNumber(row + 1);
                string text = pane.Document.GetText(line);
                var matches = regex.Matches(text).Where(m => m.Length > 0).ToList();
                bool sameStart = step == 0 && pane == ActivePane;
                Match? hit = forward
                    ? matches.FirstOrDefault(m => !sameStart || m.Index >= startCol)
                    : matches.LastOrDefault(m => !sameStart || m.Index < startCol);
                if (step == 0 && pane != ActivePane && forward && panes.IndexOf(pane) < panes.IndexOf(ActivePane)) hit = null;
                if (hit is null) continue;

                GoToRow(row, pane, hit.Index + 1, select: true, length: hit.Length);
                _findStatus.Text = string.Empty;
                if (_findAutoClose.IsChecked == true) _findPanel.IsVisible = false;
                return;
            }
        }
        _findStatus.Text = "Sin coincidencias.";
    }

    private void MarkAll()
    {
        RenderState.SearchHits.Clear();
        if (BuildSearchRegex() is not { } regex) return;
        RememberSearch();
        int count = 0;
        foreach (var pane in SearchPanes())
        {
            foreach (var line in pane.Document.Lines)
            {
                foreach (Match m in regex.Matches(pane.Document.GetText(line)))
                {
                    if (m.Length == 0) continue;
                    if (count++ < 20_000) RenderState.SearchHits.Add(new SearchHit(pane.Side, line.LineNumber - 1, m.Index, m.Length));
                }
            }
        }
        _findStatus.Text = $"{count} coincidencia(s).";
        RedrawAll();
    }

    private void ReplaceCurrent()
    {
        if (BuildSearchRegex() is not { } regex) return;
        var editor = ActivePane.Editor;
        if (editor.IsReadOnly) { _findStatus.Text = "Lado de solo lectura."; return; }
        var match = regex.Match(editor.SelectedText);
        if (editor.SelectionLength > 0 && match.Success && match.Length == editor.SelectionLength)
        {
            string replacement = _findRegex.IsChecked == true ? match.Result(_replaceText.Text ?? "") : _replaceText.Text ?? "";
            editor.Document.Replace(editor.SelectionStart, editor.SelectionLength, replacement);
        }
        FindNext(true);
    }

    private void ReplaceAll()
    {
        if (BuildSearchRegex() is not { } regex) return;
        string replacement = _replaceText.Text ?? string.Empty;
        if (_findRegex.IsChecked != true) replacement = replacement.Replace("$", "$$");
        int total = 0;
        foreach (var pane in SearchPanes())
        {
            if (pane.Editor.IsReadOnly) continue;
            pane.Document.BeginUpdate();
            try
            {
                foreach (var line in pane.Document.Lines.Reverse())
                {
                    string text = pane.Document.GetText(line);
                    int count = regex.Matches(text).Count;
                    if (count == 0) continue;
                    pane.Document.Replace(line.Offset, line.Length, regex.Replace(text, replacement));
                    total += count;
                }
            }
            finally
            {
                pane.Document.EndUpdate();
            }
        }
        _findStatus.Text = $"{total} reemplazo(s).";
    }
}
