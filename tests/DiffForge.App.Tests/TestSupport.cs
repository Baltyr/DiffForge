using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using DiffForge.App.Commands;
using DiffForge.App.Views;

[assembly: AvaloniaTestApplication(typeof(DiffForge.App.Tests.TestAppBuilder))]

namespace DiffForge.App.Tests;

public static class TestAppBuilder
{
    // La configuración del usuario no se toca: los tests usan una carpeta temporal propia.
    [ModuleInitializer]
    internal static void IsolateSettings() =>
        Environment.SetEnvironmentVariable("DIFFFORGE_HOME", Path.Combine(Path.GetTempPath(), "DiffForge.Tests", Guid.NewGuid().ToString("N")));

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>Ventana principal con dos archivos cargados y utilidades para manejar diálogos.</summary>
public sealed class Harness : IDisposable
{
    public const string LeftText =
        "using System;\r\n" +
        "namespace Demo\r\n" +
        "{\r\n" +
        "    // comentario viejo\r\n" +
        "    var total = 10;\r\n" +
        "    Console.WriteLine(\"hola\");\r\n" +
        "    Borrar();\r\n" +
        "    int a = 1;\r\n" +
        "    int b = 2;\r\n" +
        "}\r\n";

    public const string RightText =
        "using System;\r\n" +
        "namespace Demo  \r\n" +
        "{\r\n" +
        "    // comentario nuevo\r\n" +
        "    var total = 20;\r\n" +
        "    Console.WriteLine(\"hola\");\r\n" +
        "    int a = 1;\r\n" +
        "    Agregar();\r\n" +
        "    int b = 2;\r\n" +
        "}\r\n";

    private readonly List<Exception> _unhandled = [];

    public string Folder { get; } = Path.Combine(Path.GetTempPath(), "DiffForge.Tests", Guid.NewGuid().ToString("N"));
    public string LeftPath { get; }
    public string RightPath { get; }
    public MainWindow Window { get; }
    public TextCompareView View => Window.Active!;

    public Harness(string left = LeftText, string right = RightText, string extension = ".cs")
    {
        Directory.CreateDirectory(Folder);
        LeftPath = Path.Combine(Folder, "izq" + extension);
        RightPath = Path.Combine(Folder, "der" + extension);
        File.WriteAllText(LeftPath, left);
        File.WriteAllText(RightPath, right);

        Dispatcher.UIThread.UnhandledException += OnUnhandled;
        Window = new MainWindow();
        Window.Show();
        Window.NewTab();
        Run(View.LoadAsync(LeftPath, RightPath));
        Pump();
    }

    private void OnUnhandled(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _unhandled.Add(e.Exception);
        e.Handled = true;
    }

    public IReadOnlyList<Exception> Unhandled => _unhandled;

    /// <summary>Procesa la cola del dispatcher y la espera del debounce de recomparación.</summary>
    public void Pump(int rounds = 3)
    {
        for (int i = 0; i < rounds; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    /// <summary>Corre una tarea en el hilo de UI; cierra con "Cancelar" cualquier diálogo que se abra.</summary>
    public void Run(Task task, int maxRounds = 400)
    {
        for (int i = 0; i < maxRounds && !task.IsCompleted; i++)
        {
            Dispatcher.UIThread.RunJobs();
            foreach (var w in OwnedWindowsDeep(Window)) w.Close();
            Thread.Sleep(5);
        }
        if (!task.IsCompleted) throw new TimeoutException("El comando no terminó (¿diálogo sin cerrar?).");
        task.GetAwaiter().GetResult();
        Pump();
    }

    public void Execute(Cmd cmd) => Run(Window.ExecuteAsync(cmd));

    /// <summary>Espera el debounce de 350 ms tras una edición del usuario.</summary>
    public void WaitRecompare()
    {
        var until = DateTime.UtcNow.AddMilliseconds(600);
        while (DateTime.UtcNow < until) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
        Run(View.FlushAsync());
    }

    private static IEnumerable<Window> OwnedWindowsDeep(Window w)
    {
        foreach (var child in w.OwnedWindows.ToList())
        {
            foreach (var grand in OwnedWindowsDeep(child)) yield return grand;
            yield return child;
        }
    }

    public void Dispose()
    {
        Dispatcher.UIThread.UnhandledException -= OnUnhandled;
        try { Directory.Delete(Folder, recursive: true); } catch (IOException) { }
    }
}
