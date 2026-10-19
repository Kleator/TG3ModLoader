using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace TG3ModLoader.Util
{
    /// <summary>
    /// Reads TG3_Scripts.zip (which is actually a tar archive) or a real zip archive into memory.
    /// </summary>
    public static class Archive
    {
        public static Dictionary<string, (string Path, byte[] Data)> ReadAll(string file, Func<string, bool> filter = null)
        {
            var res = new Dictionary<string, (string, byte[])>(StringComparer.Ordinal);
            using (var fs = File.OpenRead(file))
            {
                var magic = new byte[4];
                int n = fs.Read(magic, 0, 4);
                fs.Position = 0;
                if (n >= 2 && magic[0] == 'P' && magic[1] == 'K') ReadZip(fs, res, filter);
                else ReadTar(fs, res, filter);
            }
            return res;
        }

        static void Add(Dictionary<string, (string, byte[])> res, string name, byte[] data, Func<string, bool> filter)
        {
            name = TextIO.NormPath(name);
            if (name.StartsWith("./")) name = name.Substring(2);
            if (name.Length == 0 || name.EndsWith("/")) return;
            if (filter != null && !filter(name)) return;
            res[TextIO.Key(name)] = (name, data);
        }

        static void ReadZip(Stream s, Dictionary<string, (string, byte[])> res, Func<string, bool> filter)
        {
            using (var zip = new ZipArchive(s, ZipArchiveMode.Read, true))
            {
                foreach (var e in zip.Entries)
                {
                    if (e.FullName.EndsWith("/") || e.FullName.EndsWith("\\")) continue;
                    var name = TextIO.NormPath(e.FullName);
                    if (filter != null && !filter(name)) continue;
                    using (var es = e.Open())
                    using (var ms = new MemoryStream())
                    {
                        es.CopyTo(ms);
                        Add(res, name, ms.ToArray(), null);
                    }
                }
            }
        }

        static void ReadTar(Stream s, Dictionary<string, (string, byte[])> res, Func<string, bool> filter)
        {
            var header = new byte[512];
            string longName = null;
            while (true)
            {
                if (!ReadFull(s, header, 512)) break;
                bool empty = true;
                for (int i = 0; i < 512; i++) if (header[i] != 0) { empty = false; break; }
                if (empty) break;

                string name = CStr(header, 0, 100);
                long size = Octal(header, 124, 12);
                char type = (char)header[156];
                string magic = CStr(header, 257, 6);
                if (magic.StartsWith("ustar"))
                {
                    string prefix = CStr(header, 345, 155);
                    if (prefix.Length > 0) name = prefix + "/" + name;
                }
                var data = new byte[size];
                if (!ReadFull(s, data, (int)size)) throw new InvalidDataException("Tar archive truncated");
                long pad = (512 - size % 512) % 512;
                if (pad > 0) s.Seek(pad, SeekOrigin.Current);

                if (type == 'L') { longName = Encoding.UTF8.GetString(data).TrimEnd('\0'); continue; }
                if (type == 'x' || type == 'g')
                {
                    var pax = ParsePax(data);
                    if (type == 'x' && pax.TryGetValue("path", out var p)) longName = p;
                    continue;
                }
                if (longName != null) { name = longName; longName = null; }
                if (type == '0' || type == '\0' || type == '7') Add(res, name, data, filter);
            }
        }

        static Dictionary<string, string> ParsePax(byte[] data)
        {
            var d = new Dictionary<string, string>();
            var text = Encoding.UTF8.GetString(data);
            int i = 0;
            while (i < text.Length)
            {
                int sp = text.IndexOf(' ', i);
                if (sp < 0) break;
                if (!int.TryParse(text.Substring(i, sp - i), out int len) || len <= 0) break;
                var rec = text.Substring(sp + 1, Math.Max(0, i + len - sp - 2));
                int eq = rec.IndexOf('=');
                if (eq > 0) d[rec.Substring(0, eq)] = rec.Substring(eq + 1);
                i += len;
            }
            return d;
        }

        static bool ReadFull(Stream s, byte[] buf, int count)
        {
            int off = 0;
            while (off < count)
            {
                int r = s.Read(buf, off, count - off);
                if (r <= 0) return false;
                off += r;
            }
            return true;
        }

        static string CStr(byte[] b, int off, int len)
        {
            int end = off;
            while (end < off + len && b[end] != 0) end++;
            return Encoding.UTF8.GetString(b, off, end - off);
        }

        static long Octal(byte[] b, int off, int len)
        {
            if ((b[off] & 0x80) != 0)
            {
                long v = 0;
                for (int i = off + 1; i < off + len; i++) v = (v << 8) | b[i];
                return v;
            }
            var s = CStr(b, off, len).Trim(' ', '\0');
            return s.Length == 0 ? 0 : Convert.ToInt64(s, 8);
        }

        /// <summary>Reads a single entry from a (large) zip archive without loading everything.</summary>
        public static byte[] ReadZipEntry(string zipPath, string entryPath)
        {
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                var key = TextIO.Key(entryPath);
                foreach (var e in zip.Entries)
                {
                    if (TextIO.Key(e.FullName) != key) continue;
                    using (var es = e.Open())
                    using (var ms = new MemoryStream())
                    {
                        es.CopyTo(ms);
                        return ms.ToArray();
                    }
                }
            }
            return null;
        }

        public static HashSet<string> ListZip(string zipPath)
        {
            var res = new HashSet<string>(StringComparer.Ordinal);
            using (var zip = ZipFile.OpenRead(zipPath))
                foreach (var e in zip.Entries) res.Add(TextIO.Key(e.FullName));
            return res;
        }
    }
}
