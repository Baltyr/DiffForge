using DiffForge.Core.Compare;
using DiffForge.Core.Documents;
using DiffForge.Core.Formats;

namespace DiffForge.Core.Tests;

/// <summary>Estos tests dependen de LineSimilarity.Score (TODO de Allan).</summary>
public class ClosenessMatchingTests
{
    [Theory]
    [InlineData("var total = 10;", "var total = 10;", 1.0, 1.0)]
    [InlineData("var total = 10;", "var total = 20;", 0.75, 1.0)]
    [InlineData("SELECT * FROM Polizas", "SELECT * FROM Polizas WHERE Id = 1", 0.5, 0.95)]
    [InlineData("b", "var total = 20;", 0.0, 0.3)]
    [InlineData("return x;", "}", 0.0, 0.3)]
    [InlineData("", "", 1.0, 1.0)]
    public void Score_IsWithinExpectedRange(string a, string b, double min, double max)
    {
        double score = LineSimilarity.Score(a, b);
        Assert.InRange(score, min, max);
        Assert.Equal(score, LineSimilarity.Score(b, a), 3);
    }

    [Fact]
    public void ChangedLine_PairsWithMostSimilarLine()
    {
        // Caso observado en la primera versión: una línea sin relación quedaba emparejada con
        // "var total = 20;" solo por estar en la misma posición.
        var l = Lines("a", "var total = 10;", "b", "d");
        var r = Lines("a", "var total = 20;", "d", "e");
        var formats = BuiltInFormats.Create();
        var plain = formats.First(f => f.Name == BuiltInFormats.PlainTextName);

        var res = TextComparer.Compare(l, r, new CompareSettings(), plain, plain);

        Assert.Contains(res.Rows, row => row.Kind == RowKind.Changed && row.Left == 1 && row.Right == 1);
        Assert.Contains(res.Rows, row => row.Kind == RowKind.LeftOnly && row.Left == 2);
    }

    private static TextFile Lines(params string[] lines) =>
        TextFile.Empty.WithLines(lines.Select(x => new TextLine(x, LineEnding.Lf)).ToArray());
}
