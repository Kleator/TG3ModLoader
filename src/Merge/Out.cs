using System.Collections.Generic;
using System.Text;
using TG3ModLoader.Script;

namespace TG3ModLoader.Merge
{
    /// <summary>A piece of merged output. Verbatim token ranges keep the formatting of their source.</summary>
    public abstract class Out
    {
        public bool HasSep;
        public bool SepOptional;
        public bool IsInclude;
        public abstract void Emit(Emitter e);

        public static Out Of(Node n) => new OutTokens(n.Doc, n.Start, n.End).WithFlags(n);

        public Out WithFlags(Node n)
        {
            HasSep = n.HasSep;
            SepOptional = n.SepOptional || n.Kind == NodeKind.Function || n.Kind == NodeKind.Raw
                          || (n.Kind == NodeKind.Assign && n.Value != null && n.Value.Kind == NodeKind.Include);
            IsInclude = n.Kind == NodeKind.Include;
            return this;
        }

        public string Render()
        {
            var e = new Emitter();
            Emit(e);
            return e.ToString();
        }
    }

    public sealed class OutTokens : Out
    {
        readonly SourceDoc doc;
        readonly int start, end;

        public OutTokens(SourceDoc doc, int start, int end) { this.doc = doc; this.start = start; this.end = end; }

        public override void Emit(Emitter e)
        {
            for (int i = start; i < end; i++) e.Token(doc.Tokens[i]);
        }
    }

    public sealed class OutText : Out
    {
        readonly string text;
        public OutText(string text) { this.text = text; }
        public override void Emit(Emitter e) => e.Raw(text);
    }

    public sealed class OutSeq : Out
    {
        public readonly List<Out> Parts = new List<Out>();
        public override void Emit(Emitter e) { foreach (var p in Parts) p.Emit(e); }
    }

    public sealed class Emitter
    {
        readonly StringBuilder sb = new StringBuilder(1 << 16);

        public void Token(Token t)
        {
            if (t.Trivia.Length == 0 && t.Text.Length > 0 && sb.Length > 0 && WouldGlue(sb[sb.Length - 1], t.Text[0]))
                sb.Append(' ');
            sb.Append(t.Trivia);
            sb.Append(t.Text);
        }

        public void Raw(string s) => sb.Append(s);

        static bool IsDelim(char c) => c == '{' || c == '}' || c == '(' || c == ')' || c == ';' || c == ',' || c == '=' || c == '"';

        static bool WouldGlue(char prev, char next)
        {
            if (char.IsWhiteSpace(prev)) return false;
            if (prev == '/' && (next == '/' || next == '*')) return true;
            if (IsDelim(prev) || IsDelim(next)) return false;
            return true;
        }

        public override string ToString() => sb.ToString();
    }
}
