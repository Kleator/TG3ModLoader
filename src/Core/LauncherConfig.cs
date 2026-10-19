using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace TG3ModLoader.Core
{
    /// <summary>An entry of the official Mod Launcher list (mods.yml). Folder mods use id 0 and the path as name.</summary>
    public sealed class LauncherEntry
    {
        public string Name;
        public ulong Id;
        public bool Enabled;
        public bool IsFolder => Id == 0;
        public string Key => IsFolder ? NormalizeFolder(Name) : Id.ToString(CultureInfo.InvariantCulture);

        public static string NormalizeFolder(string p)
        {
            try { return Path.GetFullPath(p).TrimEnd('\\', '/').ToLowerInvariant(); }
            catch { return p.ToLowerInvariant(); }
        }
    }

    /// <summary>Reads and writes %LOCALAPPDATA%\Guild3ModLauncher\mods.yml in the format the launcher emits.</summary>
    public sealed class LauncherConfig
    {
        public string CommandLine = "";
        public List<LauncherEntry> Entries = new List<LauncherEntry>();

        public static LauncherConfig Load(string path)
        {
            var cfg = new LauncherConfig();
            if (!File.Exists(path)) return cfg;
            LauncherEntry cur = null;
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                var line = raw.TrimEnd();
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#")) continue;
                bool newItem = line.StartsWith("- ");
                var body = newItem ? line.Substring(2) : line.Trim();
                int colon = body.IndexOf(':');
                if (colon <= 0) continue;
                var key = body.Substring(0, colon).Trim();
                var val = Unquote(body.Substring(colon + 1).Trim());
                if (newItem) cur = null;
                if (key == "cl") { cfg.CommandLine = val; continue; }
                if (cur == null)
                {
                    cur = new LauncherEntry();
                    cfg.Entries.Add(cur);
                }
                switch (key)
                {
                    case "name": cur.Name = val; break;
                    case "id": ulong.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out cur.Id); break;
                    case "enabled": cur.Enabled = val.Equals("true", StringComparison.OrdinalIgnoreCase); break;
                }
            }
            cfg.Entries.RemoveAll(e => e.Name == null);
            return cfg;
        }

        public void Save(string path)
        {
            var sb = new StringBuilder();
            sb.Append("- cl: ").Append(Quote(CommandLine ?? "", true));
            foreach (var e in Entries)
            {
                sb.Append("\r\n- name: ").Append(Quote(e.Name, false));
                sb.Append("\r\n  id: ").Append(e.Id.ToString(CultureInfo.InvariantCulture));
                sb.Append("\r\n  enabled: ").Append(e.Enabled ? "true" : "false");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        public LauncherEntry FindFolder(string folder)
        {
            var k = LauncherEntry.NormalizeFolder(folder);
            return Entries.FirstOrDefault(e => e.IsFolder && e.Key == k);
        }

        public static bool LauncherRunning() => Process.GetProcessesByName("Guild3ModLauncher").Length > 0;

        static string Unquote(string v)
        {
            if (v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"')
            {
                var sb = new StringBuilder();
                for (int i = 1; i < v.Length - 1; i++)
                {
                    char c = v[i];
                    if (c != '\\' || i + 1 >= v.Length - 1) { sb.Append(c); continue; }
                    char e = v[++i];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case '0': sb.Append('\0'); break;
                        case 'x': sb.Append((char)Convert.ToInt32(v.Substring(i + 1, 2), 16)); i += 2; break;
                        case 'u': sb.Append((char)Convert.ToInt32(v.Substring(i + 1, 4), 16)); i += 4; break;
                        default: sb.Append(e); break;
                    }
                }
                return sb.ToString();
            }
            if (v.Length >= 2 && v[0] == '\'' && v[v.Length - 1] == '\'')
                return v.Substring(1, v.Length - 2).Replace("''", "'");
            return v;
        }

        static string Quote(string v, bool alwaysQuoteEmpty)
        {
            if (v.Length == 0) return "\"\"";
            bool plain = !(" -?:,[]{}#&*!|>'\"%@`".IndexOf(v[0]) >= 0) && v.Trim() == v
                         && !v.Contains(": ") && !v.Contains(" #") && !v.EndsWith(":")
                         && v.All(c => c >= 0x20)
                         && !new[] { "true", "false", "null", "yes", "no", "on", "off", "~" }.Contains(v.ToLowerInvariant());
            if (plain) return v;
            var sb = new StringBuilder("\"");
            foreach (char c in v)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\t') sb.Append("\\t");
                else if (c < 0x20) sb.Append("\\x").Append(((int)c).ToString("x2"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
