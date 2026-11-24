namespace DiffForge.Core.Diff;

public enum EditKind { Equal, Delete, Insert }

/// <summary>Una operación de edición. LeftIndex/RightIndex valen -1 cuando no aplican.</summary>
public readonly record struct EditOp(EditKind Kind, int LeftIndex, int RightIndex)
{
    public static EditOp Equal(int l, int r) => new(EditKind.Equal, l, r);
    public static EditOp Delete(int l) => new(EditKind.Delete, l, -1);
    public static EditOp Insert(int r) => new(EditKind.Insert, -1, r);
}

/// <summary>
/// Myers O(ND) en espacio lineal: busca la "serpiente media" desde ambos extremos y divide el
/// problema en dos (divide y vencerás). Memoria O(N+M) en vez de O((N+M)·D) del backtracking clásico.
/// </summary>
public static class MyersDiff
{
    public static List<EditOp> Compute<T>(IReadOnlyList<T> left, IReadOnlyList<T> right, IEqualityComparer<T>? comparer = null)
    {
        var ctx = new Context<T>(left, right, comparer ?? EqualityComparer<T>.Default);
        ctx.Diff(0, left.Count, 0, right.Count);
        return ctx.Ops;
    }

    /// <summary>Diff de un sub-rango; los índices devueltos son absolutos.</summary>
    public static void ComputeRange<T>(IReadOnlyList<T> left, int aLo, int aHi, IReadOnlyList<T> right, int bLo, int bHi,
        IEqualityComparer<T> comparer, List<EditOp> output)
    {
        var ctx = new Context<T>(left, right, comparer, output);
        ctx.Diff(aLo, aHi, bLo, bHi);
    }

    private sealed class Context<T>(IReadOnlyList<T> a, IReadOnlyList<T> b, IEqualityComparer<T> eq, List<EditOp>? output = null)
    {
        private int[] _vf = new int[16];
        private int[] _vb = new int[16];

        public List<EditOp> Ops { get; } = output ?? new List<EditOp>();

        public void Diff(int aLo, int aHi, int bLo, int bHi)
        {
            while (aLo < aHi && bLo < bHi && eq.Equals(a[aLo], b[bLo]))
            {
                Ops.Add(EditOp.Equal(aLo, bLo));
                aLo++; bLo++;
            }

            int suffix = 0;
            while (aLo < aHi - suffix && bLo < bHi - suffix && eq.Equals(a[aHi - 1 - suffix], b[bHi - 1 - suffix])) suffix++;
            int aEnd = aHi - suffix, bEnd = bHi - suffix;

            if (aLo == aEnd)
            {
                for (int j = bLo; j < bEnd; j++) Ops.Add(EditOp.Insert(j));
            }
            else if (bLo == bEnd)
            {
                for (int i = aLo; i < aEnd; i++) Ops.Add(EditOp.Delete(i));
            }
            else
            {
                var (xs, ys, xe, ye) = MiddleSnake(aLo, aEnd, bLo, bEnd);
                Diff(aLo, xs, bLo, ys);
                for (int i = xs, j = ys; i < xe; i++, j++) Ops.Add(EditOp.Equal(i, j));
                Diff(xe, aEnd, ye, bEnd);
            }

            for (int s = 0; s < suffix; s++) Ops.Add(EditOp.Equal(aEnd + s, bEnd + s));
        }

        private (int, int, int, int) MiddleSnake(int aLo, int aHi, int bLo, int bHi)
        {
            int n = aHi - aLo, m = bHi - bLo, delta = n - m;
            bool odd = (delta & 1) != 0;
            int max = (n + m + 1) / 2;
            int off = max + 1, size = 2 * max + 3;
            if (_vf.Length < size) { _vf = new int[size]; _vb = new int[size]; }
            var vf = _vf; var vb = _vb;
            vf[off + 1] = 0; vb[off + 1] = 0;

            for (int d = 0; d <= max; d++)
            {
                for (int k = -d; k <= d; k += 2)
                {
                    int x = k == -d || (k != d && vf[off + k - 1] < vf[off + k + 1]) ? vf[off + k + 1] : vf[off + k - 1] + 1;
                    int y = x - k, xs = x, ys = y;
                    while (x < n && y < m && eq.Equals(a[aLo + x], b[bLo + y])) { x++; y++; }
                    vf[off + k] = x;

                    int kr = delta - k;
                    if (odd && kr >= -(d - 1) && kr <= d - 1 && vf[off + k] + vb[off + kr] >= n)
                        return (aLo + xs, bLo + ys, aLo + x, bLo + y);
                }

                for (int k = -d; k <= d; k += 2)
                {
                    int x = k == -d || (k != d && vb[off + k - 1] < vb[off + k + 1]) ? vb[off + k + 1] : vb[off + k - 1] + 1;
                    int y = x - k, xs = x, ys = y;
                    while (x < n && y < m && eq.Equals(a[aHi - 1 - x], b[bHi - 1 - y])) { x++; y++; }
                    vb[off + k] = x;

                    int kf = delta - k;
                    if (!odd && kf >= -d && kf <= d && vb[off + k] + vf[off + kf] >= n)
                        return (aHi - x, bHi - y, aHi - xs, bHi - ys);
                }
            }

            throw new InvalidOperationException("Myers no encontró la serpiente media; no debería ocurrir.");
        }
    }
}
