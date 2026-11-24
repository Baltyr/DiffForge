using Avalonia.Controls;
using Avalonia.Media;
using DiffForge.Core.Compare;
using DiffForge.Core.Reports;

namespace DiffForge.App.Dialogs;

public sealed class ReplacementDialog : DialogBase
{
    private readonly Replacement _item;
    private readonly TextBox _find = new();
    private readonly TextBox _replace = new();
    private readonly CheckBox _case = new() { Content = "Distinguir mayúsculas" };
    private readonly CheckBox _whole = new() { Content = "Solo palabras completas" };
    private readonly CheckBox _regex = new() { Content = "Expresión regular (use $1, $2… en el reemplazo)" };
    private readonly ComboBox _side = new() { ItemsSource = new[] { "Izquierda", "Derecha", "Ambos" } };

    public ReplacementDialog(Replacement item) : base("Elemento de reemplazo", 520, 330)
    {
        _item = item;
        _find.Text = item.Find;
        _replace.Text = item.ReplaceWith;
        _case.IsChecked = item.MatchCase;
        _whole.IsChecked = item.WholeWords;
        _regex.IsChecked = item.IsRegex;
        _side.SelectedIndex = (int)item.Side;
        Content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Form(("Texto a buscar:", _find), ("Reemplazar con:", _replace), ("Lado donde buscar:", _side)),
                _case, _whole, _regex,
                Hint("Ej.: buscar 'oldName' en la izquierda y reemplazar con 'newName' hace que ese renombre no cuente como diferencia importante."),
            },
        };
    }

    protected override bool Validate()
    {
        if (string.IsNullOrEmpty(_find.Text)) return false;
        _item.Find = _find.Text;
        _item.ReplaceWith = _replace.Text ?? string.Empty;
        _item.MatchCase = _case.IsChecked == true;
        _item.WholeWords = _whole.IsChecked == true;
        _item.IsRegex = _regex.IsChecked == true;
        _item.Side = (ReplacementSide)Math.Max(0, _side.SelectedIndex);
        return true;
    }
}

public sealed class FontDialog : DialogBase
{
    private readonly ComboBox _family = new() { MinWidth = 260 };
    private readonly NumericUpDown _size = new() { Minimum = 6, Maximum = 48, Increment = 1, FormatString = "0", Width = 120 };
    private readonly TextBlock _preview = new() { Text = "int total = 10; // 0O 1lI {}[]", Margin = new Avalonia.Thickness(0, 12) };

    public FontDialog(string family, double size) : base("Fuente del editor", 480, 260)
    {
        var families = FontManager.Current.SystemFonts.Select(f => f.Name).OrderBy(n => n).ToList();
        string first = family.Split(',')[0].Trim();
        if (!families.Contains(first)) families.Insert(0, first);
        _family.ItemsSource = families;
        _family.SelectedItem = first;
        _size.Value = (decimal)size;
        _family.SelectionChanged += (_, _) => UpdatePreview();
        _size.ValueChanged += (_, _) => UpdatePreview();
        UpdatePreview();
        Content = new StackPanel { Children = { Form(("Familia:", _family), ("Tamaño:", _size)), _preview } };
    }

    public string FontFamilyName => _family.SelectedItem as string ?? "Consolas";
    public double SelectedFontSize => (double)(_size.Value ?? 13);

    private void UpdatePreview()
    {
        _preview.FontFamily = new FontFamily(FontFamilyName);
        _preview.FontSize = SelectedFontSize;
    }
}

public enum ReportOutput { File, Clipboard, Browser }

public sealed class ReportDialog : DialogBase
{
    private readonly ComboBox _layout = new() { ItemsSource = new[] { "Lado a lado", "Intercalado", "Parche (unified diff)" }, SelectedIndex = 0 };
    private readonly ComboBox _filter = new() { ItemsSource = new[] { "Todas las líneas", "Solo diferencias", "Diferencias con contexto" }, SelectedIndex = 2 };
    private readonly ComboBox _format = new() { ItemsSource = new[] { "HTML", "Texto plano" }, SelectedIndex = 0 };
    private readonly ComboBox _output = new() { ItemsSource = new[] { "Archivo…", "Portapapeles", "Ver en el navegador" }, SelectedIndex = 2 };

    public ReportDialog() : base("Reporte de comparación de texto", 460, 280)
    {
        Content = Form(("Estilo:", _layout), ("Mostrar:", _filter), ("Formato:", _format), ("Salida:", _output));
    }

    public ReportLayout Layout => (ReportLayout)_layout.SelectedIndex;
    public ReportFilter Filter => (ReportFilter)_filter.SelectedIndex;
    public bool Html => _format.SelectedIndex == 0;
    public ReportOutput Output => (ReportOutput)_output.SelectedIndex;
}
