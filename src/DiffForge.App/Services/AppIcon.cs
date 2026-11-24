using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;

namespace DiffForge.App.Services;

/// <summary>
/// Logo de DiffForge dibujado en vectores: dos documentos lado a lado con una línea
/// quitada (coral) y una agregada (menta). Es la fuente única del ícono: el .ico y los
/// PNG de Assets se generan desde aquí (ver tests/DiffForge.App.Tests/BrandingTests).
/// </summary>
public static class AppIcon
{
    public const double Size = 256;

    public static readonly Color GradientStart = Color.Parse("#6366F1");
    public static readonly Color GradientEnd = Color.Parse("#0EA5E9");

    public static DrawingImage CreateLogo() => new(CreateDrawing());

    public static DrawingGroup CreateDrawing()
    {
        var group = new DrawingGroup();
        var background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(GradientStart, 0), new GradientStop(GradientEnd, 1) },
        };
        group.Children.Add(Rect(background, 8, 8, 240, 240, 56));

        IBrush paper = new SolidColorBrush(Colors.White);
        IBrush line = new SolidColorBrush(Color.Parse("#C7CCF5"));
        IBrush removed = new SolidColorBrush(Color.Parse("#F87171"));
        IBrush added = new SolidColorBrush(Color.Parse("#34D399"));

        foreach (var (x, highlight) in new[] { (50.0, removed), (136.0, added) })
        {
            group.Children.Add(Rect(paper, x, 52, 70, 152, 14));
            double[] widths = [46, 38, 46, 30];
            for (int i = 0; i < widths.Length; i++)
            {
                double y = 78 + i * 28;
                group.Children.Add(Rect(i == 1 ? highlight : line, x + 12, y, i == 1 ? 46 : widths[i], 12, 6));
            }
        }
        return group;
    }

    private static GeometryDrawing Rect(IBrush brush, double x, double y, double w, double h, double r) =>
        new() { Brush = brush, Geometry = new RectangleGeometry(new Rect(x, y, w, h), r, r) };

    /// <summary>Ícono de ventana desde el .ico empaquetado; null si no está (no debe romper el arranque).</summary>
    public static WindowIcon? Load()
    {
        var uri = new Uri("avares://DiffForge/Assets/diffforge.ico");
        return AssetLoader.Exists(uri) ? new WindowIcon(AssetLoader.Open(uri)) : null;
    }

    /// <summary>Control con el logo, para el diálogo "Acerca de".</summary>
    public static Control CreateLogoControl(double size) => new Image { Source = CreateLogo(), Width = size, Height = size };
}
