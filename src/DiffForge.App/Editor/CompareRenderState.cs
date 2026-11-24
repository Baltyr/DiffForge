using Avalonia.Media;
using Avalonia.Media.Immutable;
using DiffForge.App.Services;
using DiffForge.Core.Compare;

namespace DiffForge.App.Editor;

public readonly record struct SearchHit(Side Side, int Row, int Start, int Length);

/// <summary>Estado compartido por los renderizadores de ambos paneles.</summary>
public sealed class CompareRenderState
{
    private readonly Dictionary<int, IntraLineResult> _intraCache = [];

    public ComparisonResult? Result { get; private set; }
    public IReadOnlyList<DiffRow> Rows => Result?.Rows ?? [];

    public bool IgnoreUnimportant { get; set; }
    public bool SyntaxHighlighting { get; set; } = true;
    public bool UseStripes { get; set; }
    public (int Start, int End)? CurrentSection { get; set; }
    public List<SearchHit> SearchHits { get; } = [];
    public HashSet<int> EditedLeft { get; set; } = [];
    public HashSet<int> EditedRight { get; set; } = [];
    /// <summary>Marcadores 0-9 → (lado, línea real).</summary>
    public Dictionary<int, (Side Side, int Line)> Bookmarks { get; } = [];

    public Palette Colors { get; private set; } = Palette.From(new Dictionary<string, string>());

    public void SetResult(ComparisonResult? result)
    {
        Result = result;
        _intraCache.Clear();
    }

    public void SetColors(Dictionary<string, string> scheme) => Colors = Palette.From(scheme);

    public DiffRow? RowAt(int row) => row >= 0 && row < Rows.Count ? Rows[row] : null;

    public bool IsDisplayedAsDifference(DiffRow row) =>
        row.IsDifference && (row.Important || !IgnoreUnimportant);

    public IntraLineResult? Intra(int rowIndex)
    {
        if (Result is null || rowIndex < 0 || rowIndex >= Rows.Count) return null;
        var row = Rows[rowIndex];
        if (row.Kind == RowKind.Same) return null;
        if (!_intraCache.TryGetValue(rowIndex, out var intra))
        {
            intra = Result.CompareRow(row);
            _intraCache[rowIndex] = intra;
        }
        return intra;
    }

    public HashSet<int> Edited(Side side) => side == Side.Left ? EditedLeft : EditedRight;
}

/// <summary>Pinceles inmutables derivados del esquema de colores activo.</summary>
public sealed class Palette
{
    private readonly Dictionary<string, IBrush> _brushes = [];

    public IBrush ImportantLine { get; private init; } = Brushes.Transparent;
    public IBrush ImportantText { get; private init; } = Brushes.Red;
    public IBrush ImportantTextBack { get; private init; } = Brushes.Transparent;
    public IBrush UnimportantLine { get; private init; } = Brushes.Transparent;
    public IBrush UnimportantText { get; private init; } = Brushes.Blue;
    public IBrush ReplacementText { get; private init; } = Brushes.Purple;
    public IBrush Filler { get; private init; } = Brushes.LightGray;
    public IBrush CurrentSection { get; private init; } = Brushes.Gray;
    public IBrush SearchMatch { get; private init; } = Brushes.Yellow;
    public IBrush EditedMarker { get; private init; } = Brushes.Orange;
    public IBrush Stripes { get; private init; } = Brushes.Transparent;

    public IBrush? Element(string element) => _brushes.GetValueOrDefault(element);

    public static Palette From(Dictionary<string, string> scheme)
    {
        IBrush B(string key, string fallback) =>
            new ImmutableSolidColorBrush(Color.TryParse(scheme.GetValueOrDefault(key, fallback), out var c) ? c : Color.Parse(fallback));

        var palette = new Palette
        {
            ImportantLine = B(ColorScheme.ImportantLine, "#FFF1F0"),
            ImportantText = B(ColorScheme.ImportantText, "#C8102E"),
            ImportantTextBack = B(ColorScheme.ImportantTextBack, "#FFD3D0"),
            UnimportantLine = B(ColorScheme.UnimportantLine, "#EEF4FF"),
            UnimportantText = B(ColorScheme.UnimportantText, "#2156C9"),
            ReplacementText = B(ColorScheme.ReplacementText, "#7B3FC4"),
            Filler = B(ColorScheme.Filler, "#F2F2F2"),
            CurrentSection = B(ColorScheme.CurrentSection, "#40A0A0A0"),
            SearchMatch = B(ColorScheme.SearchMatch, "#FFE58F"),
            EditedMarker = B(ColorScheme.EditedMarker, "#E59400"),
            Stripes = B(ColorScheme.Stripes, "#00000000"),
        };
        foreach (var (key, value) in scheme)
        {
            if (Color.TryParse(value, out var color)) palette._brushes[key] = new ImmutableSolidColorBrush(color);
        }
        return palette;
    }
}
