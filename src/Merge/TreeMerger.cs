using System;
using System.Collections.Generic;
using System.Linq;
using TG3ModLoader.Script;

namespace TG3ModLoader.Merge
{
    /// <summary>
    /// A mod taking part in a merge. <see cref="Priority"/> decides real conflicts (higher wins; the mod at the
    /// top of the list has the highest priority). <see cref="Order"/> is the list position (0 = top) and decides
    /// the sequence in which additions of several mods at the same place are combined.
    /// </summary>
    public sealed class ModVersion
    {
        public int Priority;
        public int Order;
        public string Id;
        public string Name;
        public override string ToString() => Name;
    }

    public struct VN
    {
        public ModVersion V;
        public Node N;
        public VN(ModVersion v, Node n) { V = v; N = n; }
    }

    public sealed class Conflict
    {
        public string File;
        public string Path;
        /// <summary>"value", "delete" (delete vs. modify), "line" (text hunk) or "file" (binary file).</summary>
        public string Kind;
        public List<(string ModName, string Text)> Candidates = new List<(string, string)>();
        public string WinnerName;
        public string WinnerId;
        public bool ByOverride;

        public string Id => File + "::" + Path;
    }

    /// <summary>
    /// Resolves real conflicts: a user decision first, otherwise the mod with the highest priority.
    /// A decision is stored as "file::path" -> mod id, or "=value" for a custom value.
    /// </summary>
    public sealed class ConflictPolicy
    {
        public const string CustomPrefix = "=";
        readonly Dictionary<string, string> overrides;

        public ConflictPolicy(Dictionary<string, string> overrides)
        {
            this.overrides = overrides ?? new Dictionary<string, string>();
        }

        public ModVersion Pick(string file, string path, IEnumerable<ModVersion> candidates, out bool byOverride)
        {
            var list = candidates.ToList();
            byOverride = false;
            if (overrides.TryGetValue(file + "::" + path, out var id))
            {
                var o = list.FirstOrDefault(v => v.Id == id);
                if (o != null) { byOverride = true; return o; }
            }
            return list.OrderBy(v => v.Priority).Last();
        }

        /// <summary>A custom value chosen by the user for this location (only valid single values are accepted).</summary>
        public bool TryCustomValue(string file, string path, out string value)
        {
            value = null;
            if (!overrides.TryGetValue(file + "::" + path, out var v) || !v.StartsWith(CustomPrefix)) return false;
            value = v.Substring(CustomPrefix.Length).Trim();
            return IsValidScalar(value);
        }

