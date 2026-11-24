using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DiffForge.Core.Compare;

namespace DiffForge.App.Editor;

/// <summary>
/// Miniatura vertical de toda la comparación: una franja por lado coloreada por tipo de diferencia,
/// el rectángulo de la vista actual y un triángulo en la línea actual. Clic/arrastre para navegar.
/// </summary>
public sealed class ThumbnailControl : Control
{
    private static readonly IPen ViewportPen = new Pen(Brushes.Gray, 1);

    public CompareRenderState? State { get; set; }
    public double ViewportTop { get; set; }
    public double ViewportHeight { get; set; }
    public int CurrentRow { get; set; } = -1;

    /// <summary>Se dispara con la fracción vertical (0..1) donde el usuario hizo clic.</summary>
    public event Action<double>? Navigate;

    public ThumbnailControl()
    {
        Width = 28;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (State is null) return;

        var rows = State.Rows;
        int count = Math.Max(rows.Count, 1);
        double h = Bounds.Height, w = Bounds.Width;
        double rowH = h / count;
        double half = (w - 6) / 2;
        var colors = State.Colors;

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (!State.IsDisplayedAsDifference(row)) continue;
            double y = i * rowH;
            double height = Math.Max(rowH, 1);
            var brush = row.Important ? colors.ImportantText : colors.UnimportantText;
            if (row.Left >= 0) context.FillRectangle(brush, new Rect(3, y, half - 1, height));
            else context.FillRectangle(colors.Filler, new Rect(3, y, half - 1, height));
            if (row.Right >= 0) context.FillRectangle(brush, new Rect(3 + half + 1, y, half - 1, height));
            else context.FillRectangle(colors.Filler, new Rect(3 + half + 1, y, half - 1, height));
        }

        if (ViewportHeight > 0)
            context.DrawRectangle(null, ViewportPen, new Rect(0.5, ViewportTop * h + 0.5, w - 1, Math.Max(ViewportHeight * h, 4)));

        if (CurrentRow >= 0)
        {
            double y = (CurrentRow + 0.5) * rowH;
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(new Point(0, y - 4), true);
                g.LineTo(new Point(4, y));
                g.LineTo(new Point(0, y + 4));
                g.EndFigure(true);
            }
            context.DrawGeometry(Brushes.DimGray, null, geometry);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Pointer.Capture(this);
        RaiseNavigate(e.GetPosition(this).Y);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) RaiseNavigate(e.GetPosition(this).Y);
    }

    private void RaiseNavigate(double y) => Navigate?.Invoke(Math.Clamp(y / Math.Max(Bounds.Height, 1), 0, 1));
}
