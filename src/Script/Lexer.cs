using System.Collections.Generic;

namespace TG3ModLoader.Script
{
    public enum TokKind { Word, String, Open, Close, Semi, Comma, Equals, EOF }

    /// <summary>
    /// A token of the Guild 3 script language (.oc/.oo/.inc/.loo). Every token keeps the
    /// whitespace and comments that precede it, so a token list can reproduce the source exactly.
    /// </summary>
    public sealed class Token
    {
        public TokKind Kind;
        public string Text;
        public string Trivia;
        public int Line;

        public override string ToString() => Kind + ":" + Text;
    }

    public sealed class LexerException : System.Exception
    {
        public LexerException(string msg) : base(msg) { }
    }

    public static class Lexer
    {
        public static List<Token> Tokenize(string s)
        {
            var list = new List<Token>(s.Length / 6 + 16);
            int i = 0, n = s.Length, line = 1;
            while (true)
            {
                int ts = i;
                while (i < n)
                {
                    char c = s[i];
                    if (c == '\n') { line++; i++; continue; }
                    if (char.IsWhiteSpace(c) || c == '\uFEFF') { i++; continue; }
                    if (c == '/' && i + 1 < n && s[i + 1] == '/')
                    {
                        while (i < n && s[i] != '\n') i++;
                        continue;
                    }
                    if (c == '/' && i + 1 < n && s[i + 1] == '*')
                    {
                        int e = s.IndexOf("*/", i + 2, System.StringComparison.Ordinal);
                        int stop = e < 0 ? n : e + 2;
                        for (int k = i; k < stop; k++) if (s[k] == '\n') line++;
                        i = stop;
                        continue;
                    }
                    break;
                }
                string trivia = s.Substring(ts, i - ts);
                if (i >= n)
                {
                    list.Add(new Token { Kind = TokKind.EOF, Text = "", Trivia = trivia, Line = line });
                    return list;
                }
                int start = i, startLine = line;
                TokKind kind;
                char ch = s[i];
                switch (ch)
                {
                    case '{': case '(': kind = TokKind.Open; i++; break;
                    case '}': case ')': kind = TokKind.Close; i++; break;
                    case ';': kind = TokKind.Semi; i++; break;
                    case ',': kind = TokKind.Comma; i++; break;
                    case '=': kind = TokKind.Equals; i++; break;
                    case '"':
                        kind = TokKind.String;
                        i++;
                        while (i < n && s[i] != '"')
                        {
                            if (s[i] == '\\' && i + 1 < n && s[i + 1] != '\n' && s[i + 1] != '\r') { i += 2; continue; }
                            if (s[i] == '\n') line++;
                            i++;
                        }
                        if (i >= n) throw new LexerException("Unterminated string starting at line " + startLine);
                        i++;
                        break;
                    default:
                        kind = TokKind.Word;
                        while (i < n && !IsDelimiter(s, i)) i++;
                        break;
                }
                list.Add(new Token { Kind = kind, Text = s.Substring(start, i - start), Trivia = trivia, Line = startLine });
            }
        }

        static bool IsDelimiter(string s, int i)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) return true;
            switch (c)
            {
                case '{': case '}': case '(': case ')': case ';': case ',': case '=': case '"':
                    return true;
                case '/':
                    return i + 1 < s.Length && (s[i + 1] == '/' || s[i + 1] == '*');
            }
            return false;
        }
    }
}
