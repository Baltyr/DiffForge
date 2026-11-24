namespace DiffForge.Core.Compare;

public enum AlignmentMethod
{
    /// <summary>Línea N contra línea N, sin buscar coincidencias.</summary>
    Unaligned,
    /// <summary>Anclas por líneas poco frecuentes (histogram) y Myers en los tramos restantes.</summary>
    Standard,
    /// <summary>Myers O(ND) sobre todo el archivo (LCS mínimo).</summary>
    Myers,
    /// <summary>Patience diff: ancla en líneas únicas en ambos lados.</summary>
    Patience,
}

public enum ReplacementSide { Left, Right, Both }

/// <summary>Cambio repetitivo que se considera equivalente (ej. renombre de una variable).</summary>
public sealed class Replacement
{
    public string Find { get; set; } = string.Empty;
    public string ReplaceWith { get; set; } = string.Empty;
    public bool MatchCase { get; set; } = true;
    public bool WholeWords { get; set; }
    public bool IsRegex { get; set; }
    public ReplacementSide Side { get; set; } = ReplacementSide.Left;

    public override string ToString() => $"{Find} → {ReplaceWith} ({Side})";
}

/// <summary>Patrón de texto que siempre se considera sin importancia.</summary>
public sealed class UnimportantPattern
{
    public string Pattern { get; set; } = string.Empty;
    public bool IsRegex { get; set; } = true;
    public bool MatchCase { get; set; } = true;

    public override string ToString() => Pattern;
}

/// <summary>Reglas de la sesión: qué diferencias importan y cómo alinear líneas.</summary>
public sealed class CompareSettings
{
    // Alineación
    public AlignmentMethod Alignment { get; set; } = AlignmentMethod.Standard;
    public bool NeverAlignDifferences { get; set; }
    public bool UseClosenessMatching { get; set; } = true;
    /// <summary>Máximo desfase de líneas que se evalúa al emparejar por similitud.</summary>
    public int SkewTolerance { get; set; } = 200;

    // Importancia
    public bool LeadingWhitespaceImportant { get; set; }
    public bool EmbeddedWhitespaceImportant { get; set; }
    public bool TrailingWhitespaceImportant { get; set; }
    public bool EverythingElseImportant { get; set; } = true;
    public bool CaseSensitive { get; set; } = true;
    /// <summary>Importancia por elemento de gramática; si falta un elemento se asume importante.</summary>
    public Dictionary<string, bool> ElementImportance { get; set; } = [];
    public List<UnimportantPattern> UnimportantText { get; set; } = [];
    public bool OrphanLinesAlwaysImportant { get; set; }
    public bool CompareLineEndings { get; set; }

    public List<Replacement> Replacements { get; set; } = [];

    // Ajustes manuales hechos desde la vista (Align With / Isolate / Ignored).
    public List<(int Left, int Right)> ManualAlignments { get; set; } = [];
    public HashSet<int> IsolatedLeft { get; set; } = [];
    public HashSet<int> IsolatedRight { get; set; } = [];
    public HashSet<int> IgnoredLeft { get; set; } = [];
    public HashSet<int> IgnoredRight { get; set; } = [];

    public bool IsElementImportant(string element) =>
        !ElementImportance.TryGetValue(element, out var important) || important;

    public CompareSettings Clone()
    {
        var copy = (CompareSettings)MemberwiseClone();
        copy.ElementImportance = new(ElementImportance);
        copy.UnimportantText = UnimportantText.Select(p => new UnimportantPattern { Pattern = p.Pattern, IsRegex = p.IsRegex, MatchCase = p.MatchCase }).ToList();
        copy.Replacements = Replacements.Select(r => new Replacement
        {
            Find = r.Find, ReplaceWith = r.ReplaceWith, MatchCase = r.MatchCase, WholeWords = r.WholeWords, IsRegex = r.IsRegex, Side = r.Side,
        }).ToList();
        copy.ManualAlignments = [.. ManualAlignments];
        copy.IsolatedLeft = [.. IsolatedLeft];
        copy.IsolatedRight = [.. IsolatedRight];
        copy.IgnoredLeft = [.. IgnoredLeft];
        copy.IgnoredRight = [.. IgnoredRight];
        return copy;
    }
}
