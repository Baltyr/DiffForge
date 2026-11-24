using Avalonia;

namespace DiffForge.App;

internal static class Program
{
    /// <summary>Uso: DiffForge.exe [archivoIzquierdo] [archivoDerecho]</summary>
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
