using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TG3ModLoader.Util;

namespace TG3ModLoader.Merge
{
    /// <summary>
    /// Line based three-way merge (diff3 style) used for text files that are not script files
    /// (e.g. UI layouts) and as a fallback when a script file cannot be parsed.
    /// </summary>
    public static class LineMerger
    {
        sealed class Hunk
        {
            public ModVersion V;
            public int BS, BE;
            public List<string> Lines;
            public bool IsInsert => BS == BE;
        }

        public static List<string> SplitLines(string s)
        {
            var res = new List<string>();
            int start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\n') { res.Add(s.Substring(start, i - start + 1)); start = i + 1; }
            }
            if (start < s.Length) res.Add(s.Substring(start));
            return res;
        }

        static string Norm(string line) => line.TrimEnd();

        public static string Merge(string file, string baseText, List<(ModVersion V, string Text)> versions, ConflictPolicy policy, List<Conflict> conflicts)
        {
            var baseLines = SplitLines(baseText);
            var hunks = new List<Hunk>();
            foreach (var (v, text) in versions)
            {
                var lines = SplitLines(text);
                var (ia, ib) = Diff.Intern(baseLines.Select(Norm).ToList(), lines.Select(Norm).ToList());
                var lcs = Diff.Lcs(ia, ib);
                int pa = 0, pb = 0;
                for (int k = 0; k <= lcs.Count; k++)
                {
                    int x = k < lcs.Count ? lcs[k].A : baseLines.Count;
                    int y = k < lcs.Count ? lcs[k].B : lines.Count;
                    if (x > pa || y > pb) hunks.Add(new Hunk { V = v, BS = pa, BE = x, Lines = lines.GetRange(pb, y - pb) });
                    pa = x + 1; pb = y + 1;
                }
            }
            hunks.Sort((a, b) => a.BS != b.BS ? a.BS.CompareTo(b.BS) : a.BE.CompareTo(b.BE));

            var sb = new StringBuilder();
            int cur = 0;
            int idx = 0;
            while (idx < hunks.Count)
            {
                // Build a cluster of overlapping hunks.
                var cluster = new List<Hunk> { hunks[idx] };
                int cs = hunks[idx].BS, ce = hunks[idx].BE;
                idx++;
                while (idx < hunks.Count)
                {
                    var h = hunks[idx];
                    bool overlap = h.BS < ce || (h.BS == cs && (h.IsInsert || cluster.Any(c => c.IsInsert && c.BS == h.BS)))
                                   || (h.IsInsert && h.BS == ce && cluster.Any(c => !c.IsInsert && c.BE == h.BS && c.V == h.V));
                    if (!overlap) break;
                    cluster.Add(h);
                    ce = Math.Max(ce, h.BE);
                    idx++;
                }
                for (int i = cur; i < cs; i++) sb.Append(baseLines[i]);

                var byVersion = cluster.GroupBy(h => h.V).ToList();
                if (byVersion.Count == 1)
                {
                    Apply(sb, baseLines, cs, ce, cluster);
                }
                else if (cluster.All(h => h.IsInsert && h.BS == cs))
                {
                    // Pure insertions at the same place: keep all of them (identical blocks once).
                    var seen = new HashSet<string>();
                    foreach (var g in byVersion.OrderBy(g => g.Key.Order))
                    {
                        var block = string.Concat(g.SelectMany(h => h.Lines));
                        if (seen.Add(Norm(block.Replace("\r", "")))) sb.Append(block);
                    }
                }
                else
                {
                    var texts = byVersion.Select(g => (g.Key, Render(baseLines, cs, ce, g.ToList()))).ToList();
                    var distinct = texts.Select(t => Norm(t.Item2.Replace("\r", ""))).Distinct().Count();
                    if (distinct == 1) sb.Append(texts.OrderBy(t => t.Key.Priority).Last().Item2);
                    else
                    {
                        string path = "lines " + (cs + 1) + "-" + Math.Max(cs + 1, ce);
                        var winner = policy.Pick(file, path, byVersion.Select(g => g.Key), out bool byOverride);
                        var c = new Conflict { File = file, Path = path, Kind = "line", WinnerName = winner.Name, WinnerId = winner.Id, ByOverride = byOverride };
                        c.Candidates.Add(("Vanilla", Short(string.Concat(baseLines.Skip(cs).Take(ce - cs)))));
                        foreach (var t in texts.OrderBy(t => t.Key.Order)) c.Candidates.Add((t.Key.Name, Short(t.Item2)));
                        conflicts.Add(c);
                        sb.Append(texts.First(t => t.Key == winner).Item2);
                    }
                }
                cur = ce;
            }
            for (int i = cur; i < baseLines.Count; i++) sb.Append(baseLines[i]);
            return sb.ToString();
        }

        static string Render(List<string> baseLines, int cs, int ce, List<Hunk> hunks)
        {
            var sb = new StringBuilder();
            Apply(sb, baseLines, cs, ce, hunks);
            return sb.ToString();
        }

        static void Apply(StringBuilder sb, List<string> baseLines, int cs, int ce, List<Hunk> hunks)
        {
            int cur = cs;
            foreach (var h in hunks.OrderBy(h => h.BS).ThenBy(h => h.BE))
            {
                for (int i = cur; i < h.BS; i++) sb.Append(baseLines[i]);
                foreach (var l in h.Lines) sb.Append(l);
                cur = Math.Max(cur, h.BE);
            }
            for (int i = cur; i < ce; i++) sb.Append(baseLines[i]);
        }

        static string Short(string s)
        {
            s = s.Replace("\r", "").Replace("\n", " | ").Replace("\t", " ").Trim();
            return s.Length > 160 ? s.Substring(0, 160) + " ..." : s;
        }
    }
}
