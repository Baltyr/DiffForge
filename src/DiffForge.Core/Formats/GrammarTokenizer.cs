using System.Text.RegularExpressions;

namespace DiffForge.Core.Formats;

/// <summary>Tramo de una línea reconocido por un ítem de gramática.</summary>
public readonly record struct GrammarSpan(int Start, int Length, string Element);

/// <summary>
/// Tokeniza líneas según la gramática de un <see cref="FileFormat"/>. Es secuencial: un elemento
/// delimitado sin cierre (ej. comentario de bloque) continúa en las líneas siguientes.
/// El orden de los ítems define la precedencia cuando dos coinciden en la misma posición.
/// </summary>
public sealed class GrammarTokenizer
{
    private readonly CompiledItem[] _items;

    public GrammarTokenizer(FileFormat format)
    {
        _items = format.Grammar.Select(Compile).Where(c => c is not null).Select(c => c!).ToArray();
    }

    public bool IsEmpty => _items.Length == 0;

    /// <summary>Tokeniza todas las líneas manteniendo el estado multilínea.</summary>
    public IReadOnlyList<GrammarSpan>[] TokenizeAll(IReadOnlyList<string> lines)
    {
        var result = new IReadOnlyList<GrammarSpan>[lines.Count];
        int open = -1;
        for (int i = 0; i < lines.Count; i++) result[i] = TokenizeLine(lines[i], ref open);
        return result;
    }

    /// <param name="openItem">Índice del ítem delimitado abierto desde la línea anterior, o -1.</param>
    public List<GrammarSpan> TokenizeLine(string line, ref int openItem)
    {
        var spans = new List<GrammarSpan>();
        if (_items.Length == 0) return spans;

        int pos = 0;
        if (openItem >= 0)
        {
            var item = _items[openItem];
            int end = item.FindEnd(line, 0);
            if (end < 0)
            {
                if (line.Length > 0) spans.Add(new GrammarSpan(0, line.Length, item.Element));
                return spans;
            }
            spans.Add(new GrammarSpan(0, end, item.Element));
            pos = end;
            openItem = -1;
        }

        while (pos < line.Length)
        {
            int bestStart = int.MaxValue, bestLength = 0, bestItem = -1;
            for (int i = 0; i < _items.Length; i++)
            {
                var (start, length) = _items[i].FindStart(line, pos);
                if (start >= 0 && start < bestStart && (length > 0 || _items[i].Delimited))
                {
                    bestStart = start; bestLength = length; bestItem = i;
                }
            }
            if (bestItem < 0) break;

            var best = _items[bestItem];
            if (!best.Delimited)
            {
                spans.Add(new GrammarSpan(bestStart, bestLength, best.Element));
                pos = bestStart + Math.Max(bestLength, 1);
                continue;
            }

            int end = best.FindEnd(line, bestStart + bestLength);
            if (end < 0)
            {
                spans.Add(new GrammarSpan(bestStart, line.Length - bestStart, best.Element));
                if (!best.StopAtEol) openItem = bestItem;
                break;
            }
            spans.Add(new GrammarSpan(bestStart, end - bestStart, best.Element));
            pos = end;
        }
        return spans;
    }

    private static CompiledItem? Compile(GrammarItem item)
    {
        var options = RegexOptions.CultureInvariant | (item.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
        string Pattern(string s) => item.IsRegex ? s : Regex.Escape(s);

        try
        {
            switch (item.Category)
            {
                case GrammarCategory.Basic when item.Text.Length > 0:
                    return new CompiledItem(item.Element, new Regex(Pattern(item.Text), options), null, false, false, null, 0, 0);

                case GrammarCategory.List when item.Tokens.Count > 0:
                    // Un token que empieza/termina en carácter de palabra exige límite de palabra en ese extremo.
                    string Bounded(string t) => item.IsRegex
                        ? $@"(?<!\w)(?:{t})(?!\w)"
                        : (IsWordChar(t[0]) ? @"(?<!\w)" : "") + Regex.Escape(t) + (IsWordChar(t[^1]) ? @"(?!\w)" : "");
                    var alternatives = string.Join("|", item.Tokens.Where(t => t.Length > 0).OrderByDescending(t => t.Length).Select(Bounded));
                    var listRegex = new Regex(alternatives, options);
                    return new CompiledItem(item.Element, listRegex, null, false, false, null, 0, 0);

                case GrammarCategory.Delimited when item.StartDelimiter.Length > 0:
                    Regex? end = item.StopAtEndOfLine && item.EndDelimiter.Length == 0
                        ? null
                        : new Regex(Pattern(item.EndDelimiter), options);
                    char? escape = item.EscapeCharacter.Length > 0 ? item.EscapeCharacter[0] : null;
                    return new CompiledItem(item.Element, new Regex(Pattern(item.StartDelimiter), options), end, true,
                        item.StopAtEndOfLine, escape, 0, 0);

                case GrammarCategory.Columns:
                    return new CompiledItem(item.Element, null, null, false, false, null,
                        Math.Max(0, item.StartColumn - 1), item.EndColumn);
            }
        }
        catch (ArgumentException)
        {
            // Regex inválida en un ítem definido por el usuario: se ignora el ítem.
        }
        return null;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private sealed record CompiledItem(
        string Element,
        Regex? Start,
        Regex? End,
        bool Delimited,
        bool StopAtEol,
        char? Escape,
        int StartColumn,
        int EndColumn)
    {
        public (int Start, int Length) FindStart(string line, int from)
        {
            if (Start is null)
            {
                int colEnd = EndColumn == 0 ? line.Length : Math.Min(EndColumn, line.Length);
                if (StartColumn >= colEnd || from >= colEnd) return (-1, 0);
                int s = Math.Max(from, StartColumn);
                return (s, colEnd - s);
            }
            var m = Start.Match(line, from);
            return m.Success ? (m.Index, m.Length) : (-1, 0);
        }

        /// <summary>Posición inmediatamente posterior al delimitador final, o -1 si no cierra en la línea.</summary>
        public int FindEnd(string line, int from)
        {
            if (End is null) return line.Length;
            int pos = from;
            while (pos <= line.Length)
            {
                var m = End.Match(line, pos);
                if (!m.Success) return StopAtEol ? line.Length : -1;
                if (Escape is { } esc && IsEscaped(line, m.Index, esc))
                {
                    pos = m.Index + 1;
                    continue;
                }
                return m.Index + m.Length;
            }
            return -1;
        }

        private static bool IsEscaped(string line, int index, char escape)
        {
            int count = 0;
            for (int i = index - 1; i >= 0 && line[i] == escape; i--) count++;
            return count % 2 == 1;
        }
    }
}
