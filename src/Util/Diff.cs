using System;
using System.Collections.Generic;

namespace TG3ModLoader.Util
{
    /// <summary>
    /// Longest-common-subsequence on integer sequences. Uses prefix/suffix trimming, an exact DP for
    /// moderate sizes and patience-style unique anchors (with a greedy fallback) for very large inputs.
    /// </summary>
    public static class Diff
    {
        const long DpCellLimit = 6_000_000;

        public static List<(int A, int B)> Lcs(int[] a, int[] b)
        {
            var res = new List<(int, int)>();
            Rec(a, 0, a.Length, b, 0, b.Length, res, 0);
            return res;
        }

        /// <summary>Maps arbitrary keys to dense integers so they can be diffed.</summary>
        public static (int[], int[]) Intern<T>(IList<T> a, IList<T> b, IEqualityComparer<T> cmp = null)
        {
            var dict = new Dictionary<T, int>(cmp ?? EqualityComparer<T>.Default);
            int[] Map(IList<T> src)
            {
                var r = new int[src.Count];
                for (int i = 0; i < src.Count; i++)
                {
                    if (!dict.TryGetValue(src[i], out int id)) { id = dict.Count; dict[src[i]] = id; }
                    r[i] = id;
                }
                return r;
            }
            return (Map(a), Map(b));
        }

        static void Rec(int[] a, int a0, int a1, int[] b, int b0, int b1, List<(int, int)> res, int depth)
        {
            while (a0 < a1 && b0 < b1 && a[a0] == b[b0]) { res.Add((a0, b0)); a0++; b0++; }
            int sa = a1, sb = b1;
            while (sa > a0 && sb > b0 && a[sa - 1] == b[sb - 1]) { sa--; sb--; }

            if (a0 < sa && b0 < sb)
            {
                long cells = (long)(sa - a0) * (sb - b0);
                if (cells <= DpCellLimit) Dp(a, a0, sa, b, b0, sb, res);
                else
                {
                    var anchors = depth < 64 ? UniqueAnchors(a, a0, sa, b, b0, sb) : null;
                    if (anchors == null || anchors.Count == 0) Greedy(a, a0, sa, b, b0, sb, res);
                    else
                    {
                        int pa = a0, pb = b0;
                        foreach (var (x, y) in anchors)
                        {
                            Rec(a, pa, x, b, pb, y, res, depth + 1);
                            res.Add((x, y));
                            pa = x + 1; pb = y + 1;
                        }
                        Rec(a, pa, sa, b, pb, sb, res, depth + 1);
                    }
                }
            }
            for (int k = 0; k < a1 - sa; k++) res.Add((sa + k, sb + k));
        }

        static void Dp(int[] a, int a0, int a1, int[] b, int b0, int b1, List<(int, int)> res)
        {
            int n = a1 - a0, m = b1 - b0;
            var len = new int[(n + 1) * (m + 1)];
            int W = m + 1;
            for (int i = n - 1; i >= 0; i--)
            {
                int ai = a[a0 + i];
                int row = i * W, next = (i + 1) * W;
                for (int j = m - 1; j >= 0; j--)
                {
                    if (ai == b[b0 + j]) len[row + j] = len[next + j + 1] + 1;
                    else
                    {
                        int x = len[next + j], y = len[row + j + 1];
                        len[row + j] = x >= y ? x : y;
                    }
                }
            }
            int ii = 0, jj = 0;
            while (ii < n && jj < m)
            {
                if (a[a0 + ii] == b[b0 + jj]) { res.Add((a0 + ii, b0 + jj)); ii++; jj++; }
                else if (len[(ii + 1) * W + jj] >= len[ii * W + jj + 1]) ii++;
                else jj++;
            }
        }

        static List<(int, int)> UniqueAnchors(int[] a, int a0, int a1, int[] b, int b0, int b1)
        {
            var ca = new Dictionary<int, int>();
            var cb = new Dictionary<int, int>();
            var posA = new Dictionary<int, int>();
            var posB = new Dictionary<int, int>();
            for (int i = a0; i < a1; i++) { ca.TryGetValue(a[i], out int c); ca[a[i]] = c + 1; posA[a[i]] = i; }
            for (int j = b0; j < b1; j++) { cb.TryGetValue(b[j], out int c); cb[b[j]] = c + 1; posB[b[j]] = j; }
            var pairs = new List<(int, int)>();
            for (int i = a0; i < a1; i++)
            {
                int v = a[i];
                if (ca[v] == 1 && cb.TryGetValue(v, out int c) && c == 1) pairs.Add((i, posB[v]));
            }
            if (pairs.Count == 0) return pairs;
            // Longest increasing subsequence on B index (pairs are sorted by A index).
            var tails = new List<int>();
            var prev = new int[pairs.Count];
            for (int k = 0; k < pairs.Count; k++)
            {
                int y = pairs[k].Item2;
                int lo = 0, hi = tails.Count;
                while (lo < hi) { int mid = (lo + hi) / 2; if (pairs[tails[mid]].Item2 < y) lo = mid + 1; else hi = mid; }
                prev[k] = lo > 0 ? tails[lo - 1] : -1;
                if (lo == tails.Count) tails.Add(k); else tails[lo] = k;
            }
            var result = new List<(int, int)>();
            for (int k = tails[tails.Count - 1]; k >= 0; k = prev[k]) result.Add(pairs[k]);
            result.Reverse();
            return result;
        }

        static void Greedy(int[] a, int a0, int a1, int[] b, int b0, int b1, List<(int, int)> res)
        {
            var positions = new Dictionary<int, List<int>>();
            for (int j = b0; j < b1; j++)
            {
                if (!positions.TryGetValue(b[j], out var l)) positions[b[j]] = l = new List<int>();
                l.Add(j);
            }
            int pb = b0;
            for (int i = a0; i < a1 && pb < b1; i++)
            {
                if (!positions.TryGetValue(a[i], out var l)) continue;
                int idx = l.BinarySearch(pb);
                if (idx < 0) idx = ~idx;
                if (idx < l.Count) { res.Add((i, l[idx])); pb = l[idx] + 1; }
            }
        }
    }
}
