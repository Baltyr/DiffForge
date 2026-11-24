using System.Diagnostics;
using DiffForge.Core.Diff;
using DiffForge.Core.Documents;
using DiffForge.Core.Formats;

namespace DiffForge.Core.Compare;

public enum RowKind { Same, Changed, LeftOnly, RightOnly }

/// <summary>Fila alineada. Left/Right son índices base 0 de línea, o -1 si ese lado tiene un hueco.</summary>
public readonly record struct DiffRow(RowKind Kind, int Left, int Right, bool Important)
{
    public bool IsDifference => Kind != RowKind.Same;
}

/// <summary>Rango de filas contiguas con diferencias. End es exclusivo.</summary>
public readonly record struct DiffSection(int Start, int End, bool Important);

public sealed record ComparisonStats(
    int ImportantSections,
    int UnimportantSections,
    int SameLines,
    int ChangedLines,
    int UnimportantLines,
    int LeftOnlyLines,
    int RightOnlyLines,
    TimeSpan Elapsed);

public sealed class ComparisonResult
{
    public required IReadOnlyList<DiffRow> Rows { get; init; }
    public required LineInfo[] Left { get; init; }
    public required LineInfo[] Right { get; init; }
    public required LineAnalyzer LeftAnalyzer { get; init; }
    public required LineAnalyzer RightAnalyzer { get; init; }
    public required IReadOnlyList<DiffSection> Sections { get; init; }
    public required ComparisonStats Stats { get; init; }
    public required bool ColumnBased { get; init; }

    public IntraLineResult CompareRow(DiffRow row)
    {
        var leftLine = row.Left >= 0 ? Left[row.Left] : null;
        var rightLine = row.Right >= 0 ? Right[row.Right] : null;
        return IntraLineComparer.Compare(
            leftLine?.Text ?? string.Empty, leftLine is null ? null : LeftAnalyzer.Map(leftLine),
            rightLine?.Text ?? string.Empty, rightLine is null ? null : RightAnalyzer.Map(rightLine),
            ColumnBased);
    }
}

/// <summary>Motor de Text Compare: analiza ambos lados, alinea líneas y clasifica diferencias.</summary>
public static class TextComparer
{
    public static ComparisonResult Compare(
        TextFile left,
        TextFile right,
        CompareSettings settings,
        FileFormat leftFormat,
        FileFormat rightFormat,
        Func<string, string, double>? similarity = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        similarity ??= LineSimilarity.Score;

        var leftAnalyzer = new LineAnalyzer(settings, leftFormat, Side.Left);
        var rightAnalyzer = new LineAnalyzer(settings, rightFormat, Side.Right);
        var l = leftAnalyzer.Analyze(left.Lines);
        var r = rightAnalyzer.Analyze(right.Lines);
        ct.ThrowIfCancellationRequested();

        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        int Id(string key) => ids.TryGetValue(key, out var id) ? id : ids[key] = ids.Count;
        var lk = l.Select(x => Id(x.Key)).ToArray();
        var rk = r.Select(x => Id(x.Key)).ToArray();

        var rows = new List<DiffRow>(Math.Max(l.Length, r.Length));
        var builder = new RowBuilder(l, r, settings, similarity, rows);

        int pl = 0, pr = 0;
        foreach (var (al, ar) in ValidAnchors(settings.ManualAlignments, l.Length, r.Length))
        {
            AlignSegment(lk, pl, al, rk, pr, ar, settings, builder);
            builder.AddPair(al, ar);
            pl = al + 1; pr = ar + 1;
            ct.ThrowIfCancellationRequested();
        }
        AlignSegment(lk, pl, l.Length, rk, pr, r.Length, settings, builder);

        var sections = BuildSections(rows, leftFormat.LinesAreIndependent || rightFormat.LinesAreIndependent);
        sw.Stop();

        return new ComparisonResult
        {
            Rows = rows,
            Left = l,
            Right = r,
            LeftAnalyzer = leftAnalyzer,
            RightAnalyzer = rightAnalyzer,
            Sections = sections,
            Stats = BuildStats(rows, sections, sw.Elapsed),
            ColumnBased = leftFormat.ColumnBasedData || rightFormat.ColumnBasedData,
        };
    }

    private static IEnumerable<(int, int)> ValidAnchors(List<(int Left, int Right)> anchors, int nl, int nr)
    {
        int lastL = -1, lastR = -1;
        foreach (var (al, ar) in anchors.OrderBy(a => a.Left))
        {
            if (al > lastL && ar > lastR && al < nl && ar < nr)
            {
                yield return (al, ar);
                lastL = al; lastR = ar;
            }
        }
    }

