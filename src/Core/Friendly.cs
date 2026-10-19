using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TG3ModLoader.Core
{
    /// <summary>Turns technical script paths, file names and values into wording for players.</summary>
    public static class Friendly
    {
        /// <summary>"MaxItemNormalStock" -> "Max Item Normal Stock", "NPCWealth_Money" -> "NPC Wealth Money".</summary>
        public static string Humanize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Replace('_', ' ').Trim();
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (i > 0 && c != ' ' && s[i - 1] != ' ')
                {
                    char p = s[i - 1];
                    bool next = i + 1 < s.Length && char.IsLower(s[i + 1]);
                    if (char.IsUpper(c) && (char.IsLower(p) || (char.IsUpper(p) && next) || char.IsDigit(p))) sb.Append(' ');
                    else if (char.IsDigit(c) && char.IsLetter(p)) sb.Append(' ');
                }
                sb.Append(c);
            }
            var r = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
            return r.Length > 0 ? char.ToUpper(r[0], CultureInfo.InvariantCulture) + r.Substring(1) : r;
        }

        static string StripTechnical(string name)
        {
            if (name.StartsWith("G3") && name.Length > 2 && char.IsUpper(name[2])) name = name.Substring(2);
            if (name.EndsWith("Default") && name.Length > 7) name = name.Substring(0, name.Length - 7);
            return name;
        }

        /// <summary>Splits a conflict path at '/' outside of quotes.</summary>
        static List<string> Segments(string path)
        {
            var res = new List<string>();
            var sb = new StringBuilder();
            bool quote = false;
            foreach (char c in path ?? "")
            {
                if (c == '"') quote = !quote;
                if (c == '/' && !quote)
                {
                    if (sb.Length > 0) res.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }
                sb.Append(c);
            }
            if (sb.Length > 0) res.Add(sb.ToString());
            return res;
        }

        static readonly Regex Identity = new Regex(@"^(?<name>[^\[]*)\[(?<prop>[A-Za-z]+)=(?<val>.*)\]$");
        static readonly Regex Index = new Regex(@"^(?<name>[^\[]*)\[(?<i>\d+)\]$");

        static string Segment(string seg, out bool isIndex)
        {
            isIndex = false;
            seg = seg.Trim();
            if (seg == "(file)") return "Whole file";
            int ext = seg.IndexOf(" extends ", StringComparison.Ordinal);
            if (ext > 0) return Humanize(StripTechnical(seg.Substring(0, ext)));
            var m = Identity.Match(seg);
            if (m.Success)
            {
                var name = m.Groups["name"].Value;
                var val = m.Groups["val"].Value.Trim('"');
                if (name == "LocDirectEntry") return "Text " + val;
                return (name.Length > 0 ? Humanize(name) + " " : "") + "“" + val + "”";
            }
            m = Index.Match(seg);
            if (m.Success)
            {
                int i = int.Parse(m.Groups["i"].Value, CultureInfo.InvariantCulture) + 1;
                var name = m.Groups["name"].Value;
                if (name.Length == 0 || name == "tuple" || name == "list" || name == "array") { isIndex = true; return "entry " + i; }
                return Humanize(name) + " " + i;
            }
            if (seg.StartsWith("\"") && seg.EndsWith("\"") && seg.Length > 1) return seg.Substring(1, seg.Length - 2);
            if (seg.StartsWith("@")) return "include " + seg.Substring(seg.LastIndexOf('/') + 1);
            if (seg.All(char.IsDigit)) { isIndex = true; return "group " + seg; }
            if (seg.StartsWith("lines ", StringComparison.Ordinal)) return "Lines " + seg.Substring(6);
            return Humanize(seg);
        }

        /// <summary>A short title for the setting and where it sits ("Difficulty Settings › Easy").</summary>
        public static (string Title, string Context) Setting(string path)
        {
            var segs = Segments(path);
            if (segs.Count == 0) return ("Whole file", "");
            var parts = new List<(string Text, bool Index)>();
            foreach (var s in segs)
            {
                var text = Segment(s, out bool idx);
                // A position inside a list entry: "Item Overrides, entry 1, value 4" rather than "entry 1, entry 4".
                if (idx && text.StartsWith("entry ", StringComparison.Ordinal) && parts.Count > 0 &&
                    parts[parts.Count - 1].Index && parts[parts.Count - 1].Text.StartsWith("entry ", StringComparison.Ordinal))
                    text = "value " + text.Substring(6);
                parts.Add((text, idx));
            }
            // The first segment is the script object (it is already shown as the area).
            if (segs[0].Contains(" extends ") && parts.Count > 1) parts.RemoveAt(0);
            int last = parts.Count - 1;
            string title = parts[last].Text;
            int ctxEnd = last;
            while (ctxEnd > 0 && parts[ctxEnd].Index)
            {
                ctxEnd--;
                title = parts[ctxEnd].Text + ", " + title;
            }
            var context = string.Join(" › ", parts.Take(ctxEnd).Select(p => p.Text));
            return (title, context);
        }

        static readonly Dictionary<string, string> Languages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["chinese"] = "Chinese", ["english"] = "English", ["french"] = "French", ["german"] = "German", ["hungarian"] = "Hungarian",
            ["italian"] = "Italian", ["japanese"] = "Japanese", ["korean"] = "Korean", ["polish"] = "Polish", ["russian"] = "Russian", ["spanish"] = "Spanish"
        };

        /// <summary>The game area a file belongs to ("Gameplay", "Buildings", "Texts (German)").</summary>
        public static string Area(string file)
        {
            if (string.IsNullOrEmpty(file)) return "";
            var f = file.Replace('\\', '/').ToLowerInvariant();
            var name = Path.GetFileNameWithoutExtension(file);
            if (f.Contains("/localization/"))
            {
                var lang = name.StartsWith("locdirect_", StringComparison.OrdinalIgnoreCase) ? name.Substring(10) : name;
                return "Texts (" + (Languages.TryGetValue(lang, out var l) ? l : Humanize(lang)) + ")";
            }
            if (f.StartsWith("maps/")) return "Maps";
            if (f.Contains("/userinterface/")) return "User interface";
            if (f.Contains("/storagekeeperrules/")) return "Storage rules";
            if (f.Contains("/buildinglevels/") || f.Contains("buildingtemplates") || f.Contains("buildingaddons")) return "Buildings";
            if (f.Contains("unlockbuilds")) return "Unlocks";
            if (f.Contains("/actions/") || f.Contains("g3actionsdefault")) return "Actions";
            if (f.Contains("/items/") || f.Contains("g3itemsdefault")) return "Items";
            if (f.Contains("/g3characters") || f.Contains("characters")) return "Characters";
            if (f.Contains("/politicalpowers/")) return "Politics";
            if (f.Contains("/gameevents/")) return "Events";
            if (f.Contains("/dialogue/")) return "Dialogues";
            if (f.Contains("gameplaydefault")) return "Gameplay";
            if (f.Contains("aiconstvalues")) return "Computer opponents";
            if (f.Contains("productionsettings")) return "Production";
            if (f.Contains("/sounds") || f.Contains("/sfx/")) return "Sound";
            return Humanize(StripTechnical(name));
        }

        /// <summary>A value as players read it ("0.80f" -> "0.80", "\"Linear\"" -> "Linear").</summary>
        public static string Value(string text)
        {
            if (text == null) return "";
            var t = text.Trim();
            if (t == "(removed)") return "removed";
            int eq = t.IndexOf(" = ", StringComparison.Ordinal);
            if (eq > 0 && eq < 60 && !t.StartsWith("(") && !t.StartsWith("\"")) t = t.Substring(eq + 3).Trim();
            if (t.Length > 1 && t.StartsWith("\"") && t.EndsWith("\"")) t = t.Substring(1, t.Length - 2);
            if (Regex.IsMatch(t, @"^-?\d+(\.\d+)?[fF]$")) t = t.Substring(0, t.Length - 1);
            if (t.StartsWith("array", StringComparison.Ordinal) || t.StartsWith("map", StringComparison.Ordinal)) return "a list of values";
            if (t.EndsWith(" ...")) t = t.Substring(0, t.Length - 4) + "…";
            return t.Length > 60 ? t.Substring(0, 57) + "…" : t;
        }

        public static string Kind(ModKind k)
        {
            switch (k)
            {
                case ModKind.Content: return "Gameplay";
                case ModKind.Map: return "Map";
                case ModKind.Mixed: return "Map + gameplay";
                default: return "Empty";
            }
        }

        public static string Method(string m)
        {
            switch (m)
            {
                case "structural": return "Combined";
                case "line": return "Combined line by line";
                case "priority": return "One mod's version used";
                default: return "Taken from the mod";
            }
        }

        /// <summary>Rewrites known technical notes for players.</summary>
        public static string Warning(string w)
        {
            if (w == null) return "";
            var m = Regex.Match(w, @"^'(?<mod>[^']+)' contains a syntax error in (?<file>\S+):");
            if (m.Success)
                return m.Groups["mod"].Value + " contains a small error in one of its own files (" + Area(m.Groups["file"].Value) + "). " +
                       "This comes from the mod itself, not from combining. The game usually tolerates it, but this part of the mod may not work as intended.";
            m = Regex.Match(w, @"^'(?<mod>[^']+)' is enabled in the Mod Launcher but not merged");
            if (m.Success)
                return m.Groups["mod"].Value + " is switched on in the official Mod Launcher but not ticked here. Tick it here (recommended) or switch it off in the Mod Launcher - otherwise the Mod Launcher reports a conflict.";
            m = Regex.Match(w, @"^'(?<mod>[^']+)' is a map-only mod");
            if (m.Success) return m.Groups["mod"].Value + " is a map - switch it on in the official Mod Launcher, maps do not need combining.";
            if (w.StartsWith("The Mod Launcher is running", StringComparison.Ordinal))
                return "The official Mod Launcher was open, so it could not be prepared. Close it and combine again.";
            if (w.StartsWith("The longest file path", StringComparison.Ordinal))
                return "Some file paths are too long for the game (260 characters or more). Move the game or the ModLoader to a shorter folder.";
            if (w.StartsWith("Mod not found", StringComparison.Ordinal))
                return w.Replace("Mod not found (not subscribed/downloaded?): ", "A selected mod is missing - is it still subscribed in the Steam Workshop? ");
            return w;
        }
    }
}
