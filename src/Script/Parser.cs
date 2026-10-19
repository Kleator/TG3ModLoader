using System;
using System.Collections.Generic;
using System.Text;

namespace TG3ModLoader.Script
{
    public enum NodeKind { Document, Assign, Block, List, Scalar, Include, Function, Raw }

    public sealed class ParseException : Exception
    {
        public ParseException(string msg) : base(msg) { }
    }

    /// <summary>The token stream of one parsed file version (vanilla or a mod).</summary>
    public sealed class SourceDoc
    {
        public string Label;
        public List<Token> Tokens;
    }

    /// <summary>
    /// Lossless syntax tree node. Every node references a token range of its source document, so
    /// unchanged nodes can be written back byte-for-byte (including comments and formatting).
    /// </summary>
    public sealed class Node
    {
        public NodeKind Kind;
        public SourceDoc Doc;
        /// <summary>Token range [Start, End). Includes trailing separator tokens of an element.</summary>
        public int Start, End;
        /// <summary>First trailing separator token (';' or ','), or -1.</summary>
        public int SepStart = -1;
        /// <summary>True if the element had no separator although another element followed it in its source.</summary>
        public bool SepOptional;

        /// <summary>Identity inside the parent container (null = matched by content/position).</summary>
        public string Key;

        // Assign
        public int EqTok = -1;
        public string KeyText;
        public Node Value;

        // Containers (Document, Block, List)
        public int OpenTok = -1, CloseTok = -1;
        public List<Node> Children;
        public string Head = "";
        public string HeadType = "";
        public bool IsSequence;

        // Function
        public string FuncName;

        public ulong H1, H2;

        public int ContentEnd => SepStart >= 0 ? SepStart : End;
        public bool IsContainer => Kind == NodeKind.Document || Kind == NodeKind.Block || Kind == NodeKind.List;
        public bool HasSep => SepStart >= 0;
        public (ulong, ulong) Hash => (H1, H2);
        public bool SameContent(Node o) => o != null && o.H1 == H1 && o.H2 == H2;

        /// <summary>The container for children-level merging: the node itself or the value of an assignment.</summary>
        public Node AsContainer => IsContainer ? this : (Kind == NodeKind.Assign && Value != null && Value.IsContainer ? Value : null);

        public string Text(bool withLeadingTrivia = false) => TextRange(Doc, Start, ContentEnd, withLeadingTrivia);

        public static string TextRange(SourceDoc doc, int start, int end, bool withLeadingTrivia)
        {
            var sb = new StringBuilder();
            for (int i = start; i < end; i++)
            {
                var t = doc.Tokens[i];
                if (i > start || withLeadingTrivia) sb.Append(t.Trivia);
                sb.Append(t.Text);
            }
            return sb.ToString();
        }

        /// <summary>Compact single-line text for reports.</summary>
        public string Snippet(int max = 120)
        {
            var sb = new StringBuilder();
            for (int i = Start; i < ContentEnd && sb.Length <= max; i++)
            {
                var t = Doc.Tokens[i];
                if (sb.Length > 0 && NeedsSpace(sb[sb.Length - 1], t)) sb.Append(' ');
                sb.Append(t.Text);
            }
            var s = sb.ToString().Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            return s.Length > max ? s.Substring(0, max) + " ..." : s;
        }

        static bool NeedsSpace(char prev, Token t)
        {
            if (t.Kind == TokKind.Semi || t.Kind == TokKind.Comma || t.Kind == TokKind.Close) return false;
            if (prev == '{' || prev == '(') return false;
            return true;
        }

        public int Line => Doc.Tokens[Start].Line;

        /// <summary>Returns the scalar value text of a direct child assignment, or null.</summary>
        public string ChildScalar(string key)
        {
            if (Children == null) return null;
            string found = null;
            foreach (var c in Children)
            {
                if (c.Kind == NodeKind.Assign && c.KeyText == key && c.Value != null && c.Value.Kind == NodeKind.Scalar)
                {
                    if (found != null) return null; // ambiguous
                    found = c.Value.Text();
                }
            }
            return found;
        }

        public override string ToString() => Kind + " " + (Key ?? "") + " " + Snippet(60);
    }

    public static class Parser
    {
        public static Node Parse(string text, string label)
        {
            var doc = new SourceDoc { Label = label, Tokens = Lexer.Tokenize(text) };
            return new ParserImpl(doc).ParseDocument();
        }

        enum Ctx { Document, BlockBody, Map, Sequence }

        sealed class ParserImpl
        {
            readonly SourceDoc doc;
            readonly List<Token> t;
            int pos;

            public ParserImpl(SourceDoc d) { doc = d; t = d.Tokens; }

            Exception Err(string msg)
            {
                var tk = t[Math.Min(pos, t.Count - 1)];
                return new ParseException(msg + " (line " + tk.Line + ", token '" + tk.Text + "')");
            }

