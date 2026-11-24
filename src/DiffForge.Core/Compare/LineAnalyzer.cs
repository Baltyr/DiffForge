using System.Text;
using System.Text.RegularExpressions;
using DiffForge.Core.Documents;
using DiffForge.Core.Formats;

namespace DiffForge.Core.Compare;

public enum Side { Left, Right }

/// <summary>Línea analizada: texto, tramos de gramática y su clave comparable.</summary>
public sealed class LineInfo
{
    public required string Text { get; init; }
    public required LineEnding Ending { get; init; }
    public required IReadOnlyList<GrammarSpan> Grammar { get; init; }
    /// <summary>Solo el contenido importante (normalizado). Dos líneas con igual clave se alinean como iguales.</summary>
    public required string Key { get; init; }
    public required bool IsIsolated { get; init; }

    public bool HasImportantContent => Key.Length > 0;
}

/// <summary>Clasificación por carácter usada para la clave y el diff intra-línea.</summary>
public sealed class CharMap
{
    public CharMap(int length)
    {
        Folded = new char[length];
        Important = new bool[length];
        Replaced = new bool[length];
    }

    public char[] Folded { get; }
    public bool[] Important { get; }
    public bool[] Replaced { get; }
}

/// <summary>
/// Aplica las reglas de importancia de la sesión a cada línea de un lado:
/// gramática, espacios (inicio/medio/final), mayúsculas, texto sin importancia y reemplazos.
/// </summary>
public sealed class LineAnalyzer
{
    private readonly CompareSettings _settings;
    private readonly FileFormat _format;
    private readonly Side _side;
    private readonly GrammarTokenizer _tokenizer;
    private readonly Regex[] _unimportant;
    private readonly (Regex Find, string Replace, bool ApplyHere)[] _replacements;

    public LineAnalyzer(CompareSettings settings, FileFormat format, Side side)
    {
        _settings = settings;
        _format = format;
        _side = side;
        _tokenizer = new GrammarTokenizer(format);
        _unimportant = settings.UnimportantText
            .Where(p => p.Pattern.Length > 0)
            .Select(p => SafeRegex(p.IsRegex ? p.Pattern : Regex.Escape(p.Pattern), p.MatchCase))
            .Where(r => r is not null)
            .Select(r => r!)
            .ToArray();
        _replacements = settings.Replacements
            .Where(r => r.Find.Length > 0)
            .Select(r => BuildReplacement(r, side))
            .Where(r => r.Find is not null)
            .Select(r => (r.Find!, r.Replace, r.ApplyHere))
            .ToArray();
    }

    public FileFormat Format => _format;

