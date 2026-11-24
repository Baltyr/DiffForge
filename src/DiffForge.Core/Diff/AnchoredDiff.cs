namespace DiffForge.Core.Diff;

/// <summary>
/// Diffs basados en anclas, que producen alineaciones más "humanas" que Myers puro en código:
/// <list type="bullet">
/// <item>Patience (Bram Cohen): ancla en líneas que aparecen exactamente una vez en cada lado.</item>
/// <item>Histogram (estilo git): ancla en la línea menos frecuente y extiende la coincidencia.</item>
/// </list>
/// Los tramos sin anclas se resuelven con Myers.
/// </summary>
public static class AnchoredDiff
{
    private const int MaxHistogramOccurrences = 64;

    public static List<EditOp> Patience(int[] left, int[] right) => Run(left, right, histogram: false);

    public static List<EditOp> Histogram(int[] left, int[] right) => Run(left, right, histogram: true);

    private static List<EditOp> Run(int[] a, int[] b, bool histogram)
    {
        var ops = new List<EditOp>(a.Length + b.Length);
        Recurse(a, 0, a.Length, b, 0, b.Length, histogram, ops, depth: 0);
        return ops;
    }

    private static void Recurse(int[] a, int aLo, int aHi, int[] b, int bLo, int bHi, bool histogram, List<EditOp> ops, int depth)
    {
        while (aLo < aHi && bLo < bHi && a[aLo] == b[bLo]) { ops.Add(EditOp.Equal(aLo++, bLo++)); }
        int suffix = 0;
        while (aLo < aHi - suffix && bLo < bHi - suffix && a[aHi - 1 - suffix] == b[bHi - 1 - suffix]) suffix++;
        int aEnd = aHi - suffix, bEnd = bHi - suffix;

        if (aLo == aEnd || bLo == bEnd || depth > 512)
        {
            MyersDiff.ComputeRange(a, aLo, aEnd, b, bLo, bEnd, EqualityComparer<int>.Default, ops);
        }
        else if (histogram)
        {
            HistogramStep(a, aLo, aEnd, b, bLo, bEnd, ops, depth);
        }
        else
        {
            PatienceStep(a, aLo, aEnd, b, bLo, bEnd, ops, depth);
        }

        for (int s = 0; s < suffix; s++) ops.Add(EditOp.Equal(aEnd + s, bEnd + s));
    }

    private static void PatienceStep(int[] a, int aLo, int aHi, int[] b, int bLo, int bHi, List<EditOp> ops, int depth)
    {
        var counts = new Dictionary<int, (int CountA, int PosA, int CountB, int PosB)>();
        for (int i = aLo; i < aHi; i++)
        {
            counts.TryGetValue(a[i], out var c);
            counts[a[i]] = (c.CountA + 1, i, c.CountB, c.PosB);
        }
        for (int j = bLo; j < bHi; j++)
        {
            if (!counts.TryGetValue(b[j], out var c)) continue;
            counts[b[j]] = (c.CountA, c.PosA, c.CountB + 1, j);
        }

        var candidates = counts.Values
            .Where(c => c.CountA == 1 && c.CountB == 1)
            .Select(c => (A: c.PosA, B: c.PosB))
            .OrderBy(c => c.A)
            .ToList();

        var anchors = LongestIncreasingByB(candidates);
        if (anchors.Count == 0)
        {
            MyersDiff.ComputeRange(a, aLo, aHi, b, bLo, bHi, EqualityComparer<int>.Default, ops);
            return;
        }

        int pa = aLo, pb = bLo;
        foreach (var (ai, bi) in anchors)
        {
            Recurse(a, pa, ai, b, pb, bi, histogram: false, ops, depth + 1);
            ops.Add(EditOp.Equal(ai, bi));
            pa = ai + 1; pb = bi + 1;
        }
        Recurse(a, pa, aHi, b, pb, bHi, histogram: false, ops, depth + 1);
    }

    /// <summary>Subsecuencia creciente más larga en B (patience sorting, O(k log k)).</summary>
    private static List<(int A, int B)> LongestIncreasingByB(List<(int A, int B)> items)
    {
        if (items.Count == 0) return [];
        var tails = new List<int>();
        var prev = new int[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            int lo = 0, hi = tails.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (items[tails[mid]].B < items[i].B) lo = mid + 1; else hi = mid;
            }
            prev[i] = lo > 0 ? tails[lo - 1] : -1;
            if (lo == tails.Count) tails.Add(i); else tails[lo] = i;
        }

        var result = new List<(int, int)>(tails.Count);
        for (int i = tails[^1]; i >= 0; i = prev[i]) result.Add(items[i]);
        result.Reverse();
        return result;
    }

    private static void HistogramStep(int[] a, int aLo, int aHi, int[] b, int bLo, int bHi, List<EditOp> ops, int depth)
    {
        var occurrencesA = new Dictionary<int, List<int>>();
        for (int i = aLo; i < aHi; i++)
        {
            if (!occurrencesA.TryGetValue(a[i], out var list)) occurrencesA[a[i]] = list = [];
            list.Add(i);
        }

        int bestLen = 0, bestA = -1, bestB = -1, bestCount = int.MaxValue;
        for (int j = bLo; j < bHi; j++)
        {
            if (!occurrencesA.TryGetValue(b[j], out var positions)) continue;
            if (positions.Count > MaxHistogramOccurrences || positions.Count > bestCount) continue;

            foreach (int i in positions)
            {
                int s = 0;
                while (i - s - 1 >= aLo && j - s - 1 >= bLo && a[i - s - 1] == b[j - s - 1]) s++;
                int e = 1;
                while (i + e < aHi && j + e < bHi && a[i + e] == b[j + e]) e++;
                int len = s + e;
                if (positions.Count < bestCount || len > bestLen)
                {
                    bestCount = positions.Count; bestLen = len; bestA = i - s; bestB = j - s;
                }
            }
        }

        if (bestA < 0)
        {
            MyersDiff.ComputeRange(a, aLo, aHi, b, bLo, bHi, EqualityComparer<int>.Default, ops);
            return;
        }

        Recurse(a, aLo, bestA, b, bLo, bestB, histogram: true, ops, depth + 1);
        for (int k = 0; k < bestLen; k++) ops.Add(EditOp.Equal(bestA + k, bestB + k));
        Recurse(a, bestA + bestLen, aHi, b, bestB + bestLen, bHi, histogram: true, ops, depth + 1);
    }
}
