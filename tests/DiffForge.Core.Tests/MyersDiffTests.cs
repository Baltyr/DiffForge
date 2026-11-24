using DiffForge.Core.Diff;

namespace DiffForge.Core.Tests;

public class MyersDiffTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("abc", "")]
    [InlineData("", "abc")]
    [InlineData("abc", "abc")]
    [InlineData("ABCABBA", "CBABAC")]
    [InlineData("kitten", "sitting")]
    public void Script_RebuildsBothSides(string a, string b)
    {
        AssertValid(a.ToCharArray(), b.ToCharArray(), MyersDiff.Compute(a.ToCharArray(), b.ToCharArray()));
    }

    [Fact]
    public void ClassicExample_HasMinimalEditDistance()
    {
        var ops = MyersDiff.Compute("ABCABBA".ToCharArray(), "CBABAC".ToCharArray());
        Assert.Equal(5, ops.Count(o => o.Kind != EditKind.Equal));
    }

    [Fact]
    public void RandomInputs_AreValidAndMinimal()
    {
        var rng = new Random(42);
        for (int iter = 0; iter < 300; iter++)
        {
            string a = RandomString(rng, rng.Next(0, 40));
            string b = RandomString(rng, rng.Next(0, 40));
            var ops = MyersDiff.Compute(a.ToCharArray(), b.ToCharArray());
            AssertValid(a.ToCharArray(), b.ToCharArray(), ops);
            Assert.Equal(a.Length + b.Length - 2 * Lcs(a, b), ops.Count(o => o.Kind != EditKind.Equal));
        }
    }

    [Fact]
    public void AnchoredDiffs_ProduceValidScripts()
    {
        var rng = new Random(7);
        for (int iter = 0; iter < 300; iter++)
        {
            var a = Enumerable.Range(0, rng.Next(0, 60)).Select(_ => rng.Next(0, 8)).ToArray();
            var b = Enumerable.Range(0, rng.Next(0, 60)).Select(_ => rng.Next(0, 8)).ToArray();
            AssertValid(a, b, AnchoredDiff.Patience(a, b));
            AssertValid(a, b, AnchoredDiff.Histogram(a, b));
        }
    }

    [Fact]
    public void LargeVeryDifferentInputs_FinishWithLinearMemory()
    {
        var rng = new Random(1);
        var a = Enumerable.Range(0, 20_000).Select(_ => rng.Next(0, 1000)).ToArray();
        var b = Enumerable.Range(0, 20_000).Select(_ => rng.Next(0, 1000)).ToArray();
        var ops = MyersDiff.Compute(a, b);
        AssertValid(a, b, ops);
    }

    private static void AssertValid<T>(IReadOnlyList<T> a, IReadOnlyList<T> b, IReadOnlyList<EditOp> ops)
    {
        var left = ops.Where(o => o.Kind != EditKind.Insert).Select(o => o.LeftIndex).ToArray();
        var right = ops.Where(o => o.Kind != EditKind.Delete).Select(o => o.RightIndex).ToArray();
        Assert.Equal(Enumerable.Range(0, a.Count), left);
        Assert.Equal(Enumerable.Range(0, b.Count), right);
        Assert.All(ops.Where(o => o.Kind == EditKind.Equal), o => Assert.Equal(a[o.LeftIndex], b[o.RightIndex]));
    }

    private static string RandomString(Random rng, int length) =>
        new(Enumerable.Range(0, length).Select(_ => (char)('a' + rng.Next(0, 4))).ToArray());

    private static int Lcs(string a, string b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                dp[i, j] = a[i - 1] == b[j - 1] ? dp[i - 1, j - 1] + 1 : Math.Max(dp[i - 1, j], dp[i, j - 1]);
        return dp[a.Length, b.Length];
    }
}
