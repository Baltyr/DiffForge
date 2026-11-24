using DiffForge.Core.Compare;
using DiffForge.Core.Documents;
using DiffForge.Core.Formats;
using DiffForge.Core.Reports;

namespace DiffForge.Core.Tests;

public class TextComparisonTests
{
    private static readonly List<FileFormat> Formats = BuiltInFormats.Create();
    private static readonly FileFormat Plain = Formats.First(f => f.Name == BuiltInFormats.PlainTextName);
    private static readonly FileFormat CSharp = Formats.First(f => f.Name == "C#");

    private static TextFile File(params string[] lines) =>
        TextFile.Empty.WithLines(lines.Select(l => new TextLine(l, LineEnding.CrLf)).ToArray());

    private static ComparisonResult Run(TextFile l, TextFile r, CompareSettings? s = null, FileFormat? f = null) =>
        TextComparer.Compare(l, r, s ?? new CompareSettings(), f ?? Plain, f ?? Plain);

    [Fact]
    public void ChangedLine_IsImportantChange()
    {
        var res = Run(File("a", "b", "c"), File("a", "X", "c"));
        Assert.Equal([RowKind.Same, RowKind.Changed, RowKind.Same], res.Rows.Select(r => r.Kind));
        Assert.True(res.Rows[1].Important);
        Assert.Equal(1, res.Stats.ImportantSections);
    }

    [Fact]
    public void WhitespaceChange_IsUnimportantByDefault()
    {
        var res = Run(File("  a  b"), File("a b"));
        var row = Assert.Single(res.Rows);
        Assert.Equal(RowKind.Changed, row.Kind);
        Assert.False(row.Important);
    }

    [Fact]
    public void WhitespaceChange_IsImportant_WhenConfigured()
    {
        var s = new CompareSettings { LeadingWhitespaceImportant = true };
        Assert.True(Assert.Single(Run(File("  a"), File("a"), s).Rows).Important);
    }

    [Fact]
    public void CaseChange_DependsOnCaseSetting()
    {
        Assert.True(Assert.Single(Run(File("Hola"), File("hola")).Rows).Important);
        var s = new CompareSettings { CaseSensitive = false };
        Assert.False(Assert.Single(Run(File("Hola"), File("hola"), s).Rows).Important);
    }

    [Fact]
    public void CommentChange_IsUnimportant_WhenElementUnchecked()
    {
        var s = new CompareSettings { ElementImportance = { [BuiltInFormats.Comment] = false } };
        var res = Run(File("x = 1; // viejo"), File("x = 1; // nuevo"), s, CSharp);
        Assert.False(Assert.Single(res.Rows).Important);
    }

    [Fact]
    public void UnimportantPattern_SuppressesDifference()
    {
        var s = new CompareSettings { UnimportantText = { new UnimportantPattern { Pattern = @"\d{4}-\d{2}-\d{2}" } } };
        var res = Run(File("Generado 2024-01-01"), File("Generado 2026-11-23"), s);
        Assert.False(Assert.Single(res.Rows).Important);
    }

    [Fact]
    public void Replacement_MakesRenameUnimportant()
    {
        var s = new CompareSettings { Replacements = { new Replacement { Find = "oldName", ReplaceWith = "newName", WholeWords = true } } };
        var res = Run(File("var x = oldName + 1;"), File("var x = newName + 1;"), s);
        var row = Assert.Single(res.Rows);
        Assert.False(row.Important);
        Assert.All(res.CompareRow(row).Left, sp => Assert.Equal(SpanKind.Replacement, sp.Kind));
    }

    [Fact]
    public void BlankOrphanLine_IsUnimportant_UnlessConfigured()
    {
        var res = Run(File("a", "b"), File("a", "", "b"));
        Assert.False(res.Rows.Single(r => r.Kind == RowKind.RightOnly).Important);

        var s = new CompareSettings { OrphanLinesAlwaysImportant = true };
        Assert.True(Run(File("a", "b"), File("a", "", "b"), s).Rows.Single(r => r.Kind == RowKind.RightOnly).Important);
    }

