using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using DiffForge.App.Services;
using DiffForge.Core.Documents;
using DiffForge.Core.Formats;

namespace DiffForge.App.Dialogs;

/// <summary>Herramientas ▸ Formatos de archivo: máscaras, gramática, conversión y opciones varias.</summary>
public sealed class FileFormatsDialog : DialogBase
{
    private readonly List<FileFormat> _formats;
    private readonly ListBox _list = new() { Width = 200 };
    private readonly ContentControl _editor = new();
    private FileFormat? _current;

    // Campos del formato seleccionado
    private readonly TextBox _name = new();
    private readonly TextBox _masks = new();
    private readonly TextBox _description = new() { AcceptsReturn = true, Height = 50, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly ListBox _grammar = new() { Height = 220 };
    private readonly TextBox _caseInsensitive = new() { Watermark = "Elementos sin distinción de mayúsculas, separados por coma" };
    private readonly NumericUpDown _tabStop = new() { Minimum = 1, Maximum = 16, FormatString = "0", Width = 120 };
    private readonly CheckBox _insertSpaces = new() { Content = "Insertar espacios en vez de tabs" };
    private readonly CheckBox _independent = new() { Content = "Las líneas son independientes (navegar línea por línea)" };
    private readonly CheckBox _columns = new() { Content = "Datos por columnas (comparar carácter por posición)" };
    private readonly ComboBox _conversion = new() { ItemsSource = new[] { "Ninguna", "Programa externo" } };
    private readonly TextBox _loadCmd = new() { Watermark = "ej. \"C:\\tools\\pdftotext.exe\" %s %t" };
    private readonly TextBox _saveCmd = new() { Watermark = "%s = temporal editado, %t = destino, %o = original" };
    private readonly CheckBox _disableEditing = new() { Content = "Deshabilitar edición" };
    private readonly ComboBox _encoding = new();
    private readonly CheckBox _ctrlZ = new() { Content = "Ctrl+Z (0x1A) indica fin de archivo" };
    private readonly NumericUpDown _charLimit = new() { Minimum = 0, Maximum = 100000, FormatString = "0", Width = 120 };
    private readonly CheckBox _trimOnSave = new() { Content = "Quitar espacios al final al guardar" };
    private readonly CheckBox _tabsOnSave = new() { Content = "Convertir espacios iniciales a tabs al guardar" };

    public FileFormatsDialog(string? select = null) : base("Formatos de archivo", 900, 640)
    {
        _formats = AppSettings.DeepClone(AppSettings.Current.FileFormats);
        _encoding.ItemsSource = new[] { "(Detectar)" }.Concat(TextFileCodec.KnownEncodings).ToList();

        _list.SelectionChanged += (_, _) =>
        {
            Commit();
            _current = _list.SelectedIndex >= 0 ? _formats[_list.SelectedIndex] : null;
            Load();
        };

        var add = new Button { Content = "Nuevo" };
        var clone = new Button { Content = "Duplicar" };
        var remove = new Button { Content = "Eliminar" };
        var reset = new Button { Content = "Restablecer" };
        add.Click += (_, _) => { Commit(); _formats.Insert(0, new FileFormat { Name = "Nuevo formato", Masks = "*.ext" }); RefreshList(0); };
        clone.Click += (_, _) =>
        {
            if (_current is null) return;
            Commit();
            var copy = AppSettings.DeepClone(_current);
            copy.Name += " (copia)";
            _formats.Insert(_list.SelectedIndex, copy);
            RefreshList(_list.SelectedIndex);
        };
        remove.Click += (_, _) =>
        {
            if (_current is null || _current.Name == BuiltInFormats.PlainTextName) return;
            int i = _list.SelectedIndex;
            _current = null;
            _formats.RemoveAt(i);
            RefreshList(Math.Min(i, _formats.Count - 1));
        };
        reset.Click += (_, _) => { _current = null; _formats.Clear(); _formats.AddRange(BuiltInFormats.Create()); RefreshList(0); };

        var left = new DockPanel { Margin = new Thickness(0, 0, 12, 0) };
        var leftButtons = new WrapPanel { Children = { add, clone, remove, reset } };
        DockPanel.SetDock(leftButtons, Dock.Bottom);
        left.Children.Add(leftButtons);
        left.Children.Add(_list);

        _editor.Content = BuildEditor();
        var root = new DockPanel();
        DockPanel.SetDock(left, Dock.Left);
        root.Children.Add(left);
        root.Children.Add(_editor);
        Content = root;

        RefreshList(Math.Max(0, _formats.FindIndex(f => f.Name == select)));
    }

    private Control BuildEditor()
    {
        var addItem = new Button { Content = "Nuevo…" };
        var editItem = new Button { Content = "Editar…" };
        var removeItem = new Button { Content = "Eliminar" };
        var up = new Button { Content = "▲" };
        var down = new Button { Content = "▼" };
        addItem.Click += async (_, _) =>
        {
            if (_current is null) return;
            var item = new GrammarItem();
            if (await new GrammarItemDialog(item).ShowAsync(this)) { _current.Grammar.Add(item); RefreshGrammar(); }
        };
        editItem.Click += async (_, _) =>
        {
            if (_current is null || _grammar.SelectedIndex < 0) return;
            if (await new GrammarItemDialog(_current.Grammar[_grammar.SelectedIndex]).ShowAsync(this)) RefreshGrammar();
        };
        removeItem.Click += (_, _) =>
        {
            if (_current is null || _grammar.SelectedIndex < 0) return;
            _current.Grammar.RemoveAt(_grammar.SelectedIndex);
            RefreshGrammar();
        };
        up.Click += (_, _) => Move(-1);
        down.Click += (_, _) => Move(+1);

        var general = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Form(("Nombre:", _name), ("Máscaras:", _masks)),
                Hint("Máscaras separadas por ';' — ej. *.cs;*.csx"),
                Heading("Descripción"), _description,
            },
        };
        var grammar = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Hint("El orden importa: los ítems de arriba tienen precedencia. Se usan para resaltado y para decidir importancia."),
                _grammar,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { addItem, editItem, removeItem, up, down } },
                Form(("Sin distinción de mayúsculas:", _caseInsensitive)),
            },
        };
        var conversion = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Form(("Conversión:", _conversion), ("Al cargar:", _loadCmd), ("Al guardar:", _saveCmd)),
                Hint("Un programa de conversión es exitoso si termina con código 0 y genera un archivo no vacío."),
                _disableEditing,
                Form(("Codificación:", _encoding), ("Límite de caracteres por línea (0 = sin límite):", _charLimit)),
                _ctrlZ, _trimOnSave, _tabsOnSave,
            },
        };
        var misc = new StackPanel
        {
            Spacing = 8,
            Children = { _insertSpaces, Form(("Tabulación:", _tabStop)), _independent, _columns },
        };

        return new TabControl
        {
            Items =
            {
                new TabItem { Header = "General", Content = general },
                new TabItem { Header = "Gramática", Content = grammar },
                new TabItem { Header = "Conversión", Content = conversion },
                new TabItem { Header = "Varios", Content = misc },
            },
        };
    }

    private void Move(int delta)
    {
        if (_current is null) return;
        int i = _grammar.SelectedIndex, j = i + delta;
        if (i < 0 || j < 0 || j >= _current.Grammar.Count) return;
        (_current.Grammar[i], _current.Grammar[j]) = (_current.Grammar[j], _current.Grammar[i]);
        RefreshGrammar();
        _grammar.SelectedIndex = j;
    }

    private void RefreshList(int select)
    {
        _list.ItemsSource = null;
        _list.ItemsSource = _formats.Select(f => f.Name).ToList();
        _list.SelectedIndex = select;
    }

    private void RefreshGrammar()
    {
        _grammar.ItemsSource = null;
        _grammar.ItemsSource = _current?.Grammar.Select(g => g.ToString()).ToList();
    }

    private void Load()
    {
        _editor.IsEnabled = _current is not null;
        if (_current is not { } f) return;
        _name.Text = f.Name;
        _masks.Text = f.Masks;
        _description.Text = f.Description;
        RefreshGrammar();
        _caseInsensitive.Text = string.Join(", ", f.Elements.Where(e => !e.CaseSensitive).Select(e => e.Name));
        _tabStop.Value = f.TabStop;
        _insertSpaces.IsChecked = f.InsertSpacesInsteadOfTabs;
        _independent.IsChecked = f.LinesAreIndependent;
        _columns.IsChecked = f.ColumnBasedData;
        _conversion.SelectedIndex = (int)f.Conversion;
        _loadCmd.Text = f.LoadCommand;
        _saveCmd.Text = f.SaveCommand;
        _disableEditing.IsChecked = f.DisableEditing;
        _encoding.SelectedItem = f.EncodingName ?? "(Detectar)";
        _ctrlZ.IsChecked = f.CtrlZIsEndOfFile;
        _charLimit.Value = f.CharactersPerLineLimit;
        _trimOnSave.IsChecked = f.TrimTrailingWhitespaceOnSave;
        _tabsOnSave.IsChecked = f.LeadingSpacesToTabsOnSave;
    }

    private void Commit()
    {
        if (_current is not { } f) return;
        f.Name = string.IsNullOrWhiteSpace(_name.Text) ? f.Name : _name.Text.Trim();
        f.Masks = _masks.Text ?? "";
        f.Description = _description.Text ?? "";
        var insensitive = (_caseInsensitive.Text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        f.Elements = insensitive.Select(n => new GrammarElement { Name = n, CaseSensitive = false }).ToList();
        f.TabStop = (int)(_tabStop.Value ?? 4);
        f.InsertSpacesInsteadOfTabs = _insertSpaces.IsChecked == true;
        f.LinesAreIndependent = _independent.IsChecked == true;
        f.ColumnBasedData = _columns.IsChecked == true;
        f.Conversion = (ConversionMethod)Math.Max(0, _conversion.SelectedIndex);
        f.LoadCommand = _loadCmd.Text ?? "";
        f.SaveCommand = _saveCmd.Text ?? "";
        f.DisableEditing = _disableEditing.IsChecked == true;
        f.EncodingName = _encoding.SelectedItem as string is { } e && e != "(Detectar)" ? e : null;
        f.CtrlZIsEndOfFile = _ctrlZ.IsChecked == true;
        f.CharactersPerLineLimit = (int)(_charLimit.Value ?? 0);
        f.TrimTrailingWhitespaceOnSave = _trimOnSave.IsChecked == true;
        f.LeadingSpacesToTabsOnSave = _tabsOnSave.IsChecked == true;
    }

    protected override bool Validate()
    {
        Commit();
        if (!_formats.Any(f => f.Name == BuiltInFormats.PlainTextName))
            _formats.Add(BuiltInFormats.Create().First(f => f.Name == BuiltInFormats.PlainTextName));
        AppSettings.Current.FileFormats = _formats;
        AppSettings.Save();
        return true;
    }
}

