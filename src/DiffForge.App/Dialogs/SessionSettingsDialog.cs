using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using DiffForge.App.Services;
using DiffForge.App.Views;
using DiffForge.Core.Compare;
using DiffForge.Core.Documents;

namespace DiffForge.App.Dialogs;

/// <summary>Configuración de la sesión: Specs, Formato, Importancia, Alineación y Reemplazos.</summary>
public sealed class SessionSettingsDialog : DialogBase
{
    private const string Detected = "(Detectado)";
    private const string NoOverride = "(Ninguna)";

    private readonly TextBox _leftSpec = new();
    private readonly TextBox _rightSpec = new();
    private readonly CheckBox _disableEditing = new() { Content = "Deshabilitar edición (proteger archivos en disco)" };
    private readonly TextBox _description = new() { AcceptsReturn = true, Height = 70, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly ComboBox _leftFormat = new() { MinWidth = 220 };
    private readonly ComboBox _rightFormat = new() { MinWidth = 220 };
    private readonly ComboBox _leftEncoding = new() { MinWidth = 220 };
    private readonly ComboBox _rightEncoding = new() { MinWidth = 220 };
    private readonly StackPanel _elements = new() { Spacing = 2 };
    private readonly CheckBox _leading = new() { Content = "Espacios al inicio de línea" };
    private readonly CheckBox _embedded = new() { Content = "Espacios en medio de la línea" };
    private readonly CheckBox _trailing = new() { Content = "Espacios al final de línea" };
    private readonly CheckBox _everythingElse = new() { Content = "Todo lo demás (texto fuera de la gramática)" };
    private readonly CheckBox _case = new() { Content = "Distinguir mayúsculas en \"todo lo demás\"" };
    private readonly TextBox _unimportant = new() { AcceptsReturn = true, Height = 80, Watermark = "Un patrón por línea (regex). Prefijo '=' para texto literal." };
    private readonly CheckBox _orphans = new() { Content = "Las líneas huérfanas siempre son importantes" };
    private readonly CheckBox _lineEndings = new() { Content = "Comparar finales de línea (PC/Mac/Unix)" };
    private readonly RadioButton[] _alignment;
    private readonly CheckBox _neverAlign = new() { Content = "Nunca alinear diferencias (mostrar bloques agregados/borrados)" };
    private readonly NumericUpDown _skew = new() { Minimum = 0, Maximum = 100000, Increment = 10, Width = 140, FormatString = "0" };
    private readonly CheckBox _closeness = new() { Content = "Emparejar líneas por similitud" };
    private readonly ListBox _replacements = new() { Height = 220 };
    private readonly CheckBox _updateDefaults = new() { Content = "Usar estas reglas también como predeterminadas", Margin = new Thickness(12, 0) };
    private readonly List<Replacement> _replacementItems;

    public SessionSettingsDialog(TextCompareView view) : base("Configuración de la sesión — Text Compare", 720, 600)
    {
        Settings = view.Settings.Clone();
        _replacementItems = Settings.Replacements;

        // Specs
        _leftSpec.Text = view.Left.IsUntitled ? "" : view.Left.Spec;
        _rightSpec.Text = view.Right.IsUntitled ? "" : view.Right.Spec;
        _disableEditing.IsChecked = view.DisableEditing;
        _description.Text = view.Description;
        var specs = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Form(("Izquierda:", WithBrowse(_leftSpec)), ("Derecha:", WithBrowse(_rightSpec))),
                _disableEditing,
                Heading("Descripción"),
                _description,
            },
        };