        public static bool IsValidScalar(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.IndexOf('\n') >= 0 || value.Contains("//") || value.Contains("/*")) return false;
            try
            {
                var toks = Lexer.Tokenize(value);
                return toks.Count >= 2 && toks.Take(toks.Count - 1).All(t => t.Kind == TokKind.Word || t.Kind == TokKind.String)
                       && toks[toks.Count - 1].Trivia.Trim().Length == 0;
            }
            catch (LexerException) { return false; }
        }
    }

    /// <summary>
    /// Structural three-way merge of script files. Every mod is diffed against vanilla; all
    /// non-overlapping changes are combined, overlapping changes are resolved by priority.
    /// </summary>
    public sealed class TreeMerger
    {
        readonly string file;
        readonly ConflictPolicy policy;
        readonly bool additiveOnly;
        public readonly List<Conflict> Conflicts = new List<Conflict>();

        public TreeMerger(string file, ConflictPolicy policy, bool additiveOnly)
        {
            this.file = file;
            this.policy = policy;
            this.additiveOnly = additiveOnly;
        }

        public string Merge(Node baseRoot, List<VN> versions)
        {
            var changed = versions.Where(v => !v.N.SameContent(baseRoot)).ToList();
            var o = MergeNode(baseRoot, changed, "");
            return o.Render();
        }

        Out MergeNode(Node b, List<VN> changed, string path)
        {
            if (changed.Count == 0) return Out.Of(b);
            var top = changed.OrderBy(c => c.V.Priority).Last();
            // In additive mode (localization) removals are ignored, so containers always need a real merge.
            bool mustMerge = additiveOnly && (b.IsContainer || b.Kind == NodeKind.Assign) && SameShape(b, changed);
            if (!mustMerge)
            {
                if (changed.All(c => c.N.SameContent(top.N))) return Out.Of(top.N);
                if (changed.Count == 1) return Out.Of(changed[0].N);
            }

            if (SameShape(b, changed))
            {
                if (b.Kind == NodeKind.Assign) return MergeAssign(b, changed, path);
                return MergeContainer(b, changed, path);
            }

            // Same number written differently (4 / 4.0 / 4.0f) is not a real conflict.
            if (changed.All(c => ScalarEquivalent(c.N, top.N))) return Out.Of(top.N);

            // The user may have chosen an own value (or the vanilla value) for this location.
            if (b != null && b.Kind == NodeKind.Scalar && changed.All(c => c.N.Kind == NodeKind.Scalar) && policy.TryCustomValue(file, path, out var custom))
            {
                var rec = Record("value", path, b, changed, top.V, true);
                rec.WinnerName = "custom value: " + custom;
                rec.WinnerId = ConflictPolicy.CustomPrefix + custom;
                var seq = new OutSeq();
                seq.Parts.Add(new OutText(b.Doc.Tokens[b.Start].Trivia + custom));
                if (b.HasSep) seq.Parts.Add(new OutTokens(b.Doc, b.SepStart, b.End));
                seq.WithFlags(b);
                return seq;
            }

            var winner = policy.Pick(file, path, changed.Select(c => c.V), out bool byOverride);
            var win = changed.Last(c => c.V == winner);
            Record("value", path, b, changed, winner, byOverride);
            return Out.Of(win.N);
        }

        public static bool ScalarEquivalent(Node a, Node b)
        {
            if (a.SameContent(b)) return true;
            if (a.Kind != NodeKind.Scalar || b.Kind != NodeKind.Scalar) return false;
            if (a.ContentEnd - a.Start != 1 || b.ContentEnd - b.Start != 1) return false;
            return TryNumber(a.Doc.Tokens[a.Start].Text, out double x) && TryNumber(b.Doc.Tokens[b.Start].Text, out double y) && x == y;
        }

        static bool TryNumber(string s, out double v)
        {
            if (s.EndsWith("f") || s.EndsWith("F")) s = s.Substring(0, s.Length - 1);
            return double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
        }

        static bool SameShape(Node b, List<VN> changed)
        {
            switch (b.Kind)
            {
                case NodeKind.Assign:
                    return changed.All(c => c.N.Kind == NodeKind.Assign && c.N.KeyText == b.KeyText);
                case NodeKind.Document:
                    return changed.All(c => c.N.Kind == NodeKind.Document);
                case NodeKind.Block:
                    return changed.All(c => c.N.Kind == NodeKind.Block && c.N.HeadType == b.HeadType);
                case NodeKind.List:
                    return changed.All(c => c.N.Kind == NodeKind.List && Matcher.Compatible(b, c.N));
                default:
                    return false;
            }
        }

        Out MergeAssign(Node b, List<VN> changed, string path)
        {
            var vals = changed.Where(c => !c.N.Value.SameContent(b.Value)).Select(c => new VN(c.V, c.N.Value)).ToList();
            var seq = new OutSeq();
            seq.Parts.Add(new OutTokens(b.Doc, b.Start, b.EqTok + 1));
            seq.Parts.Add(MergeNode(b.Value, vals, path));
            if (b.HasSep) seq.Parts.Add(new OutTokens(b.Doc, b.SepStart, b.End));
            seq.WithFlags(b);
            return seq;
        }

        sealed class Align
        {
            public VN Ver;
            public int[] BaseToMod;
            public List<(int Anchor, Node N)> Inserts = new List<(int, Node)>();
        }

        Out MergeContainer(Node b, List<VN> changed, string path)
        {
            var bc = b.Children;
            var aligns = new List<Align>();
            foreach (var c in changed.OrderBy(c => c.V.Order))
            {
                var map = Matcher.Match(b, c.N);
                var al = new Align { Ver = c, BaseToMod = Enumerable.Repeat(-1, bc.Count).ToArray() };
                int lastBase = -1;
                for (int j = 0; j < map.Length; j++)
                {
                    int bi = map[j];
                    if (bi >= 0 && al.BaseToMod[bi] < 0)
                    {
                        al.BaseToMod[bi] = j;
                        lastBase = bi;
                    }
                    else al.Inserts.Add((lastBase, c.N.Children[j]));
                }
                aligns.Add(al);
            }

            // Keyed insertions may come from several mods; resolve them once.
            var keyed = new Dictionary<string, List<VN>>(StringComparer.Ordinal);
            foreach (var al in aligns)
                foreach (var ins in al.Inserts)
                    if (ins.N.Key != null)
                    {
                        if (!keyed.TryGetValue(ins.N.Key, out var l)) keyed[ins.N.Key] = l = new List<VN>();
                        l.Add(new VN(al.Ver.V, ins.N));
                    }
            var placed = new HashSet<string>(StringComparer.Ordinal);
            var children = new List<Out>();

            void EmitAnchor(int anchor)
            {
                var acc = new List<Node>();
                foreach (var al in aligns)
                {
                    var seq = al.Inserts.Where(x => x.Anchor == anchor).Select(x => x.N).ToList();
                    if (seq.Count > 0) acc = UnionSequence(acc, seq);
                }
                foreach (var n in acc)
                {
                    if (n.Key != null)
                    {
                        if (!placed.Add(n.Key)) continue;
                        var vers = keyed[n.Key];
                        children.Add(MergeInserted(vers, path + "/" + Matcher.Label(n, -1)));
                    }
                    else children.Add(Out.Of(n));
                }
            }

            EmitAnchor(-1);
            for (int i = 0; i < bc.Count; i++)
            {
                var be = bc[i];
                var mods = new List<VN>();
                var dels = new List<VN>();
                foreach (var al in aligns)
                {
                    int j = al.BaseToMod[i];
                    if (j < 0) dels.Add(al.Ver);
                    else
                    {
                        var mn = al.Ver.N.Children[j];
                        if (!mn.SameContent(be)) mods.Add(new VN(al.Ver.V, mn));
                    }
                }
                string cpath = path + "/" + Matcher.Label(be, i);
                if (dels.Count > 0 && additiveOnly) dels.Clear();

                if (dels.Count == 0) children.Add(MergeNode(be, mods, cpath));
                else if (mods.Count > 0)
                {
                    var all = dels.Concat(mods).Select(x => x.V).ToList();
                    var winner = policy.Pick(file, cpath, all, out bool byOverride);
                    var c = new Conflict { File = file, Path = cpath, Kind = "delete", WinnerName = winner.Name, WinnerId = winner.Id, ByOverride = byOverride };
                    c.Candidates.Add(("Vanilla", be.Snippet()));
                    foreach (var d in dels) c.Candidates.Add((d.V.Name, "(removed)"));
                    foreach (var m in mods) c.Candidates.Add((m.V.Name, m.N.Snippet()));
                    Conflicts.Add(c);
                    if (mods.Any(m => m.V == winner)) children.Add(MergeNode(be, mods, cpath));
                }
                // else: removed by every mod that touched it
                EmitAnchor(i);
            }

            var parts = new OutSeq();
            if (b.Kind != NodeKind.Document)
            {
                var headSrc = b;
                foreach (var c in changed.OrderBy(c => c.V.Priority))
                    if (c.N.Head != b.Head) headSrc = c.N;
                parts.Parts.Add(new OutTokens(headSrc.Doc, headSrc.Start, headSrc.OpenTok + 1));
            }
            string defSep = DefaultSeparator(b);
            for (int k = 0; k < children.Count; k++)
            {
                var ch = children[k];
                parts.Parts.Add(ch);
                if (k < children.Count - 1)
                {
                    var nx = children[k + 1];
                    if (!ch.HasSep && !ch.SepOptional && !ch.IsInclude && !nx.IsInclude) parts.Parts.Add(new OutText(defSep));
                }
            }
            parts.Parts.Add(new OutTokens(b.Doc, b.CloseTok, b.CloseTok + 1));
            if (b.HasSep) parts.Parts.Add(new OutTokens(b.Doc, b.SepStart, b.End));
            parts.WithFlags(b);
            return parts;
        }

        /// <summary>A keyed element that does not exist in vanilla but was added by one or more mods.</summary>
        Out MergeInserted(List<VN> vers, string path)
        {
            if (vers.Count == 1) return Out.Of(vers[0].N);
            var first = vers.OrderBy(v => v.V.Order).First();
            var empty = EmptyClone(first.N);
            if (empty != null) return MergeNode(empty, vers, path);
            var top = vers.OrderBy(v => v.V.Priority).Last();
            if (vers.All(v => v.N.SameContent(top.N))) return Out.Of(top.N);
            var winner = policy.Pick(file, path, vers.Select(v => v.V), out bool byOverride);
            Record("value", path, null, vers, winner, byOverride);
            return Out.Of(vers.Last(v => v.V == winner).N);
        }

        /// <summary>Creates an empty container of the same shape, used as the common base of added elements.</summary>
        internal static Node EmptyClone(Node n)
        {
            if (n.Kind == NodeKind.Assign)
            {
                if (n.Value == null || !n.Value.IsContainer) return null;
                var v = EmptyClone(n.Value);
                return new Node
                {
                    Kind = n.Kind, Doc = n.Doc, Start = n.Start, End = n.End, SepStart = n.SepStart, SepOptional = n.SepOptional,
                    Key = n.Key, EqTok = n.EqTok, KeyText = n.KeyText, Value = v, H1 = 1, H2 = 1
                };
            }
            if (!n.IsContainer) return null;
            return new Node
            {
                Kind = n.Kind, Doc = n.Doc, Start = n.Start, End = n.End, SepStart = n.SepStart, SepOptional = n.SepOptional,
                Key = n.Key, OpenTok = n.OpenTok, CloseTok = n.CloseTok, Children = new List<Node>(), Head = n.Head,
                HeadType = n.HeadType, IsSequence = n.IsSequence, H1 = 1, H2 = 1
            };
        }

        /// <summary>Order-preserving union of two element lists; identical elements are kept once.</summary>
        static List<Node> UnionSequence(List<Node> acc, List<Node> add)
        {
            if (acc.Count == 0) return new List<Node>(add);
            string Id(Node n) => n.Key ?? ("#" + n.H1.ToString("x16") + n.H2.ToString("x16"));
            var (ia, ib) = Util.Diff.Intern(acc.Select(Id).ToList(), add.Select(Id).ToList());
            var lcs = Util.Diff.Lcs(ia, ib);
            var res = new List<Node>();
            int pa = 0, pb = 0;
            for (int k = 0; k <= lcs.Count; k++)
            {
                int x = k < lcs.Count ? lcs[k].A : acc.Count;
                int y = k < lcs.Count ? lcs[k].B : add.Count;
                for (int i = pa; i < x; i++) res.Add(acc[i]);
                for (int j = pb; j < y; j++) res.Add(add[j]);
                if (k < lcs.Count) res.Add(acc[x]);
                pa = x + 1; pb = y + 1;
            }
            return res;
        }

        static string DefaultSeparator(Node b)
        {
            int semi = 0, comma = 0;
            foreach (var c in b.Children)
            {
                if (!c.HasSep) continue;
                if (c.Doc.Tokens[c.SepStart].Kind == TokKind.Semi) semi++; else comma++;
            }
            if (semi == comma) return b.IsSequence ? "," : ";";
            return semi > comma ? ";" : ",";
        }

        Conflict Record(string kind, string path, Node b, List<VN> changed, ModVersion winner, bool byOverride)
        {
            var c = new Conflict { File = file, Path = path, Kind = kind, WinnerName = winner.Name, WinnerId = winner.Id, ByOverride = byOverride };
            if (b != null) c.Candidates.Add(("Vanilla", b.Snippet()));
            foreach (var v in changed.OrderBy(v => v.V.Order)) c.Candidates.Add((v.V.Name, v.N.Snippet()));
            Conflicts.Add(c);
            return c;
        }
    }
}
