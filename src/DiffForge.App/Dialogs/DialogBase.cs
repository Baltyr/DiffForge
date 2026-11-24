using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace DiffForge.App.Dialogs;

/// <summary>Ventana modal con botones Aceptar/Cancelar y contenido arbitrario.</summary>
public class DialogBase : Window
{
    private readonly ContentControl _host = new();

    public DialogBase(string title, double width = 560, double height = 480)
    {
        Title = title;
        Width = width;
        Height = height;
        MinWidth = 360;
        MinHeight = 200;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        CanResize = true;
        Icon = Services.AppIcon.Load();

        var ok = new Button { Content = "Aceptar", IsDefault = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Classes.Add("accent");
        ok.Click += (_, _) => { if (Validate()) Close(true); };
        cancel.Click += (_, _) => Close(false);

        Buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        Buttons.Children.Add(ok);
        Buttons.Children.Add(cancel);
        var footer = new Border { Child = Buttons, Padding = new Thickness(16, 12), BorderThickness = new Thickness(0, 1, 0, 0) };
        footer.Classes.Add("chrome");

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        _host.Margin = new Thickness(16, 14, 16, 10);
        root.Children.Add(_host);
        base.Content = root;

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(false); };
    }

    protected StackPanel Buttons { get; }

    public new object? Content
    {
        get => _host.Content;
        set => _host.Content = value;
    }

    protected virtual bool Validate() => true;

    public Task<bool> ShowAsync(Window owner) => ShowDialog<bool>(owner);

    // Helpers de layout para formularios.
    public static Grid Form(params (string Label, Control Field)[] rows)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        for (int i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = new TextBlock { Text = rows[i].Label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 10, 3) };
            rows[i].Field.Margin = new Thickness(0, 3);
            Grid.SetRow(label, i);
            Grid.SetRow(rows[i].Field, i);
            Grid.SetColumn(rows[i].Field, 1);
            grid.Children.Add(label);
            grid.Children.Add(rows[i].Field);
        }
        return grid;
    }

    public static TextBlock Heading(string text) =>
        new() { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 4) };

    public static TextBlock Hint(string text) =>
        new() { Text = text, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
}

public static class MessageBox
{
    public static async Task<string> ShowAsync(Window owner, string title, string message, params string[] buttons)
    {
        if (buttons.Length == 0) buttons = ["Aceptar"];
        string result = buttons[^1];
        var window = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            MaxWidth = 640,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        for (int i = 0; i < buttons.Length; i++)
        {
            string text = buttons[i];
            var button = new Button { Content = text, MinWidth = 90, IsDefault = i == 0, IsCancel = i == buttons.Length - 1, HorizontalContentAlignment = HorizontalAlignment.Center };
            if (i == 0) button.Classes.Add("accent");
            button.Click += (_, _) => { result = text; window.Close(); };
            panel.Children.Add(button);
        }
        window.Content = new StackPanel
        {
            Margin = new Thickness(22, 20),
            Spacing = 18,
            Children =
            {
                new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 580 },
                panel,
            },
        };
        await window.ShowDialog(owner);
        return result;
    }

    public static async Task<string?> PromptAsync(Window owner, string title, string label, string initial = "")
    {
        var box = new TextBox { Text = initial };
        var dialog = new DialogBase(title, 420, 170) { Content = DialogBase.Form((label, box)) };
        dialog.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        return await dialog.ShowAsync(owner) ? box.Text : null;
    }
}

/// <summary>"Acerca de": logo, versión y descripción.</summary>
public sealed class AboutDialog : Window
{
    public AboutDialog()
    {
        Title = "Acerca de DiffForge";
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = Services.AppIcon.Load();

        var version = typeof(AboutDialog).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? "1.0.0";

        var ok = new Button { Content = "Cerrar", MinWidth = 90, IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Classes.Add("accent");
        ok.Click += (_, _) => Close();

        var muted = new TextBlock { Text = $"Versión {version}  ·  .NET {Environment.Version.ToString(2)}  ·  Avalonia", FontSize = 12 };
        muted.Classes.Add("muted");
        Content = new StackPanel
        {
            Margin = new Thickness(28, 24),
            Spacing = 14,
            Width = 380,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 16,
                    Children =
                    {
                        Services.AppIcon.CreateLogoControl(64),
                        new StackPanel
                        {
                            VerticalAlignment = VerticalAlignment.Center,
                            Spacing = 2,
                            Children =
                            {
                                new TextBlock { Text = "DiffForge", FontSize = 24, FontWeight = FontWeight.SemiBold },
                                muted,
                            },
                        },
                    },
                },
                new TextBlock
                {
                    Text = "Comparador de archivos de texto: alineación por líneas, diferencias dentro de la línea, reglas de importancia, gramáticas por lenguaje y edición en vivo.",
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock { Text = "© 2026 Baltyr · github.com/Baltyr/DiffForge", FontSize = 12, Classes = { "muted" } },
                ok,
            },
        };
    }
}
