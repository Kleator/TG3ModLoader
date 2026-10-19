using System;
using System.Collections.Generic;
using System.Linq;
using TG3ModLoader.Script;
using TG3ModLoader.Util;

namespace TG3ModLoader.Merge
{
    /// <summary>
    /// Aligns the children of two versions of the same container.
    /// Keyed children (assignments, named blocks, functions) are matched by key, unkeyed children
    /// (array elements, includes, anonymous objects) by identity fields, content LCS and similarity.
    /// </summary>
    public static class Matcher
    {
        /// <summary>Properties that identify an object inside a list (e.g. LocDirectEntry.Key).</summary>
        static readonly string[] IdentityProps =
        {
            "Key", "Name", "ID", "Id", "ActionName", "TypeName", "Typename", "UniqueName", "InternalName",
            "Identifier", "ItemName", "ObjectName", "EventName", "TemplateName", "DialogueId", "QuestId"
        };

        /// <summary>Returns for every child of <paramref name="m"/> the index of the matching child of <paramref name="b"/>, or -1.</summary>
        public static int[] Match(Node b, Node m)
        {
            var B = b.Children;
            var M = m.Children;
            var res = new int[M.Count];
            for (int j = 0; j < res.Length; j++) res[j] = -1;
            if (B.Count == 0 || M.Count == 0) return res;

            var bIdx = new Dictionary<string, int>(StringComparer.Ordinal);
            var bMatched = new bool[B.Count];
            for (int i = 0; i < B.Count; i++) if (B[i].Key != null) bIdx[B[i].Key] = i;
            for (int j = 0; j < M.Count; j++)
                if (M[j].Key != null && bIdx.TryGetValue(M[j].Key, out int i)) { res[j] = i; bMatched[i] = true; }

            // Everything not matched by key is aligned by content. This includes keyed elements whose key is
            // unique on one side but repeated (= unkeyed list) on the other side.
            var bu = new List<int>();
            var mu = new List<int>();
            for (int i = 0; i < B.Count; i++) if (!bMatched[i]) bu.Add(i);
            for (int j = 0; j < M.Count; j++) if (res[j] < 0) mu.Add(j);
            if (bu.Count == 0 || mu.Count == 0) return res;

            var prop = FindIdentity(B, bu, M, mu);
            if (prop != null)
            {
                var d = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (int i in bu) d[IdentityOf(B[i], prop)] = i;
                foreach (int j in mu)
                    if (d.TryGetValue(IdentityOf(M[j], prop), out int i)) res[j] = i;
                return res;
            }

            var (ia, ib) = Diff.Intern(bu.Select(i => B[i].Hash).ToList(), mu.Select(j => M[j].Hash).ToList());
            var lcs = Diff.Lcs(ia, ib);
            int pa = 0, pb = 0;
            for (int k = 0; k <= lcs.Count; k++)
            {
                int x = k < lcs.Count ? lcs[k].A : bu.Count;
                int y = k < lcs.Count ? lcs[k].B : mu.Count;
                if (x > pa && y > pb) PairHunk(B, bu, pa, x, M, mu, pb, y, res);
                if (k < lcs.Count) res[mu[y]] = bu[x];
                pa = x + 1; pb = y + 1;
            }
            return res;
        }

        public static string IdentityOf(Node n, string prop) => n.HeadType + "|" + n.ChildScalar(prop);

        /// <summary>Finds an identity property that is present and unique in all unkeyed elements of both versions.</summary>
        public static string FindIdentity(List<Node> B, List<int> bu, List<Node> M, List<int> mu)
        {
            if (bu.Count + mu.Count < 3) return null;
            foreach (int i in bu) if (B[i].Kind != NodeKind.Block) return null;
            foreach (int j in mu) if (M[j].Kind != NodeKind.Block) return null;
            foreach (var p in IdentityProps)
                if (Unique(B, bu, p) && Unique(M, mu, p)) return p;
            return null;
        }

        static bool Unique(List<Node> nodes, List<int> idx, string prop)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (int i in idx)
            {
                var v = nodes[i].ChildScalar(prop);
                if (v == null) return false;
                if (!seen.Add(nodes[i].HeadType + "|" + v)) return false;
            }
            return true;
        }

        public static bool Compatible(Node a, Node b)
        {
            if (a.Kind != b.Kind) return false;
            switch (a.Kind)
            {
                case NodeKind.Block: return a.HeadType == b.HeadType;
                case NodeKind.List: return a.HeadType == b.HeadType || (a.HeadType != "map" && b.HeadType != "map");
                case NodeKind.Scalar: return true;
                case NodeKind.Assign: return a.KeyText == b.KeyText;
                default: return false;
            }
        }

