using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DiffForge.App.Services;

namespace DiffForge.App.Dialogs;

/// <summary>Herramientas ▸ Opciones: edición, siguiente diferencia, apariencia, backups y vista.</summary>
public sealed class OptionsDialog : DialogBase
{
    private readonly ProgramOptions _o;
    private readonly List<(CheckBox Box, Action<bool> Set)> _checks = [];
    private readonly NumericUpDown _context = new() { Minimum = 0, Maximum = 100, FormatString = "0", Width = 120 };
    private readonly ComboBox _theme = new() { ItemsSource = new[] { "Seguir al sistema operativo", "Claro", "Oscuro" } };
    private readonly TextBox _backupFolder = new() { Watermark = "(misma carpeta del archivo)" };
    private readonly TextBox _backupSuffix = new();
    private readonly Dictionary<string, TextBox> _lightColors = [];
    private readonly Dictionary<string, TextBox> _darkColors = [];

    public OptionsDialog() : base("Opciones", 700, 620)
    {
        _o = AppSettings.DeepClone(AppSettings.Current.Options);

        var editing = Page(
            Check("Sangría automática al crear una línea", _o.AutoIndent, v => _o.AutoIndent = v),
            Check("Backspace des-sangra dentro del margen inicial", _o.BackspaceUnindents, v => _o.BackspaceUnindents = v),
            Check("Inicializar 'Buscar' con la palabra actual", _o.InitFindWithCurrentWord, v => _o.InitFindWithCurrentWord = v),
            Check("Mostrar cantidad de líneas ocultas por filtros", _o.ShowFilteredLineCounts, v => _o.ShowFilteredLineCounts = v),
            Form(("Líneas de contexto (Mostrar contexto):", _context)));
        _context.Value = _o.ContextLines;

        var nextDiff = Page(
            Check("Al cargar archivos, ir a la primera diferencia", _o.GoToFirstDifferenceOnLoad, v => _o.GoToFirstDifferenceOnLoad = v),
            Check("Después de copiar al otro lado, ir a la siguiente diferencia", _o.GoToNextDifferenceAfterCopy, v => _o.GoToNextDifferenceAfterCopy = v),
            Check("Al llegar al final, volver a la primera diferencia", _o.WrapAroundDifferences, v => _o.WrapAroundDifferences = v),
            Check("Mostrar mensaje cuando no hay más diferencias", _o.ShowNoMoreDifferencesMessage, v => _o.ShowNoMoreDifferencesMessage = v));

        _theme.SelectedIndex = (int)_o.Theme;
        var colorGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,110,110") };
        colorGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        AddCell(colorGrid, new TextBlock { Text = "Elemento", FontWeight = FontWeight.SemiBold }, 0, 0);
        AddCell(colorGrid, new TextBlock { Text = "Tema claro", FontWeight = FontWeight.SemiBold }, 0, 1);
        AddCell(colorGrid, new TextBlock { Text = "Tema oscuro", FontWeight = FontWeight.SemiBold }, 0, 2);
        int row = 1;
        foreach (var key in ColorScheme.DefaultLight().Keys)
        {
            colorGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            AddCell(colorGrid, new TextBlock { Text = key, VerticalAlignment = VerticalAlignment.Center }, row, 0);
            AddCell(colorGrid, _lightColors[key] = ColorBox(_o.Colors.Light.GetValueOrDefault(key, "#000000")), row, 1);
            AddCell(colorGrid, _darkColors[key] = ColorBox(_o.Colors.Dark.GetValueOrDefault(key, "#000000")), row, 2);
            row++;
        }
        var resetColors = new Button { Content = "Restablecer colores" };
        resetColors.Click += (_, _) =>
        {
            foreach (var (k, v) in ColorScheme.DefaultLight()) if (_lightColors.TryGetValue(k, out var b)) b.Text = v;
            foreach (var (k, v) in ColorScheme.DefaultDark()) if (_darkColors.TryGetValue(k, out var b)) b.Text = v;
        };
        var appearance = new ScrollViewer
        {
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    Form(("Tema:", _theme)),
                    Check("Franjas alternas en el fondo", _o.UseStripes, v => _o.UseStripes = v),
                    Heading("Colores de las vistas de archivo (#RRGGBB o #AARRGGBB)"),
                    colorGrid,
                    resetColors,
                },
            },
        };

        _backupFolder.Text = _o.BackupFolder;
        _backupSuffix.Text = _o.BackupSuffix;
        var backups = Page(
            Check("Crear copia de seguridad antes de guardar", _o.BackupOnSave, v => _o.BackupOnSave = v),
            Form(("Carpeta de backups:", _backupFolder), ("Sufijo:", _backupSuffix)));

        var view = Page(
            Check("Números de línea", _o.ShowLineNumbers, v => _o.ShowLineNumbers = v),
            Check("Miniatura", _o.ShowThumbnail, v => _o.ShowThumbnail = v),
            Check("Detalle de texto", _o.ShowTextDetails, v => _o.ShowTextDetails = v),
            Check("Información de archivo", _o.ShowFileInfo, v => _o.ShowFileInfo = v),
            Check("Barra de herramientas", _o.ShowToolbar, v => _o.ShowToolbar = v),
            Check("Resaltado de sintaxis", _o.SyntaxHighlighting, v => _o.SyntaxHighlighting = v),
            Hint("Se aplica a las comparaciones nuevas."));

        Content = new TabControl
        {
            TabStripPlacement = Dock.Left,
            Items =
            {
                new TabItem { Header = "Edición de texto", Content = editing },
                new TabItem { Header = "Siguiente diferencia", Content = nextDiff },
                new TabItem { Header = "Apariencia", Content = appearance },
                new TabItem { Header = "Copias de seguridad", Content = backups },
                new TabItem { Header = "Vista por defecto", Content = view },
            },
        };
    }

    private static StackPanel Page(params Control[] children)
    {
        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(8, 0) };
        foreach (var c in children) panel.Children.Add(c);
        return panel;
    }

    private CheckBox Check(string text, bool value, Action<bool> set)
    {
        var box = new CheckBox { Content = text, IsChecked = value };
        _checks.Add((box, set));
        return box;
    }

    private static TextBox ColorBox(string value)
    {
        var box = new TextBox { Text = value };
        void Paint()
        {
            if (Color.TryParse(box.Text, out var c))
            {
                box.BorderBrush = new SolidColorBrush(c);
                box.BorderThickness = new Thickness(0, 0, 0, 4);
            }
        }
        box.TextChanged += (_, _) => Paint();
        Paint();
        return box;
    }

    private static void AddCell(Grid grid, Control control, int row, int column)
    {
        control.Margin = new Thickness(0, 2, 8, 2);
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    protected override bool Validate()
    {
        foreach (var (box, set) in _checks) set(box.IsChecked == true);
        _o.ContextLines = (int)(_context.Value ?? 3);
        _o.Theme = (ThemeChoice)Math.Max(0, _theme.SelectedIndex);
        _o.BackupFolder = _backupFolder.Text ?? string.Empty;
        _o.BackupSuffix = string.IsNullOrWhiteSpace(_backupSuffix.Text) ? ".bak" : _backupSuffix.Text;
        foreach (var (k, b) in _lightColors) if (Color.TryParse(b.Text, out _)) _o.Colors.Light[k] = b.Text!;
        foreach (var (k, b) in _darkColors) if (Color.TryParse(b.Text, out _)) _o.Colors.Dark[k] = b.Text!;

        AppSettings.Current.Options = _o;
        AppSettings.Save();
        return true;
    }
}