            public Node ParseDocument()
            {
                var n = new Node { Kind = NodeKind.Document, Doc = doc, Start = 0, IsSequence = false };
                n.Children = ParseItems(Ctx.Document, false);
                if (t[pos].Kind != TokKind.EOF) throw Err("Unexpected token");
                n.CloseTok = pos;
                n.End = t.Count;
                AssignKeys(n.Children, Ctx.Document);
                ComputeHash(n);
                return n;
            }

            List<Node> ParseItems(Ctx ctx, bool expectClose)
            {
                var items = new List<Node>();
                while (true)
                {
                    var tk = t[pos];
                    if (tk.Kind == TokKind.EOF)
                    {
                        if (expectClose) throw Err("Unexpected end of file, missing closing bracket");
                        break;
                    }
                    if (tk.Kind == TokKind.Close)
                    {
                        if (!expectClose) throw Err("Unexpected closing bracket");
                        break;
                    }
                    if (tk.Kind == TokKind.Semi || tk.Kind == TokKind.Comma)
                    {
                        // Stray separator: attach to the previous element when adjacent, otherwise keep as raw node.
                        if (items.Count > 0 && items[items.Count - 1].End == pos)
                        {
                            var last = items[items.Count - 1];
                            if (last.SepStart < 0) last.SepStart = pos;
                            last.SepOptional = false;
                            pos++;
                            last.End = pos;
                        }
                        else
                        {
                            items.Add(new Node { Kind = NodeKind.Raw, Doc = doc, Start = pos, End = pos + 1 });
                            pos++;
                        }
                        continue;
                    }
                    if (tk.Kind == TokKind.Equals) throw Err("Unexpected '='");

                    var item = ParseItem(ctx);
                    if (t[pos].Kind == TokKind.Semi || t[pos].Kind == TokKind.Comma)
                    {
                        item.SepStart = pos;
                        pos++;
                        item.End = pos;
                    }
                    else item.SepOptional = true;
                    items.Add(item);
                }
                if (items.Count > 0 && items[items.Count - 1].SepStart < 0) items[items.Count - 1].SepOptional = false;
                return items;
            }

            Node ParseItem(Ctx ctx)
            {
                var tk = t[pos];
                if (tk.Kind == TokKind.Word && tk.Text.Length > 1 && tk.Text[0] == '@')
                {
                    var inc = new Node { Kind = NodeKind.Include, Doc = doc, Start = pos, End = pos + 1 };
                    pos++;
                    return inc;
                }
                if (tk.Kind == TokKind.Word && tk.Text == "function") return ParseFunction();

                int j = pos;
                while (t[j].Kind == TokKind.Word || t[j].Kind == TokKind.String) j++;
                if (j > pos && t[j].Kind == TokKind.Equals)
                {
                    var a = new Node { Kind = NodeKind.Assign, Doc = doc, Start = pos, EqTok = j };
                    a.KeyText = NormalizeKey(pos, j);
                    pos = j + 1;
                    a.Value = ParseValue();
                    a.End = pos;
                    return a;
                }
                return ParseValue();
            }

            string NormalizeKey(int start, int end)
            {
                var parts = new List<string>();
                for (int i = start; i < end; i++)
                {
                    var s = t[i].Text;
                    if (parts.Count == 0 && i < end - 1 && (s == "static" || s == "var" || s == "const")) continue;
                    parts.Add(s);
                }
                return string.Join(" ", parts);
            }

            Node ParseFunction()
            {
                var f = new Node { Kind = NodeKind.Function, Doc = doc, Start = pos };
                pos++;
                if (t[pos].Kind == TokKind.Word) { f.FuncName = t[pos].Text; pos++; }
                else f.FuncName = "";
                if (t[pos].Kind == TokKind.Open) SkipBalanced();
                if (t[pos].Kind != TokKind.Open) throw Err("Function body expected");
                SkipBalanced();
                f.End = pos;
                return f;
            }

            void SkipBalanced()
            {
                int depth = 0;
                do
                {
                    var k = t[pos].Kind;
                    if (k == TokKind.EOF) throw Err("Unbalanced brackets in function");
                    if (k == TokKind.Open) depth++;
                    else if (k == TokKind.Close) depth--;
                    pos++;
                } while (depth > 0);
            }

