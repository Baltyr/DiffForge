using System.Text;

namespace DiffForge.Core.Documents;

public enum LineEnding { None, CrLf, Lf, Cr }

public readonly record struct TextLine(string Text, LineEnding Ending);

/// <summary>
/// Contenido de un archivo de texto preservando todo lo necesario para guardarlo byte a byte:
/// codificación, BOM y el final de línea de cada línea (incluida la ausencia de salto final).
/// </summary>
public sealed class TextFile
{
    public TextFile(IReadOnlyList<TextLine> lines, Encoding encoding, bool hasBom)
    {
        Lines = lines;
        Encoding = encoding;
        HasBom = hasBom;
    }

    public IReadOnlyList<TextLine> Lines { get; }
    public Encoding Encoding { get; }
    public bool HasBom { get; }

    public static TextFile Empty { get; } = new([], new UTF8Encoding(false), false);

    /// <summary>Final de línea más frecuente; se usa para líneas nuevas.</summary>
    public LineEnding DominantEnding
    {
        get
        {
            int crlf = 0, lf = 0, cr = 0;
            foreach (var line in Lines)
            {
                switch (line.Ending)
                {
                    case LineEnding.CrLf: crlf++; break;
                    case LineEnding.Lf: lf++; break;
                    case LineEnding.Cr: cr++; break;
                }
            }
            if (crlf == 0 && lf == 0 && cr == 0) return OperatingSystem.IsWindows() ? LineEnding.CrLf : LineEnding.Lf;
            return crlf >= lf && crlf >= cr ? LineEnding.CrLf : lf >= cr ? LineEnding.Lf : LineEnding.Cr;
        }
    }

    public string LineEndingSummary
    {
        get
        {
            var kinds = Lines.Select(l => l.Ending).Where(e => e != LineEnding.None).Distinct().ToList();
            return kinds.Count switch
            {
                0 => "—",
                1 => kinds[0] switch { LineEnding.CrLf => "PC (CRLF)", LineEnding.Lf => "Unix (LF)", _ => "Mac (CR)" },
                _ => "Mixtos",
            };
        }
    }

    public TextFile WithLines(IReadOnlyList<TextLine> lines) => new(lines, Encoding, HasBom);

    public string ToText()
    {
        var sb = new StringBuilder();
        foreach (var line in Lines) sb.Append(line.Text).Append(line.Ending.AsString());
        return sb.ToString();
    }
}

public static class LineEndingExtensions
{
    public static string AsString(this LineEnding ending) => ending switch
    {
        LineEnding.CrLf => "\r\n",
        LineEnding.Lf => "\n",
        LineEnding.Cr => "\r",
        _ => string.Empty,
    };

    public static string ShortName(this LineEnding ending) => ending switch
    {
        LineEnding.CrLf => "CRLF",
        LineEnding.Lf => "LF",
        LineEnding.Cr => "CR",
        _ => "",
    };
}
