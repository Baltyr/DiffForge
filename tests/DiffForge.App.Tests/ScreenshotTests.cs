using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using DiffForge.App.Commands;

namespace DiffForge.App.Tests;

/// <summary>
/// Capturas de la ventana renderizada sin pantalla, para revisar el diseño. Solo corren si
/// DIFFFORGE_SCREENSHOTS apunta a una carpeta de salida.
/// </summary>
public class ScreenshotTests
{
    private static string? OutDir => Environment.GetEnvironmentVariable("DIFFFORGE_SCREENSHOTS");

    [AvaloniaFact]
    public void Capture()
    {
        if (OutDir is not { } dir) return;
        Directory.CreateDirectory(dir);
        foreach (var (name, theme) in new[] { ("claro", DiffForge.App.Services.ThemeChoice.Light), ("oscuro", DiffForge.App.Services.ThemeChoice.Dark) })
        {
            DiffForge.App.Services.AppSettings.Current.Options.Theme = theme;
            using var h = new Harness();
            h.Window.Width = 1400;
            h.Window.Height = 820;
            h.View.LeftPane.Editor.Focus();
            h.Execute(Cmd.NextSection);
            h.Pump(6);
            h.Window.CaptureRenderedFrame()!.Save(Path.Combine(dir, $"main-{name}.png"));

            h.Execute(Cmd.Find);
            h.Execute(Cmd.HexDetails);
            h.Pump(6);
            h.Window.CaptureRenderedFrame()!.Save(Path.Combine(dir, $"buscar-{name}.png"));

            foreach (var (file, dialog) in new (string, Avalonia.Controls.Window)[]
            {
                ("acerca", new DiffForge.App.Dialogs.AboutDialog()),
                ("sesion", new DiffForge.App.Dialogs.SessionSettingsDialog(h.View)),
                ("opciones", new DiffForge.App.Dialogs.OptionsDialog()),
            })
            {
                dialog.Show(h.Window);
                h.Pump(6);
                dialog.CaptureRenderedFrame()!.Save(Path.Combine(dir, $"{file}-{name}.png"));
                dialog.Close();
            }
            h.Window.Close();
        }
        DiffForge.App.Services.AppSettings.Current.Options.Theme = DiffForge.App.Services.ThemeChoice.System;
        DiffForge.App.Views.MainWindow.ApplyTheme();
    }
}