            Node ParseValue()
            {
                var tk = t[pos];
                if (tk.Kind == TokKind.Word && (tk.Text == "array" || tk.Text == "map") && t[pos + 1].Kind == TokKind.Open)
                {
                    var l = new Node { Kind = NodeKind.List, Doc = doc, Start = pos, OpenTok = pos + 1, Head = tk.Text, HeadType = tk.Text };
                    l.IsSequence = tk.Text == "array";
                    pos += 2;
                    var ctx = l.IsSequence ? Ctx.Sequence : Ctx.Map;
                    l.Children = ParseItems(ctx, true);
                    l.CloseTok = pos;
                    pos++;
                    l.End = pos;
                    AssignKeys(l.Children, ctx);
                    return l;
                }
                if (tk.Kind == TokKind.Open)
                {
                    var l = new Node { Kind = NodeKind.List, Doc = doc, Start = pos, OpenTok = pos, Head = "", HeadType = tk.Text == "(" ? "tuple" : "group", IsSequence = true };
                    pos++;
                    l.Children = ParseItems(Ctx.Sequence, true);
                    l.CloseTok = pos;
                    pos++;
                    l.End = pos;
                    AssignKeys(l.Children, Ctx.Sequence);
                    return l;
                }
                if (tk.Kind == TokKind.Word && tk.Text.Length > 1 && tk.Text[0] == '@')
                {
                    // Include used as value, e.g. "LawSettings = @base/LawSettings" (the included text brings its own separator).
                    var inc = new Node { Kind = NodeKind.Include, Doc = doc, Start = pos, End = pos + 1 };
                    pos++;
                    return inc;
                }
                if (tk.Kind == TokKind.Word || tk.Kind == TokKind.String)
                {
                    int j = pos;
                    while (t[j].Kind == TokKind.Word || t[j].Kind == TokKind.String)
                    {
                        // A word directly followed by '=' starts the next assignment (missing separator).
                        if (j > pos && t[j + 1].Kind == TokKind.Equals) break;
                        j++;
                    }
                    if (t[j].Kind == TokKind.Open)
                    {
                        var b = new Node { Kind = NodeKind.Block, Doc = doc, Start = pos, OpenTok = j, IsSequence = false };
                        var sb = new StringBuilder();
                        for (int i = pos; i < j; i++) { if (i > pos) sb.Append(' '); sb.Append(t[i].Text); }
                        b.Head = sb.ToString();
                        b.HeadType = t[pos].Text;
                        pos = j + 1;
                        b.Children = ParseItems(Ctx.BlockBody, true);
                        b.CloseTok = pos;
                        pos++;
                        b.End = pos;
                        AssignKeys(b.Children, Ctx.BlockBody);
                        return b;
                    }
                    var s = new Node { Kind = NodeKind.Scalar, Doc = doc, Start = pos, End = j };
                    pos = j;
                    return s;
                }
                throw Err("Value expected");
            }

            /// <summary>
            /// Assigns identity keys. A key that occurs more than once in the same container (e.g. a list of
            /// "RequiredBuilding = ..." entries) describes a list, so those elements stay unkeyed and are
            /// aligned by content like array elements.
            /// </summary>
            static void AssignKeys(List<Node> children, Ctx ctx)
            {
                var keys = new string[children.Count];
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < children.Count; i++)
                {
                    var c = children[i];
                    string k = null;
                    switch (c.Kind)
                    {
                        case NodeKind.Assign: k = "A:" + c.KeyText; break;
                        case NodeKind.Function: k = "F:" + c.FuncName; break;
                        case NodeKind.Block:
                            if (ctx == Ctx.BlockBody || ctx == Ctx.Map || (ctx == Ctx.Document && c.Head.Contains(" extends ")))
                                k = "B:" + c.Head;
                            break;
                    }
                    keys[i] = k;
                    if (k != null)
                    {
                        counts.TryGetValue(k, out int n);
                        counts[k] = n + 1;
                    }
                }
                for (int i = 0; i < children.Count; i++)
                    children[i].Key = keys[i] != null && counts[keys[i]] == 1 ? keys[i] : null;
            }

            void ComputeHash(Node n)
            {
                var h = new Hasher();
                h.Add((ulong)n.Kind);
                switch (n.Kind)
                {
                    case NodeKind.Assign:
                        h.Add(n.KeyText);
                        ComputeHash(n.Value);
                        h.Add(n.Value.H1); h.Add(n.Value.H2);
                        break;
                    case NodeKind.Document:
                    case NodeKind.Block:
                    case NodeKind.List:
                        h.Add(n.Head);
                        h.Add(n.HeadType);
                        foreach (var c in n.Children)
                        {
                            ComputeHash(c);
                            h.Add(c.H1); h.Add(c.H2);
                        }
                        break;
                    default:
                        for (int i = n.Start; i < n.ContentEnd; i++) h.Add(t[i].Text);
                        break;
                }
                n.H1 = h.A; n.H2 = h.B;
            }
        }
    }

    /// <summary>128-bit content hash (two independent 64-bit FNV variants).</summary>
    public struct Hasher
    {
        public ulong A, B;
        bool init;

        void Init()
        {
            if (init) return;
            A = 14695981039346656037UL;
            B = 0x84222325CBF29CE4UL;
            init = true;
        }

        public void Add(string s)
        {
            Init();
            foreach (char c in s)
            {
                A = (A ^ c) * 1099511628211UL;
                B = ((B ^ c) * 0x9E3779B97F4A7C15UL);
                B ^= B >> 29;
            }
            A = (A ^ 0xFFFF) * 1099511628211UL;
            B = (B ^ 0xFFFF) * 0x9E3779B97F4A7C15UL;
            B ^= B >> 29;
        }

        public void Add(ulong x)
        {
            Init();
            for (int i = 0; i < 8; i++)
            {
                byte by = (byte)(x >> (i * 8));
                A = (A ^ by) * 1099511628211UL;
                B = (B ^ by) * 0x9E3779B97F4A7C15UL;
                B ^= B >> 29;
            }
        }
    }
}