        static void PairHunk(List<Node> B, List<int> bu, int a0, int a1, List<Node> M, List<int> mu, int b0, int b1, int[] res)
        {
            int na = a1 - a0, nm = b1 - b0;
            if (na == nm)
            {
                for (int k = 0; k < na; k++)
                {
                    var bn = B[bu[a0 + k]];
                    var mn = M[mu[b0 + k]];
                    if (Compatible(bn, mn)) res[mu[b0 + k]] = bu[a0 + k];
                }
                return;
            }
            if ((long)na * nm > 250_000)
            {
                // Very large unequal hunk: greedy in-order pairing of compatible, similar containers.
                int pj = 0;
                for (int i = 0; i < na && pj < nm; i++)
                {
                    var bn = B[bu[a0 + i]];
                    for (int j = pj; j < Math.Min(nm, pj + 8); j++)
                    {
                        var mn = M[mu[b0 + j]];
                        if (Compatible(bn, mn) && bn.AsContainer != null && Similarity(bn, mn) >= 0.5)
                        {
                            res[mu[b0 + j]] = bu[a0 + i];
                            pj = j + 1;
                            break;
                        }
                    }
                }
                return;
            }
            // Order-preserving alignment that maximizes similarity of paired containers.
            var score = new double[na + 1, nm + 1];
            var sim = new double[na, nm];
            for (int i = 0; i < na; i++)
                for (int j = 0; j < nm; j++)
                {
                    var bn = B[bu[a0 + i]];
                    var mn = M[mu[b0 + j]];
                    sim[i, j] = Compatible(bn, mn) && bn.AsContainer != null ? Similarity(bn, mn) : -1;
                }
            for (int i = na - 1; i >= 0; i--)
                for (int j = nm - 1; j >= 0; j--)
                {
                    double best = Math.Max(score[i + 1, j], score[i, j + 1]);
                    if (sim[i, j] >= 0.5) best = Math.Max(best, score[i + 1, j + 1] + sim[i, j]);
                    score[i, j] = best;
                }
            int ii = 0, jj = 0;
            while (ii < na && jj < nm)
            {
                if (sim[ii, jj] >= 0.5 && Math.Abs(score[ii, jj] - (score[ii + 1, jj + 1] + sim[ii, jj])) < 1e-9)
                {
                    res[mu[b0 + jj]] = bu[a0 + ii];
                    ii++; jj++;
                }
                else if (score[ii + 1, jj] >= score[ii, jj + 1]) ii++;
                else jj++;
            }
        }

        /// <summary>Fraction of children shared by two containers (multiset of child hashes).</summary>
        public static double Similarity(Node a, Node b)
        {
            var ca = a.AsContainer;
            var cb = b.AsContainer;
            if (ca == null || cb == null) return a.SameContent(b) ? 1 : 0;
            if (ca.Children.Count == 0 && cb.Children.Count == 0) return 1;
            var counts = new Dictionary<(ulong, ulong), int>();
            foreach (var c in ca.Children) { counts.TryGetValue(c.Hash, out int n); counts[c.Hash] = n + 1; }
            int common = 0;
            foreach (var c in cb.Children)
            {
                if (counts.TryGetValue(c.Hash, out int n) && n > 0) { common++; counts[c.Hash] = n - 1; }
            }
            return (double)common / Math.Max(ca.Children.Count, cb.Children.Count);
        }

        /// <summary>Human readable label of a child, used in conflict paths and reports.</summary>
        public static string Label(Node n, int index)
        {
            if (n.Key != null)
            {
                var k = n.Key;
                return k.Length > 2 && k[1] == ':' ? k.Substring(2) : k;
            }
            switch (n.Kind)
            {
                case NodeKind.Block:
                    foreach (var p in IdentityProps)
                    {
                        var v = n.ChildScalar(p);
                        if (v != null) return n.HeadType + "[" + p + "=" + v + "]";
                    }
                    return n.HeadType + "[" + index + "]";
                case NodeKind.Include: return n.Snippet(80);
                case NodeKind.Assign: return n.KeyText + "[" + index + "]";
                case NodeKind.List: return (n.HeadType.Length > 0 ? n.HeadType : "list") + "[" + index + "]";
                default: return "[" + index + "]";
            }
        }
    }
}