        // Formato
        var formatNames = new List<string> { Detected };
        formatNames.AddRange(AppSettings.Current.FileFormats.Select(f => f.Name));
        var encodings = new List<string> { NoOverride };
        encodings.AddRange(TextFileCodec.KnownEncodings);
        _leftFormat.ItemsSource = formatNames;
        _rightFormat.ItemsSource = formatNames;
        _leftEncoding.ItemsSource = encodings;
        _rightEncoding.ItemsSource = encodings;
        _leftFormat.SelectedItem = view.Left.FormatOverride ?? Detected;
        _rightFormat.SelectedItem = view.Right.FormatOverride ?? Detected;
        _leftEncoding.SelectedItem = view.Left.EncodingOverride ?? NoOverride;
        _rightEncoding.SelectedItem = view.Right.EncodingOverride ?? NoOverride;
        var format = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Hint("El formato define gramática, tabulación y conversión. 'Detectado' lo elige por máscara de archivo."),
                Form(("Formato izquierdo:", _leftFormat), ("Formato derecho:", _rightFormat),
                     ("Codificación izquierda:", _leftEncoding), ("Codificación derecha:", _rightEncoding)),
                Hint($"Formatos detectados ahora: {view.Left.Format.Name} / {view.Right.Format.Name}"),
            },
        };

        // Importancia
        var elementNames = view.Left.Format.ElementNames.Concat(view.Right.Format.ElementNames).Distinct().OrderBy(n => n);
        foreach (var name in elementNames)
            _elements.Children.Add(new CheckBox { Content = name, IsChecked = Settings.IsElementImportant(name), Tag = name });
        _leading.IsChecked = Settings.LeadingWhitespaceImportant;
        _embedded.IsChecked = Settings.EmbeddedWhitespaceImportant;
        _trailing.IsChecked = Settings.TrailingWhitespaceImportant;
        _everythingElse.IsChecked = Settings.EverythingElseImportant;
        _case.IsChecked = Settings.CaseSensitive;
        _unimportant.Text = string.Join("\n", Settings.UnimportantText.Select(p => (p.IsRegex ? "" : "=") + p.Pattern));
        _orphans.IsChecked = Settings.OrphanLinesAlwaysImportant;
        _lineEndings.IsChecked = Settings.CompareLineEndings;
        var editGrammar = new Button { Content = "Editar gramática…", HorizontalAlignment = HorizontalAlignment.Left };
        editGrammar.Click += async (_, _) => await new FileFormatsDialog(view.ActivePane.Side == Side.Left ? view.Left.Format.Name : view.Right.Format.Name).ShowAsync(this);
        var importance = new ScrollViewer
        {
            Content = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    Hint("Marque lo que es IMPORTANTE. Lo desmarcado se muestra como diferencia menor (azul)."),
                    Heading("Elementos de gramática"), _elements, editGrammar,
                    Heading("Espacios y texto"), _leading, _embedded, _trailing, _everythingElse, _case,
                    Heading("Texto sin importancia"), _unimportant,
                    Heading("Varios"), _orphans, _lineEndings,
                },
            },
        };

        // Alineación
        _alignment =
        [
            new RadioButton { GroupName = "align", Content = "Sin alinear (línea N contra línea N)", Tag = AlignmentMethod.Unaligned },
            new RadioButton { GroupName = "align", Content = "Estándar (anclas por líneas poco frecuentes + Myers)", Tag = AlignmentMethod.Standard },
            new RadioButton { GroupName = "align", Content = "Myers O(ND) (LCS mínimo de todo el archivo)", Tag = AlignmentMethod.Myers },
            new RadioButton { GroupName = "align", Content = "Patience diff (anclas en líneas únicas)", Tag = AlignmentMethod.Patience },
        ];
        foreach (var rb in _alignment) rb.IsChecked = (AlignmentMethod)rb.Tag! == Settings.Alignment;
        _neverAlign.IsChecked = Settings.NeverAlignDifferences;
        _skew.Value = Settings.SkewTolerance;
        _closeness.IsChecked = Settings.UseClosenessMatching;
        var alignment = new StackPanel { Spacing = 6 };
        foreach (var rb in _alignment) alignment.Children.Add(rb);
        alignment.Children.Add(_neverAlign);
        alignment.Children.Add(Form(("Tolerancia de desfase (líneas):", _skew)));
        alignment.Children.Add(_closeness);
        alignment.Children.Add(Hint("Para forzar dos líneas lado a lado use Editar ▸ Alinear con… (Ctrl+Alt+A)."));
        var clearManual = new Button { Content = $"Quitar ajustes manuales ({Settings.ManualAlignments.Count} alineaciones, {Settings.IsolatedLeft.Count + Settings.IsolatedRight.Count} aisladas, {Settings.IgnoredLeft.Count + Settings.IgnoredRight.Count} ignoradas)" };
        clearManual.Click += (_, _) =>
        {
            Settings.ManualAlignments.Clear();
            Settings.IsolatedLeft.Clear(); Settings.IsolatedRight.Clear();
            Settings.IgnoredLeft.Clear(); Settings.IgnoredRight.Clear();
            clearManual.IsEnabled = false;
        };
        alignment.Children.Add(clearManual);

        // Reemplazos
        RefreshReplacements();
        var add = new Button { Content = "Nuevo…" };
        var edit = new Button { Content = "Editar…" };
        var remove = new Button { Content = "Eliminar" };
        add.Click += async (_, _) =>
        {
            var item = new Replacement();
            if (await new ReplacementDialog(item).ShowAsync(this)) { _replacementItems.Add(item); RefreshReplacements(); }
        };
        edit.Click += async (_, _) =>
        {
            if (_replacements.SelectedIndex < 0) return;
            if (await new ReplacementDialog(_replacementItems[_replacements.SelectedIndex]).ShowAsync(this)) RefreshReplacements();
        };
        remove.Click += (_, _) =>
        {
            if (_replacements.SelectedIndex < 0) return;
            _replacementItems.RemoveAt(_replacements.SelectedIndex);
            RefreshReplacements();
        };
        var replacements = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Hint("Cambios repetitivos que se consideran equivalentes (ej. renombrar una variable). Se muestran en morado."),
                _replacements,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { add, edit, remove } },
            },
        };

        Content = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Specs", Content = specs },
                new TabItem { Header = "Formato", Content = format },
                new TabItem { Header = "Importancia", Content = importance },
                new TabItem { Header = "Alineación", Content = alignment },
                new TabItem { Header = "Reemplazos", Content = replacements },
            },
        };
        Buttons.Children.Insert(0, _updateDefaults);
    }

    public CompareSettings Settings { get; }
    public string LeftSpec => _leftSpec.Text?.Trim() ?? "";
    public string RightSpec => _rightSpec.Text?.Trim() ?? "";
    public bool DisableEditing => _disableEditing.IsChecked == true;
    public string Description => _description.Text ?? "";
    public string? LeftFormat => _leftFormat.SelectedItem as string is { } s && s != Detected ? s : null;
    public string? RightFormat => _rightFormat.SelectedItem as string is { } s && s != Detected ? s : null;
    public string? LeftEncoding => _leftEncoding.SelectedItem as string is { } s && s != NoOverride ? s : null;
    public string? RightEncoding => _rightEncoding.SelectedItem as string is { } s && s != NoOverride ? s : null;
    public bool UpdateDefaults => _updateDefaults.IsChecked == true;

    protected override bool Validate()
    {
        foreach (var cb in _elements.Children.OfType<CheckBox>())
            Settings.ElementImportance[(string)cb.Tag!] = cb.IsChecked == true;
        Settings.LeadingWhitespaceImportant = _leading.IsChecked == true;
        Settings.EmbeddedWhitespaceImportant = _embedded.IsChecked == true;
        Settings.TrailingWhitespaceImportant = _trailing.IsChecked == true;
        Settings.EverythingElseImportant = _everythingElse.IsChecked == true;
        Settings.CaseSensitive = _case.IsChecked == true;
        Settings.UnimportantText = (_unimportant.Text ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.StartsWith('=') ? new UnimportantPattern { Pattern = l[1..], IsRegex = false } : new UnimportantPattern { Pattern = l })
            .ToList();
        Settings.OrphanLinesAlwaysImportant = _orphans.IsChecked == true;
        Settings.CompareLineEndings = _lineEndings.IsChecked == true;
        Settings.Alignment = (AlignmentMethod)(_alignment.First(r => r.IsChecked == true).Tag!);
        Settings.NeverAlignDifferences = _neverAlign.IsChecked == true;
        Settings.SkewTolerance = (int)(_skew.Value ?? 200);
        Settings.UseClosenessMatching = _closeness.IsChecked == true;
        Settings.Replacements = _replacementItems;
        return true;
    }

    private void RefreshReplacements()
    {
        _replacements.ItemsSource = null;
        _replacements.ItemsSource = _replacementItems.Select(r => r.ToString()).ToList();
    }

    private Control WithBrowse(TextBox box)
    {
        var browse = new Button { Content = "…", Margin = new Thickness(4, 0, 0, 0) };
        browse.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } p) box.Text = p;
        };
        var panel = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        panel.Children.Add(browse);
        panel.Children.Add(box);
        return panel;
    }
}