    [Fact]
    public void LineEndings_IgnoredUnlessCompared()
    {
        var l = TextFile.Empty.WithLines([new TextLine("a", LineEnding.CrLf)]);
        var r = TextFile.Empty.WithLines([new TextLine("a", LineEnding.Lf)]);
        Assert.Equal(RowKind.Same, Assert.Single(Run(l, r).Rows).Kind);
        Assert.True(Assert.Single(Run(l, r, new CompareSettings { CompareLineEndings = true }).Rows).Important);
    }

    [Theory]
    [InlineData(AlignmentMethod.Standard)]
    [InlineData(AlignmentMethod.Myers)]
    [InlineData(AlignmentMethod.Patience)]
    public void AllAlignments_CoverEveryLineOnce(AlignmentMethod method)
    {
        var l = File("using A;", "class X {", "  void F() {}", "  int y;", "}");
        var r = File("using A;", "using B;", "class X {", "  int y;", "  void G() {}", "}");
        var res = Run(l, r, new CompareSettings { Alignment = method });
        Assert.Equal(Enumerable.Range(0, 5), res.Rows.Where(x => x.Left >= 0).Select(x => x.Left));
        Assert.Equal(Enumerable.Range(0, 6), res.Rows.Where(x => x.Right >= 0).Select(x => x.Right));
    }

    [Fact]
    public void Unaligned_PairsByPosition()
    {
        var res = Run(File("a", "b"), File("b", "a", "c"), new CompareSettings { Alignment = AlignmentMethod.Unaligned });
        Assert.Equal([(0, 0), (1, 1), (-1, 2)], res.Rows.Select(r => (r.Left, r.Right)));
    }

    [Fact]
    public void NeverAlignDifferences_ProducesOrphansOnly()
    {
        var res = Run(File("a", "b"), File("a", "c"), new CompareSettings { NeverAlignDifferences = true });
        Assert.DoesNotContain(res.Rows, r => r.Kind == RowKind.Changed);
    }

    [Fact]
    public void ManualAlignment_ForcesPair()
    {
        var s = new CompareSettings { ManualAlignments = { (2, 0) } };
        var res = Run(File("a", "b", "c"), File("x", "a"), s);
        Assert.Contains(res.Rows, r => r.Left == 2 && r.Right == 0);
    }

    [Fact]
    public void IsolatedLine_IsNeverPaired()
    {
        var s = new CompareSettings { IsolatedLeft = { 0 } };
        var res = Run(File("a"), File("a"), s);
        Assert.Equal([RowKind.LeftOnly, RowKind.RightOnly], res.Rows.Select(r => r.Kind));
    }

    [Fact]
    public void IgnoredLine_IsUnimportant()
    {
        var s = new CompareSettings { IgnoredLeft = { 0 } };
        Assert.False(Assert.Single(Run(File("a"), File("b"), s).Rows).Important);
    }

    [Fact]
    public void IntraLine_MarksOnlyChangedWord()
    {
        var res = Run(File("var total = 10;"), File("var total = 20;"));
        var intra = res.CompareRow(res.Rows[0]);
        Assert.Equal(new DiffSpan(12, 1, SpanKind.Important), Assert.Single(intra.Left));
        Assert.Equal(new DiffSpan(12, 1, SpanKind.Important), Assert.Single(intra.Right));
    }

    [Fact]
    public void LinesAreIndependent_SplitsSections()
    {
        var format = Formats.First(f => f.Name == "INI / Config");
        var res = Run(File("a=1", "b=1"), File("a=2", "b=2"), f: format);
        Assert.Equal(2, res.Sections.Count);
    }

    [Fact]
    public void UnifiedPatch_HasHunkHeader()
    {
        var res = Run(File("a", "b", "c"), File("a", "X", "c"));
        var patch = TextReport.Generate(res, new ReportOptions(ReportLayout.Patch, ReportFilter.Context, 1, false, "l.txt", "r.txt"));
        Assert.Contains("@@ -1,3 +1,3 @@", patch);
        Assert.Contains("-b", patch);
        Assert.Contains("+X", patch);
    }
}
