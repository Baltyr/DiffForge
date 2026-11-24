using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using DiffForge.App.Commands;
using DiffForge.App.Dialogs;
using DiffForge.App.Services;

namespace DiffForge.App.Views;

/// <summary>Ventana principal: menús, barra de herramientas y pestañas de comparación.</summary>
public sealed class MainWindow : Window
{
    private readonly TabControl _tabs = new();
    private readonly Menu _menu = new();
    private readonly Border _toolbarHost = new();
    private readonly Dictionary<Cmd, MenuItem> _menuItems = [];
    private readonly Dictionary<Cmd, Button> _toolButtons = [];
    private bool _closingConfirmed;

    // Comandos que dentro de un TextBox común (buscar, ruta) deben seguir siendo edición normal.
    private static readonly HashSet<Cmd> TextBoxNativeCommands =
    [
        Cmd.Undo, Cmd.Redo, Cmd.SelectAll, Cmd.DeleteToStartOfWord, Cmd.DeleteToEndOfWord, Cmd.CopyLineToLeft, Cmd.CopyLineToRight,
    ];

    public MainWindow()
    {
        Title = "DiffForge";
        Width = 1400;
        Height = 860;
        MinWidth = 640;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        BuildMenu();
        _toolbarHost.Child = BuildToolbar();
        _toolbarHost.IsVisible = AppSettings.Current.Options.ShowToolbar;
        _toolbarHost.BorderThickness = new Thickness(0, 0, 0, 1);
        _toolbarHost.BorderBrush = new SolidColorBrush(Color.FromArgb(50, 128, 128, 128));

        _tabs.SelectionChanged += (_, _) => RefreshCommandStates();
        _tabs.Padding = new Thickness(0);

        var root = new DockPanel();
        DockPanel.SetDock(_menu, Dock.Top);
        DockPanel.SetDock(_toolbarHost, Dock.Top);
        root.Children.Add(_menu);
        root.Children.Add(_toolbarHost);
        root.Children.Add(_tabs);
        Content = root;

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Closing += OnClosing;
        ApplyTheme();
        AppSettings.Changed += () => { ApplyTheme(); _toolbarHost.IsVisible = AppSettings.Current.Options.ShowToolbar; };
    }

    public TextCompareView? Active => (_tabs.SelectedItem as TabItem)?.Content as TextCompareView;

