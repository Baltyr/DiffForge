namespace DiffForge.Core.Compare;

/// <summary>
/// Dentro de un bloque de cambios (N líneas borradas, M insertadas) decide qué pares mostrar
/// lado a lado. Programación dinámica que maximiza la suma de similitudes respetando el orden,
/// limitada a una banda de <c>skew</c> líneas para acotar el costo.
/// </summary>
public static class ClosenessMatcher
{
    public const double Threshold = 0.5;
    private const long MaxCells = 1_000_000;

    /// <returns>Secuencia ordenada; -1 en un lado indica línea sin pareja.</returns>
    public static List<(int Left, int Right)> Align(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right,
        Func<string, string, double> similarity,
        int skew,
        Func<int, bool>? leftPairable = null,
        Func<int, bool>? rightPairable = null)
    {
        int n = left.Count, m = right.Count;
        if (n == 0 || m == 0 || (long)(n + 1) * (m + 1) > MaxCells) return Positional(n, m);

        var dp = new double[n + 1, m + 1];
        var pairScore = new double[n + 1, m + 1];
        int band = Math.Max(skew, 0) + Math.Abs(n - m);

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                double best = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                pairScore[i, j] = double.NaN;
                if (Math.Abs(i - j) <= band
                    && (leftPairable?.Invoke(i - 1) ?? true)
                    && (rightPairable?.Invoke(j - 1) ?? true))
                {
                    double s = similarity(left[i - 1], right[j - 1]);
                    if (s >= Threshold)
                    {
                        pairScore[i, j] = s;
                        best = Math.Max(best, dp[i - 1, j - 1] + s);
                    }
                }
                dp[i, j] = best;
            }
        }

        var reversed = new List<(int, int)>();
        int x = n, y = m;
        while (x > 0 || y > 0)
        {
            if (x > 0 && y > 0 && !double.IsNaN(pairScore[x, y]) && dp[x, y] == dp[x - 1, y - 1] + pairScore[x, y])
            {
                reversed.Add((x - 1, y - 1));
                x--; y--;
            }
            else if (x > 0 && (y == 0 || dp[x, y] == dp[x - 1, y]))
            {
                reversed.Add((x - 1, -1));
                x--;
            }
            else
            {
                reversed.Add((-1, y - 1));
                y--;
            }
        }
        reversed.Reverse();
        return GroupOrphans(reversed);
    }

    public static List<(int Left, int Right)> Positional(int n, int m)
    {
        var result = new List<(int, int)>(Math.Max(n, m));
        int paired = Math.Min(n, m);
        for (int i = 0; i < paired; i++) result.Add((i, i));
        for (int i = paired; i < n; i++) result.Add((i, -1));
        for (int j = paired; j < m; j++) result.Add((-1, j));
        return result;
    }

    public static List<(int Left, int Right)> Unpaired(int n, int m)
    {
        var result = new List<(int, int)>(n + m);
        for (int i = 0; i < n; i++) result.Add((i, -1));
        for (int j = 0; j < m; j++) result.Add((-1, j));
        return result;
    }

    /// <summary>Entre dos pares, muestra primero las líneas solo-izquierda y luego las solo-derecha.</summary>
    private static List<(int, int)> GroupOrphans(List<(int L, int R)> seq)
    {
        var result = new List<(int, int)>(seq.Count);
        var lefts = new List<(int, int)>();
        var rights = new List<(int, int)>();
        foreach (var item in seq)
        {
            if (item.L >= 0 && item.R >= 0)
            {
                result.AddRange(lefts); result.AddRange(rights);
                lefts.Clear(); rights.Clear();
                result.Add(item);
            }
            else if (item.L >= 0) lefts.Add(item);
            else rights.Add(item);
        }
        result.AddRange(lefts); result.AddRange(rights);
        return result;
    }
}
