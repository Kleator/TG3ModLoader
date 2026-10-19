using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace TG3ModLoader.Core
{
    public enum ModKind { Content, Map, Mixed, Empty }

    /// <summary>A mod found in the Steam Workshop folder or registered as folder mod in the launcher.</summary>
    public sealed class ModInfo
    {
        public string Id;
        public string Name;
        public string Dir;
        public string MediaDir;
        public string MapsDir;
        public bool IsWorkshop;
        public int MediaFiles;
        public int MapFiles;
        public bool InLauncher;
        public bool LauncherEnabled;
        public int LauncherIndex = int.MaxValue;
        public ModKind Kind;
        public bool Missing;

        public bool Mergeable => Kind == ModKind.Content || Kind == ModKind.Mixed;

        public string KindText
        {
            get
            {
                switch (Kind)
                {
                    case ModKind.Content: return "Content/Scripts";
                    case ModKind.Map: return "Map";
                    case ModKind.Mixed: return "Map + Scripts";
                    default: return Missing ? "missing" : "empty";
                }
            }
        }

        public override string ToString() => Name + " (" + Id + ")";
    }

    public static class ModCatalog
    {
        public static List<ModInfo> Scan(GameEnv env, LauncherConfig lc)
        {
            var mods = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
            if (env.WorkshopFound)
            {
                foreach (var dir in Directory.GetDirectories(env.WorkshopDir))
                {
                    var name = Path.GetFileName(dir);
                    if (!name.All(char.IsDigit)) continue;
                    var m = Inspect(dir);
                    m.Id = name;
                    m.Name = name;
                    m.IsWorkshop = true;
                    mods[m.Id] = m;
                }
            }
            var ownOutput = LauncherEntry.NormalizeFolder(env.OutputModDir);
            for (int i = 0; i < lc.Entries.Count; i++)
            {
                var e = lc.Entries[i];
                ModInfo m;
                if (e.IsFolder)
                {
                    if (e.Key == ownOutput) continue;
                    if (!mods.TryGetValue(e.Key, out m))
                    {
                        m = Directory.Exists(e.Name) ? Inspect(e.Name) : new ModInfo { Dir = e.Name, Missing = true, Kind = ModKind.Empty };
                        m.Id = e.Key;
                        mods[m.Id] = m;
                    }
                    m.Name = Path.GetFileName(e.Name.TrimEnd('\\', '/'));
                }
                else
                {
                    var id = e.Id.ToString(CultureInfo.InvariantCulture);
                    if (!mods.TryGetValue(id, out m))
                    {
                        m = new ModInfo { Id = id, Dir = null, Missing = true, Kind = ModKind.Empty, IsWorkshop = true };
                        mods[id] = m;
                    }
                    if (!string.IsNullOrWhiteSpace(e.Name)) m.Name = e.Name;
                }
                m.InLauncher = true;
                m.LauncherEnabled = e.Enabled;
                m.LauncherIndex = Math.Min(m.LauncherIndex, i);
            }
            return mods.Values.OrderBy(m => m.LauncherIndex).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static ModInfo Inspect(string dir)
        {
            var m = new ModInfo { Dir = dir };
            m.MediaDir = FindSub(dir, "media");
            m.MapsDir = FindSub(dir, "maps");
            m.MediaFiles = m.MediaDir != null ? Directory.EnumerateFiles(Util.TextIO.LongPath(m.MediaDir), "*", SearchOption.AllDirectories).Count() : 0;
            m.MapFiles = m.MapsDir != null ? Directory.EnumerateFiles(Util.TextIO.LongPath(m.MapsDir), "*", SearchOption.AllDirectories).Count() : 0;
            if (m.MediaFiles > 0 && m.MapFiles > 0) m.Kind = ModKind.Mixed;
            else if (m.MediaFiles > 0) m.Kind = ModKind.Content;
            else if (m.MapFiles > 0) m.Kind = ModKind.Map;
            else m.Kind = ModKind.Empty;
            return m;
        }

        static string FindSub(string dir, string name)
        {
            var d = Path.Combine(dir, name);
            if (Directory.Exists(d)) return d;
            d = Path.Combine(dir, "content", name);
            return Directory.Exists(d) ? d : null;
        }
    }
}