    private static void AlignSegment(int[] lk, int lLo, int lHi, int[] rk, int rLo, int rHi, CompareSettings settings, RowBuilder builder)
    {
        if (settings.Alignment == AlignmentMethod.Unaligned)
        {
            int count = Math.Max(lHi - lLo, rHi - rLo);
            for (int i = 0; i < count; i++)
            {
                int li = lLo + i < lHi ? lLo + i : -1;
                int ri = rLo + i < rHi ? rLo + i : -1;
                if (li >= 0 && ri >= 0) builder.AddPair(li, ri);
                else if (li >= 0) builder.AddOrphan(li, -1);
                else builder.AddOrphan(-1, ri);
            }
            return;
        }

        var segL = lk[lLo..lHi];
        var segR = rk[rLo..rHi];
        var ops = settings.Alignment switch
        {
            AlignmentMethod.Myers => MyersDiff.Compute(segL, segR),
            AlignmentMethod.Patience => AnchoredDiff.Patience(segL, segR),
            _ => AnchoredDiff.Histogram(segL, segR),
        };

        var deletes = new List<int>();
        var inserts = new List<int>();
        foreach (var op in ops)
        {
            switch (op.Kind)
            {
                case EditKind.Delete: deletes.Add(lLo + op.LeftIndex); break;
                case EditKind.Insert: inserts.Add(rLo + op.RightIndex); break;
                default:
                    builder.AddChangeBlock(deletes, inserts);
                    deletes.Clear(); inserts.Clear();
                    builder.AddPair(lLo + op.LeftIndex, rLo + op.RightIndex);
                    break;
            }
        }
        builder.AddChangeBlock(deletes, inserts);
    }

    private sealed class RowBuilder(
        LineInfo[] left,
        LineInfo[] right,
        CompareSettings settings,
        Func<string, string, double> similarity,
        List<DiffRow> rows)
    {
        public void AddChangeBlock(List<int> deletes, List<int> inserts)
        {
            if (deletes.Count == 0 && inserts.Count == 0) return;

            List<(int L, int R)> pairs;
            if (settings.NeverAlignDifferences || deletes.Count == 0 || inserts.Count == 0)
            {
                pairs = ClosenessMatcher.Unpaired(deletes.Count, inserts.Count);
            }
            else if (settings.UseClosenessMatching)
            {
                pairs = ClosenessMatcher.Align(
                    deletes.Select(i => left[i].Key).ToArray(),
                    inserts.Select(i => right[i].Key).ToArray(),
                    similarity,
                    settings.SkewTolerance,
                    i => !left[deletes[i]].IsIsolated,
                    j => !right[inserts[j]].IsIsolated);
            }
            else
            {
                pairs = PositionalExceptIsolated(deletes, inserts);
            }

            foreach (var (pl, pr) in pairs)
            {
                int li = pl >= 0 ? deletes[pl] : -1;
                int ri = pr >= 0 ? inserts[pr] : -1;
                if (li >= 0 && ri >= 0) AddPair(li, ri);
                else AddOrphan(li, ri);
            }
        }

        private List<(int, int)> PositionalExceptIsolated(List<int> deletes, List<int> inserts)
        {
            if (deletes.Any(i => left[i].IsIsolated) || inserts.Any(i => right[i].IsIsolated))
                return ClosenessMatcher.Unpaired(deletes.Count, inserts.Count);
            return ClosenessMatcher.Positional(deletes.Count, inserts.Count);
        }

        public void AddPair(int li, int ri)
        {
            var l = left[li];
            var r = right[ri];
            bool ignored = settings.IgnoredLeft.Contains(li) || settings.IgnoredRight.Contains(ri);

            if (l.Key == r.Key)
            {
                bool identical = l.Text == r.Text && (!settings.CompareLineEndings || l.Ending == r.Ending);
                rows.Add(new DiffRow(identical ? RowKind.Same : RowKind.Changed, li, ri, Important: false));
            }
            else
            {
                rows.Add(new DiffRow(RowKind.Changed, li, ri, Important: !ignored));
            }
        }

        public void AddOrphan(int li, int ri)
        {
            var line = li >= 0 ? left[li] : right[ri];
            bool ignored = li >= 0 ? settings.IgnoredLeft.Contains(li) : settings.IgnoredRight.Contains(ri);
            bool important = !ignored && (line.HasImportantContent || settings.OrphanLinesAlwaysImportant);
            rows.Add(new DiffRow(li >= 0 ? RowKind.LeftOnly : RowKind.RightOnly, li, ri, important));
        }
    }

    private static List<DiffSection> BuildSections(List<DiffRow> rows, bool linesAreIndependent)
    {
        var sections = new List<DiffSection>();
        int start = -1;
        bool important = false;
        for (int i = 0; i <= rows.Count; i++)
        {
            bool isDiff = i < rows.Count && rows[i].IsDifference;
            bool split = linesAreIndependent && start >= 0 && isDiff;
            if (start >= 0 && (!isDiff || split))
            {
                sections.Add(new DiffSection(start, i, important));
                start = -1;
            }
            if (isDiff && start < 0)
            {
                start = i;
                important = false;
            }
            if (isDiff) important |= rows[i].Important;
        }
        return sections;
    }

    private static ComparisonStats BuildStats(List<DiffRow> rows, List<DiffSection> sections, TimeSpan elapsed)
    {
        int same = 0, changed = 0, unimportant = 0, leftOnly = 0, rightOnly = 0;
        foreach (var row in rows)
        {
            if (row.Kind == RowKind.Same) same++;
            else if (!row.Important) unimportant++;
            else if (row.Kind == RowKind.Changed) changed++;
            else if (row.Kind == RowKind.LeftOnly) leftOnly++;
            else rightOnly++;
        }
        return new ComparisonStats(
            sections.Count(s => s.Important),
            sections.Count(s => !s.Important),
            same, changed, unimportant, leftOnly, rightOnly, elapsed);
    }
}