public sealed class GrammarItemDialog : DialogBase
{
    private readonly GrammarItem _item;
    private readonly AutoCompleteBox _element = new()
    {
        ItemsSource = new[] { BuiltInFormats.Comment, BuiltInFormats.StringElement, BuiltInFormats.Keyword, BuiltInFormats.Number, BuiltInFormats.Directive, BuiltInFormats.Tag },
    };
    private readonly ComboBox _category = new() { ItemsSource = new[] { "Básico", "Delimitado", "Lista", "Columnas" } };
    private readonly TextBox _text = new();
    private readonly TextBox _start = new();
    private readonly TextBox _end = new();
    private readonly CheckBox _eol = new() { Content = "Termina en fin de línea" };
    private readonly TextBox _escape = new() { MaxLength = 1, Width = 60, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _tokens = new() { AcceptsReturn = true, Height = 120, Watermark = "Un token por línea o separados por espacios" };
    private readonly NumericUpDown _startCol = new() { Minimum = 1, Maximum = 100000, FormatString = "0", Width = 120 };
    private readonly NumericUpDown _endCol = new() { Minimum = 0, Maximum = 100000, FormatString = "0", Width = 120 };
    private readonly CheckBox _case = new() { Content = "Distinguir mayúsculas al reconocer" };
    private readonly CheckBox _regex = new() { Content = "Expresión regular" };
    private readonly StackPanel _basic, _delimited, _list, _columns;

    public GrammarItemDialog(GrammarItem item) : base("Ítem de gramática", 560, 520)
    {
        _item = item;
        _element.Text = item.Element;
        _category.SelectedIndex = (int)item.Category;
        _text.Text = item.Text;
        _start.Text = item.StartDelimiter;
        _end.Text = item.EndDelimiter;
        _eol.IsChecked = item.StopAtEndOfLine;
        _escape.Text = item.EscapeCharacter;
        _tokens.Text = string.Join("\n", item.Tokens);
        _startCol.Value = item.StartColumn;
        _endCol.Value = item.EndColumn;
        _case.IsChecked = item.MatchCase;
        _regex.IsChecked = item.IsRegex;

        _basic = new StackPanel { Children = { Form(("Texto:", _text)) } };
        _delimited = new StackPanel { Spacing = 6, Children = { Form(("Inicio:", _start), ("Fin:", _end), ("Escape:", _escape)), _eol } };
        _list = new StackPanel { Children = { _tokens } };
        _columns = new StackPanel { Children = { Form(("Columna inicial:", _startCol), ("Columna final (0 = fin de línea):", _endCol)) } };
        _category.SelectionChanged += (_, _) => ShowCategory();
        ShowCategory();

        Content = new StackPanel
        {
            Spacing = 8,
            Children = { Form(("Elemento:", _element), ("Categoría:", _category)), _basic, _delimited, _list, _columns, _case, _regex },
        };
    }

    private void ShowCategory()
    {
        _basic.IsVisible = _category.SelectedIndex == 0;
        _delimited.IsVisible = _category.SelectedIndex == 1;
        _list.IsVisible = _category.SelectedIndex == 2;
        _columns.IsVisible = _category.SelectedIndex == 3;
    }

    protected override bool Validate()
    {
        if (string.IsNullOrWhiteSpace(_element.Text)) return false;
        _item.Element = _element.Text.Trim();
        _item.Category = (GrammarCategory)Math.Max(0, _category.SelectedIndex);
        _item.Text = _text.Text ?? "";
        _item.StartDelimiter = _start.Text ?? "";
        _item.EndDelimiter = _end.Text ?? "";
        _item.StopAtEndOfLine = _eol.IsChecked == true;
        _item.EscapeCharacter = _escape.Text ?? "";
        _item.Tokens = (_tokens.Text ?? "").Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).ToList();
        _item.StartColumn = (int)(_startCol.Value ?? 1);
        _item.EndColumn = (int)(_endCol.Value ?? 0);
        _item.MatchCase = _case.IsChecked == true;
        _item.IsRegex = _regex.IsChecked == true;
        return true;
    }
}