    public static void ApplyTheme()
    {
        if (Application.Current is not { } app) return;
        app.RequestedThemeVariant = AppSettings.Current.Options.Theme switch
        {
            ThemeChoice.Light => ThemeVariant.Light,
            ThemeChoice.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    // ---------- Pestañas ----------

    public TextCompareView NewTab()
    {
        var view = new TextCompareView();
        var title = new TextBlock { Text = "Nueva comparación", VerticalAlignment = VerticalAlignment.Center, MaxWidth = 320, TextTrimming = TextTrimming.CharacterEllipsis };
        var close = new Button { Content = "✕", Padding = new Thickness(5, 0), Margin = new Thickness(6, 0, 0, 0), Background = Brushes.Transparent, FontSize = 11 };
        var tab = new TabItem
        {
            Header = new StackPanel { Orientation = Orientation.Horizontal, Children = { title, close } },
            Content = view,
            FontSize = 13,
            MinHeight = 30,
            Padding = new Thickness(10, 2),
        };
        close.Click += async (_, _) => await CloseTabAsync(tab);
        view.TitleChanged += () =>
        {
            title.Text = view.TabTitle;
            ToolTip.SetTip(title, $"{view.Left.Spec}\n{view.Right.Spec}");
            if (Active == view) { Title = $"{view.TabTitle} — DiffForge"; RefreshCommandStates(); }
        };
        view.NewComparisonRequested += async (l, r) => await NewTab().SetDocumentsAsync(l, r);
        _tabs.Items.Add(tab);
        _tabs.SelectedItem = tab;
        return view;
    }

    private async Task CloseTabAsync(TabItem tab)
    {
        if (tab.Content is TextCompareView view && !await view.ConfirmCloseAsync()) return;
        _tabs.Items.Remove(tab);
        if (_tabs.Items.Count == 0) NewTab();
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_closingConfirmed) return;
        e.Cancel = true;
        foreach (var view in _tabs.Items.OfType<TabItem>().Select(t => t.Content).OfType<TextCompareView>().ToList())
        {
            _tabs.SelectedItem = _tabs.Items.OfType<TabItem>().First(t => t.Content == view);
            if (!await view.ConfirmCloseAsync()) return;
        }
        AppSettings.Save();
        _closingConfirmed = true;
        Close();
    }

    // ---------- Menús y barra ----------

    private void BuildMenu()
    {
        foreach (var menuName in AppCommands.Menus)
        {
            var top = new MenuItem { Header = menuName };
            var submenus = new Dictionary<string, MenuItem>();
            foreach (var def in AppCommands.All.Where(c => c.Menu == menuName))
            {
                var parent = top;
                if (def.Submenu is { } sub)
                {
                    if (!submenus.TryGetValue(sub, out var subItem))
                    {
                        if (def.Separator) top.Items.Add(new Separator());
                        subItem = new MenuItem { Header = sub };
                        submenus[sub] = subItem;
                        top.Items.Add(subItem);
                    }
                    parent = subItem;
                }
                else if (def.Separator && top.Items.Count > 0)
                {
                    top.Items.Add(new Separator());
                }

                var item = new MenuItem
                {
                    Header = def.Unavailable ? def.Header + "  (no disponible)" : def.Header,
                    InputGesture = def.KeyGesture,
                    ToggleType = def.Toggle ? MenuItemToggleType.CheckBox : MenuItemToggleType.None,
                    IsEnabled = !def.Unavailable,
                };
                var id = def.Id;
                item.Click += async (_, _) => await ExecuteAsync(id);
                _menuItems[id] = item;
                parent.Items.Add(item);
            }
            top.SubmenuOpened += (_, _) => RefreshCommandStates();
            _menu.Items.Add(top);
        }
    }

    private Control BuildToolbar()
    {
        var bar = new WrapPanel { Margin = new Thickness(4, 2) };
        CommandDef? previous = null;
        foreach (var def in AppCommands.All.Where(c => c.ToolbarGlyph is not null))
        {
            if (previous is not null && previous.Menu != def.Menu)
                bar.Children.Add(new Border { Width = 1, Margin = new Thickness(6, 4), Background = new SolidColorBrush(Color.FromArgb(70, 128, 128, 128)) });
            var button = new Button
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(6, 2),
                Margin = new Thickness(1),
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = def.ToolbarGlyph, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = def.ToolbarLabel, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center },
                    },
                },
            };
            ToolTip.SetTip(button, def.Gesture is null ? def.Header : $"{def.Header} ({def.Gesture})");
            var id = def.Id;
            button.Click += async (_, _) => await ExecuteAsync(id);
            _toolButtons[id] = button;
            bar.Children.Add(button);
            previous = def;
        }
        return bar;
    }

    private void RefreshCommandStates()
    {
        var view = Active;
        foreach (var (id, item) in _menuItems)
        {
            var def = AppCommands.Get(id);
            if (def.Unavailable) continue;
            item.IsEnabled = view is null ? IsGlobal(id) : (IsGlobal(id) || view.CanExecute(id));
            if (def.Toggle) item.IsChecked = id == Cmd.Toolbar ? _toolbarHost.IsVisible : view?.IsChecked(id) ?? false;
        }
        foreach (var (id, button) in _toolButtons)
        {
            bool isChecked = view?.IsChecked(id) == true;
            button.Background = isChecked ? new SolidColorBrush(Color.FromArgb(60, 100, 140, 220)) : Brushes.Transparent;
            button.IsEnabled = view is null || view.CanExecute(id);
        }
    }

    private static bool IsGlobal(Cmd id) => id is Cmd.NewComparison or Cmd.OpenSession or Cmd.Exit or Cmd.CloseTab
        or Cmd.Options or Cmd.FileFormats or Cmd.About or Cmd.KeyboardShortcuts or Cmd.Toolbar;

    // ---------- Ejecución ----------

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        foreach (var def in AppCommands.All)
        {
            if (def.KeyGesture is not { } gesture || def.Unavailable || !gesture.Matches(e)) continue;
            if (e.Source is TextBox && TextBoxNativeCommands.Contains(def.Id)) return;
            e.Handled = true;
            _ = ExecuteAsync(def.Id);
            return;
        }
    }

    public async Task ExecuteAsync(Cmd cmd)
    {
        switch (cmd)
        {
            case Cmd.NewComparison: NewTab(); break;
            case Cmd.CloseTab:
                if (_tabs.SelectedItem is TabItem tab) await CloseTabAsync(tab);
                break;
            case Cmd.Exit: Close(); break;
            case Cmd.OpenSession: await OpenSessionAsync(); break;
            case Cmd.Options:
                await new OptionsDialog().ShowAsync(this);
                break;
            case Cmd.FileFormats:
                if (await new FileFormatsDialog(Active?.ActivePane.Side == Core.Compare.Side.Left ? Active.Left.Format.Name : Active?.Right.Format.Name).ShowAsync(this) && Active is { } v)
                    await v.ExecuteAsync(Cmd.ReloadFiles);
                break;
            case Cmd.Toolbar:
                _toolbarHost.IsVisible = !_toolbarHost.IsVisible;
                break;
            case Cmd.About:
                await MessageBox.ShowAsync(this, "Acerca de DiffForge",
                    "DiffForge — comparador de texto\nAvalonia + .NET 8 · AvaloniaEdit\n\nProyecto propio: implementación independiente, sin código ni recursos de terceros.");
                break;
            case Cmd.KeyboardShortcuts:
                await MessageBox.ShowAsync(this, "Atajos de teclado", string.Join("\n",
                    AppCommands.All.Where(c => c.Gesture is not null && !c.Unavailable && c.Submenu is null)
                        .Select(c => $"{c.Gesture,-24} {c.Header}")));
                break;
            default:
                if (Active is { } view) await view.ExecuteAsync(cmd);
                break;
        }
        RefreshCommandStates();
    }

    private async Task OpenSessionAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir sesión",
            FileTypeFilter = [new FilePickerFileType("Sesión DiffForge") { Patterns = ["*" + TextCompareView.SessionExtension] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) await OpenSessionFileAsync(path);
    }

    public async Task OpenSessionFileAsync(string path)
    {
        var view = Active is { Result: null } empty ? empty : NewTab();
        try
        {
            await view.LoadSessionAsync(path);
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException)
        {
            view.SetMessage($"No se pudo abrir la sesión: {ex.Message}");
        }
    }
}
