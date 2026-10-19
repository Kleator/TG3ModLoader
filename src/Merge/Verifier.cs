using System;
using System.Collections.Generic;
using System.Linq;
using TG3ModLoader.Script;

namespace TG3ModLoader.Merge
{
    /// <summary>
    /// Checks that every change a mod made relative to vanilla is present in the merged result,
    /// unless it was overridden by a recorded conflict (a higher priority mod changed the same value).
    /// </summary>
    public sealed class Verifier
    {
        readonly string file;
        readonly List<string> conflictPaths;
        readonly bool additiveOnly;
        public readonly List<string> Violations = new List<string>();
        public int CheckedChanges;

        public Verifier(string file, IEnumerable<Conflict> conflicts, bool additiveOnly)
        {
            this.file = file;
            conflictPaths = conflicts.Select(c => c.Path).ToList();
            this.additiveOnly = additiveOnly;
        }

        /// <summary>A conflict was decided exactly at this node or at one of its ancestors.</summary>
        bool ExplainedHere(string path)
        {
            foreach (var c in conflictPaths)
            {
                if (c == path) return true;
                if (path.StartsWith(c + "/", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>Like <see cref="ExplainedHere"/>, but also accepts conflicts inside this node (used for leaves).</summary>
        bool Explained(string path)
        {
            if (ExplainedHere(path)) return true;
            foreach (var c in conflictPaths)
                if (c.StartsWith(path + "/", StringComparison.Ordinal)) return true;
            return false;
        }

        public void Check(Node baseRoot, ModVersion v, Node modRoot, Node merged)
        {
            Check(baseRoot, modRoot, merged, "", v);
        }

        void Fail(ModVersion v, string path, string what)
        {
            Violations.Add(v.Name + ": " + file + " " + (path.Length == 0 ? "/" : path) + " - " + what);
        }

        void Check(Node b, Node m, Node r, string path, ModVersion v)
        {
            if (m.SameContent(b)) return;
            if (r != null && TreeMerger.ScalarEquivalent(r, m)) { CheckedChanges++; return; }
            if (ExplainedHere(path)) { CheckedChanges++; return; }
            if (r == null) { CheckedChanges++; Fail(v, path, "change is missing in the result"); return; }

            if (b.Kind == NodeKind.Assign && m.Kind == NodeKind.Assign && r.Kind == NodeKind.Assign)
            {
                Check(b.Value, m.Value, r.Value, path, v);
                return;
            }
            if (b.IsContainer && m.IsContainer && r.IsContainer && b.Kind == m.Kind && b.Kind == r.Kind)
            {
                var mapM = Matcher.Match(b, m);
                var mapR = Matcher.Match(b, r);
                var baseToM = Invert(mapM, b.Children.Count);
                var baseToR = Invert(mapR, b.Children.Count);
                for (int i = 0; i < b.Children.Count; i++)
                {
                    var bc = b.Children[i];
                    string cp = path + "/" + Matcher.Label(bc, i);
                    int mi = baseToM[i], ri = baseToR[i];
                    if (mi < 0)
                    {
                        if (additiveOnly) continue;
                        CheckedChanges++;
                        if (ri >= 0 && !ExplainedHere(cp)) Fail(v, cp, "removal was not applied");
                        continue;
                    }
                    Check(bc, m.Children[mi], ri >= 0 ? r.Children[ri] : null, cp, v);
                }
                // Elements added by the mod must exist in the result.
                var rKeys = new Dictionary<string, Node>(StringComparer.Ordinal);
                var rHashes = new Dictionary<(ulong, ulong), int>();
                foreach (var rc in r.Children)
                {
                    if (rc.Key != null) rKeys[rc.Key] = rc;
                    rHashes.TryGetValue(rc.Hash, out int n);
                    rHashes[rc.Hash] = n + 1;
                }
                for (int j = 0; j < m.Children.Count; j++)
                {
                    if (mapM[j] >= 0) continue;
                    var mc = m.Children[j];
                    CheckedChanges++;
                    string cp = path + "/" + Matcher.Label(mc, -1);
                    if (rHashes.ContainsKey(mc.Hash)) continue;
                    if (mc.Key != null && rKeys.TryGetValue(mc.Key, out var rc))
                    {
                        // Added by several mods and combined: check this mod's part against an empty base.
                        var empty = TreeMerger.EmptyClone(mc);
                        if (empty != null) { CheckedChanges--; Check(empty, mc, rc, cp, v); }
                        else if (!Explained(cp)) Fail(v, cp, "added element differs");
                    }
                    else if (!ExplainedHere(path) && !Explained(cp))
                        Fail(v, cp, "added element is missing: " + mc.Snippet(80));
                }
                return;
            }
            CheckedChanges++;
            if (Explained(path)) return;
            Fail(v, path, "value differs: expected '" + m.Snippet(60) + "', got '" + r.Snippet(60) + "'");
        }

        static int[] Invert(int[] map, int n)
        {
            var res = Enumerable.Repeat(-1, n).ToArray();
            for (int j = 0; j < map.Length; j++) if (map[j] >= 0 && res[map[j]] < 0) res[map[j]] = j;
            return res;
        }
    }
}
