using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using DiffForge.App.Services;

namespace DiffForge.App.Tests;

/// <summary>
/// Genera los recursos gráficos desde <see cref="AppIcon"/>: PNG de 16 a 256 px y el .ico
/// multi-resolución. Solo escribe si DIFFFORGE_ASSETS apunta a la carpeta Assets de la app.
/// </summary>
public class BrandingTests
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    [AvaloniaFact]
    public void Logo_RendersAtAllSizes()
    {
        foreach (int size in Sizes)
        {
            using var png = Render(size);
            Assert.Equal(size, png.PixelSize.Width);
        }
    }

    [AvaloniaFact]
    public void GenerateAssets()
    {
        if (Environment.GetEnvironmentVariable("DIFFFORGE_ASSETS") is not { } dir) return;
        Directory.CreateDirectory(dir);
        var images = new List<byte[]>();
        foreach (int size in Sizes)
        {
            using var bmp = Render(size);
            using var ms = new MemoryStream();
            bmp.Save(ms);
            images.Add(ms.ToArray());
            if (size is 256 or 64) File.WriteAllBytes(Path.Combine(dir, $"diffforge-{size}.png"), ms.ToArray());
        }
        File.WriteAllBytes(Path.Combine(dir, "diffforge.ico"), BuildIco(Sizes, images));
    }

    private static RenderTargetBitmap Render(int size)
    {
        var bmp = new RenderTargetBitmap(new PixelSize(size, size));
        var image = new Image { Source = AppIcon.CreateLogo(), Width = size, Height = size };
        image.Measure(new Size(size, size));
        image.Arrange(new Rect(0, 0, size, size));
        bmp.Render(image);
        return bmp;
    }

    /// <summary>ICO con entradas PNG (formato válido desde Windows Vista).</summary>
    private static byte[] BuildIco(int[] sizes, List<byte[]> pngs)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(pngs[i].Length);
            w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var png in pngs) w.Write(png);
        return ms.ToArray();
    }
}
