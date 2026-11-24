using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DiffForge.App.Services;
using DiffForge.App.Views;

namespace DiffForge.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppSettings.Load();
            var window = new MainWindow();
            desktop.MainWindow = window;
            var args = CommandLine.Parse(desktop.Args ?? []);
            window.Opened += async (_, _) => await StartAsync(window, args);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static async Task StartAsync(MainWindow window, CommandLine args)
    {
        if (args.SessionPath is not null)
        {
            window.NewTab();
            await window.OpenSessionFileAsync(args.SessionPath);
            return;
        }

        var view = window.NewTab();
        view.LeftReadOnlyArg = args.ReadOnlyLeft;
        view.RightReadOnlyArg = args.ReadOnlyRight;
        if (args.Left is not null || args.Right is not null)
        {
            await view.LoadAsync(args.Left, args.Right);
            if (args.LeftTitle is not null) view.Left.Title = args.LeftTitle;
            if (args.RightTitle is not null) view.Right.Title = args.RightTitle;
        }
    }
}

/// <summary>
/// Línea de comandos: <c>DiffForge [izq] [der] [/title1=…] [/title2=…] [/ro] [/ro1] [/ro2]</c>
/// o <c>DiffForge sesion.dfsession</c>.
/// </summary>
public sealed record CommandLine(string? Left, string? Right, string? LeftTitle, string? RightTitle,
    bool ReadOnlyLeft, bool ReadOnlyRight, string? SessionPath)
{
    public static CommandLine Parse(string[] args)
    {
        var positional = new List<string>();
        string? t1 = null, t2 = null;
        bool ro1 = false, ro2 = false;
        foreach (var arg in args)
        {
            if (arg.StartsWith('/') || arg.StartsWith("--"))
            {
                var a = arg.TrimStart('/', '-');
                string key = a.Split('=')[0].ToLowerInvariant();
                string? value = a.Contains('=') ? a[(a.IndexOf('=') + 1)..] : null;
                switch (key)
                {
                    case "title1" or "lefttitle": t1 = value; break;
                    case "title2" or "righttitle": t2 = value; break;
                    case "ro": ro1 = ro2 = true; break;
                    case "ro1" or "leftreadonly": ro1 = true; break;
                    case "ro2" or "rightreadonly": ro2 = true; break;
                }
                continue;
            }
            positional.Add(arg);
        }

        if (positional.Count == 1 && positional[0].EndsWith(TextCompareView.SessionExtension, StringComparison.OrdinalIgnoreCase))
            return new CommandLine(null, null, t1, t2, ro1, ro2, positional[0]);

        return new CommandLine(positional.ElementAtOrDefault(0), positional.ElementAtOrDefault(1), t1, t2, ro1, ro2, null);
    }
}
