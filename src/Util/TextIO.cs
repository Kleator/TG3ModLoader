using System;
using System.IO;
using System.Text;

namespace TG3ModLoader.Util
{
    /// <summary>Decoding/encoding of game text files (UTF-8 with or without BOM, UTF-16, ANSI fallback).</summary>
    public sealed class DecodedText
    {
        public string Text;
        public Encoding Encoding;
        public byte[] Preamble = new byte[0];

        public byte[] Encode(string text)
        {
            var body = Encoding.GetBytes(text);
            if (Preamble.Length == 0) return body;
            var res = new byte[Preamble.Length + body.Length];
            Buffer.BlockCopy(Preamble, 0, res, 0, Preamble.Length);
            Buffer.BlockCopy(body, 0, res, Preamble.Length, body.Length);
            return res;
        }
    }

    public static class TextIO
    {
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, false);

        public static DecodedText Decode(byte[] data)
        {
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                return new DecodedText { Text = Utf8.GetString(data, 3, data.Length - 3), Encoding = Utf8, Preamble = new byte[] { 0xEF, 0xBB, 0xBF } };
            if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
                return new DecodedText { Text = Encoding.Unicode.GetString(data, 2, data.Length - 2), Encoding = new UnicodeEncoding(false, false), Preamble = new byte[] { 0xFF, 0xFE } };
            if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
                return new DecodedText { Text = Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2), Encoding = new UnicodeEncoding(true, false), Preamble = new byte[] { 0xFE, 0xFF } };
            try
            {
                return new DecodedText { Text = StrictUtf8.GetString(data), Encoding = Utf8 };
            }
            catch (DecoderFallbackException)
            {
                var ansi = Encoding.GetEncoding(1252);
                return new DecodedText { Text = ansi.GetString(data), Encoding = ansi };
            }
        }

        public static bool BytesEqual(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>Compares two text files ignoring line ending and trailing whitespace differences.</summary>
        public static bool TextEquivalent(byte[] a, byte[] b)
        {
            if (BytesEqual(a, b)) return true;
            var ta = Decode(a).Text;
            var tb = Decode(b).Text;
            return Normalize(ta) == Normalize(tb);
        }

        static string Normalize(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var line in s.Replace("\r\n", "\n").Split('\n')) sb.Append(line.TrimEnd()).Append('\n');
            return sb.ToString().TrimEnd();
        }

        public static string NormPath(string rel) => rel.Replace('\\', '/').TrimStart('/');

        /// <summary>
        /// Returns the path with the \\?\ prefix so that files deeper than MAX_PATH (260 characters) can be
        /// accessed - mods with deep folder structures inside deep Steam libraries exceed that limit.
        /// </summary>
        public static string LongPath(string p)
        {
            if (string.IsNullOrEmpty(p) || p.StartsWith(@"\\?\")) return p;
            var full = Path.GetFullPath(p);
            return full.StartsWith(@"\\") ? @"\\?\UNC\" + full.Substring(2) : @"\\?\" + full;
        }

        public static string Key(string rel) => NormPath(rel).ToLowerInvariant();

        public static void WriteAllBytesAtomic(string path, byte[] data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, data);
        }
    }
}
