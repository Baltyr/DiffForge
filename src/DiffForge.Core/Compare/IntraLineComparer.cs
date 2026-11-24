using DiffForge.Core.Diff;

namespace DiffForge.Core.Compare;

public enum SpanKind { Important, Unimportant, Replacement }

/// <summary>Tramo con diferencia dentro de una línea.</summary>
public readonly record struct DiffSpan(int Start, int Length, SpanKind Kind);

/// <summary>Diferencias de una fila y cómo se corresponden los caracteres (para la vista de alineación).</summary>
public sealed record IntraLineResult(
    IReadOnlyList<DiffSpan> Left,
    IReadOnlyList<DiffSpan> Right,
    IReadOnlyList<EditOp> CharAlignment);

/// <summary>
/// Diff dentro de una línea en dos pasadas: primero por unidades (palabras, espacios, símbolos)
/// para que el resultado se lea natural, y luego por carácter dentro de cada tramo cambiado corto.
/// Cada carácter distinto se clasifica según su importancia (CharMap).
/// </summary>
public static class IntraLineComparer
{
    private const int MaxCharRefine = 400;

    public static IntraLineResult Compare(string left, CharMap? leftMap, string right, CharMap? rightMap, bool columnBased)
    {
        leftMap ??= Identity(left);
        rightMap ??= Identity(right);

        var ops = columnBased ? Positional(left.Length, right.Length, leftMap, rightMap) : TwoPass(leftMap, rightMap);

        var leftKinds = new SpanKind?[left.Length];
        var rightKinds = new SpanKind?[right.Length];
        foreach (var op in ops)
        {
            switch (op.Kind)
            {
                case EditKind.Delete:
                    leftKinds[op.LeftIndex] = Classify(leftMap, op.LeftIndex);
                    break;
                case EditKind.Insert:
                    rightKinds[op.RightIndex] = Classify(rightMap, op.RightIndex);
                    break;
                default:
                    // Iguales tras normalizar pero distintos en crudo (mayúsculas, reemplazos).
                    if (left[op.LeftIndex] != right[op.RightIndex])
                    {
                        leftKinds[op.LeftIndex] = SpanKind.Unimportant;
                        rightKinds[op.RightIndex] = SpanKind.Unimportant;
                    }
                    break;
            }
        }

        return new IntraLineResult(ToSpans(leftKinds), ToSpans(rightKinds), ops);
    }

    private static SpanKind Classify(CharMap map, int i) =>
        map.Replaced[i] ? SpanKind.Replacement : map.Important[i] ? SpanKind.Important : SpanKind.Unimportant;

    private static List<EditOp> Positional(int n, int m, CharMap l, CharMap r)
    {
        var ops = new List<EditOp>(Math.Max(n, m) * 2);
        for (int i = 0; i < Math.Max(n, m); i++)
        {
            if (i < n && i < m && l.Folded[i] == r.Folded[i]) ops.Add(EditOp.Equal(i, i));
            else
            {
                if (i < n) ops.Add(EditOp.Delete(i));
                if (i < m) ops.Add(EditOp.Insert(i));
            }
        }
        return ops;
    }

    private static List<EditOp> TwoPass(CharMap l, CharMap r)
    {
        var lu = Units(l.Folded);
        var ru = Units(r.Folded);
        var unitOps = MyersDiff.Compute(lu.Select(u => u.Text).ToArray(), ru.Select(u => u.Text).ToArray(), StringComparer.Ordinal);

        var ops = new List<EditOp>(l.Folded.Length + r.Folded.Length);
        var delUnits = new List<int>();
        var insUnits = new List<int>();

        void Flush()
        {
            if (delUnits.Count == 0 && insUnits.Count == 0) return;
            int ls = delUnits.Count > 0 ? lu[delUnits[0]].Start : 0;
            int le = delUnits.Count > 0 ? lu[delUnits[^1]].End : 0;
            int rs = insUnits.Count > 0 ? ru[insUnits[0]].Start : 0;
            int re = insUnits.Count > 0 ? ru[insUnits[^1]].End : 0;

            if (delUnits.Count > 0 && insUnits.Count > 0 && (le - ls) + (re - rs) <= MaxCharRefine)
            {
                MyersDiff.ComputeRange(l.Folded, ls, le, r.Folded, rs, re, EqualityComparer<char>.Default, ops);
            }
            else
            {
                for (int i = ls; i < le; i++) ops.Add(EditOp.Delete(i));
                for (int j = rs; j < re; j++) ops.Add(EditOp.Insert(j));
            }
            delUnits.Clear(); insUnits.Clear();
        }

        foreach (var op in unitOps)
        {
            switch (op.Kind)
            {
                case EditKind.Delete: delUnits.Add(op.LeftIndex); break;
                case EditKind.Insert: insUnits.Add(op.RightIndex); break;
                default:
                    Flush();
                    var a = lu[op.LeftIndex];
                    var b = ru[op.RightIndex];
                    for (int k = 0; k < a.End - a.Start; k++) ops.Add(EditOp.Equal(a.Start + k, b.Start + k));
                    break;
            }
        }
        Flush();
        return ops;
    }

    private readonly record struct Unit(int Start, int End, string Text);

    private static List<Unit> Units(char[] s)
    {
        var units = new List<Unit>();
        int i = 0;
        while (i < s.Length)
        {
            int start = i;
            if (char.IsLetterOrDigit(s[i]) || s[i] == '_')
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
            else if (char.IsWhiteSpace(s[i]))
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            else
                i++;
            units.Add(new Unit(start, i, new string(s, start, i - start)));
        }
        return units;
    }

    private static List<DiffSpan> ToSpans(SpanKind?[] kinds)
    {
        var spans = new List<DiffSpan>();
        int i = 0;
        while (i < kinds.Length)
        {
            if (kinds[i] is not { } kind) { i++; continue; }
            int start = i;
            while (i < kinds.Length && kinds[i] == kind) i++;
            spans.Add(new DiffSpan(start, i - start, kind));
        }
        return spans;
    }

    private static CharMap Identity(string text)
    {
        var map = new CharMap(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            map.Folded[i] = text[i];
            map.Important[i] = true;
        }
        return map;
    }
}