    public LineInfo[] Analyze(IReadOnlyList<TextLine> lines)
    {
        var grammar = _tokenizer.TokenizeAll(lines.Select(l => l.Text).ToArray());
        var isolated = _side == Side.Left ? _settings.IsolatedLeft : _settings.IsolatedRight;
        var result = new LineInfo[lines.Count];

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            bool isIsolated = isolated.Contains(i);
            string key = isIsolated
                ? $"\u0002{_side}{i}"
                : BuildKey(line.Text, grammar[i], line.Ending);
            result[i] = new LineInfo { Text = line.Text, Ending = line.Ending, Grammar = grammar[i], Key = key, IsIsolated = isIsolated };
        }
        return result;
    }

    public CharMap Map(LineInfo line) => Map(line.Text, line.Grammar);

    private string BuildKey(string text, IReadOnlyList<GrammarSpan> grammar, LineEnding ending)
    {
        var map = Map(text, grammar, out var substitutions);
        var sb = new StringBuilder(text.Length);
        int sub = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (sub < substitutions.Count && substitutions[sub].Start == i)
            {
                sb.Append(substitutions[sub].Text);
                i += substitutions[sub].Length - 1;
                sub++;
                continue;
            }
            if (map.Important[i]) sb.Append(map.Folded[i]);
        }
        if (_settings.CompareLineEndings) sb.Append('\u0001').Append((int)ending);
        return sb.ToString();
    }

    private CharMap Map(string text, IReadOnlyList<GrammarSpan> grammar) => Map(text, grammar, out _);

    private CharMap Map(string text, IReadOnlyList<GrammarSpan> grammar, out List<(int Start, int Length, string Text)> substitutions)
    {
        int n = text.Length;
        var map = new CharMap(n);
        var element = new string?[n];
        foreach (var span in grammar)
        {
            for (int i = span.Start; i < span.Start + span.Length && i < n; i++) element[i] ??= span.Element;
        }

        int firstNonWs = 0;
        while (firstNonWs < n && char.IsWhiteSpace(text[firstNonWs]) && element[firstNonWs] is null) firstNonWs++;
        int lastNonWs = n - 1;
        while (lastNonWs >= 0 && char.IsWhiteSpace(text[lastNonWs]) && element[lastNonWs] is null) lastNonWs--;

        var unimportant = new bool[n];
        foreach (var regex in _unimportant)
        {
            foreach (Match m in regex.Matches(text))
            {
                for (int i = m.Index; i < m.Index + m.Length; i++) unimportant[i] = true;
            }
        }

        for (int i = 0; i < n; i++)
        {
            char c = text[i];
            bool important;
            bool caseSensitive;
            if (unimportant[i])
            {
                important = false;
                caseSensitive = true;
            }
            else if (element[i] is { } el)
            {
                important = _settings.IsElementImportant(el);
                caseSensitive = _format.IsElementCaseSensitive(el);
            }
            else if (char.IsWhiteSpace(c))
            {
                important = i < firstNonWs ? _settings.LeadingWhitespaceImportant
                    : i > lastNonWs ? _settings.TrailingWhitespaceImportant
                    : _settings.EmbeddedWhitespaceImportant;
                caseSensitive = true;
            }
            else
            {
                important = _settings.EverythingElseImportant;
                caseSensitive = _settings.CaseSensitive;
            }

            map.Important[i] = important;
            map.Folded[i] = caseSensitive ? c : char.ToLowerInvariant(c);
        }

        substitutions = [];
        foreach (var (find, replace, applyHere) in _replacements)
        {
            foreach (Match m in find.Matches(text))
            {
                if (m.Length == 0) continue;
                for (int i = m.Index; i < m.Index + m.Length; i++) map.Replaced[i] = true;
                if (applyHere && !substitutions.Any(s => s.Start < m.Index + m.Length && m.Index < s.Start + s.Length))
                    substitutions.Add((m.Index, m.Length, m.Result(replace)));
            }
        }
        substitutions.Sort((x, y) => x.Start.CompareTo(y.Start));
        return map;
    }

    private static (Regex? Find, string Replace, bool ApplyHere) BuildReplacement(Replacement r, Side side)
    {
        bool applyHere = r.Side == ReplacementSide.Both
            || (r.Side == ReplacementSide.Left && side == Side.Left)
            || (r.Side == ReplacementSide.Right && side == Side.Right);

        // En el lado que recibe el reemplazo se busca "Find"; en el otro se marca el texto resultante.
        string pattern = applyHere
            ? (r.IsRegex ? r.Find : Regex.Escape(r.Find))
            : Regex.Escape(r.ReplaceWith);
        if (!applyHere && r.ReplaceWith.Length == 0) return (null, string.Empty, false);
        if (r.WholeWords) pattern = $@"\b(?:{pattern})\b";

        string replacement = r.IsRegex ? r.ReplaceWith : r.ReplaceWith.Replace("$", "$$");
        return (SafeRegex(pattern, r.MatchCase), replacement, applyHere);
    }

    private static Regex? SafeRegex(string pattern, bool matchCase)
    {
        try
        {
            return new Regex(pattern, RegexOptions.CultureInvariant | (matchCase ? RegexOptions.None : RegexOptions.IgnoreCase),
                TimeSpan.FromMilliseconds(200));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
