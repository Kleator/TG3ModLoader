using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TG3ModLoader.Util;

namespace TG3ModLoader.Core
{
    public sealed class ModSelection
    {
        public string Id;
        public string Name;
        public bool Enabled;
    }

    /// <summary>ModLoader.config.json: which mods are merged, in which order, and manual conflict decisions.</summary>
    public sealed class ToolConfig
    {
        public string GameDir;
        public string WorkshopDir;
        public bool UpdateLauncherConfig = true;
        public bool IncludeMapsOfMixedMods = true;
        /// <summary>Priority order: the first entry has the highest priority and wins real conflicts.</summary>
        public List<ModSelection> Mods = new List<ModSelection>();
        /// <summary>"file::path" -> mod id that should win this conflict.</summary>
        public Dictionary<string, string> ConflictOverrides = new Dictionary<string, string>(StringComparer.Ordinal);
        public bool Exists;

        public static ToolConfig Load(string path)
        {
            var c = new ToolConfig();
            if (!File.Exists(path)) return c;
            var d = Json.Parse(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
            if (d == null) return c;
            c.Exists = true;
            c.GameDir = d.Str("gameDir");
            c.WorkshopDir = d.Str("workshopDir");
            c.UpdateLauncherConfig = d.Bool("updateLauncherConfig", true);
            c.IncludeMapsOfMixedMods = d.Bool("includeMapsOfMixedMods", true);
            if (d.TryGetValue("mods", out var mv) && mv is List<object> ml)
                foreach (var o in ml.OfType<Dictionary<string, object>>())
                {
                    var id = o.Str("id");
                    if (string.IsNullOrEmpty(id) || c.Mods.Any(x => x.Id == id)) continue;
                    c.Mods.Add(new ModSelection { Id = id, Name = o.Str("name", id), Enabled = o.Bool("enabled", false) });
                }
            if (d.TryGetValue("conflictOverrides", out var ov) && ov is Dictionary<string, object> od)
                foreach (var kv in od) if (kv.Value != null) c.ConflictOverrides[kv.Key] = kv.Value.ToString();
            return c;
        }

        public void Save(string path)
        {
            var d = new Dictionary<string, object>
            {
                ["_info"] = "TG3 ModLoader configuration. Mod order: the first entry has the highest priority and wins real conflicts.",
                ["gameDir"] = GameDir,
                ["workshopDir"] = WorkshopDir,
                ["updateLauncherConfig"] = UpdateLauncherConfig,
                ["includeMapsOfMixedMods"] = IncludeMapsOfMixedMods,
                ["mods"] = Mods.Select(m => (object)new Dictionary<string, object> { ["id"] = m.Id, ["name"] = m.Name, ["enabled"] = m.Enabled }).ToList(),
                ["conflictOverrides"] = ConflictOverrides.ToDictionary(kv => kv.Key, kv => (object)kv.Value)
            };
            File.WriteAllText(path, Json.Write(d), new UTF8Encoding(false));
            Exists = true;
        }

        /// <summary>Adds newly found mods (disabled) and keeps the stored order for known ones.</summary>
        public void Reconcile(List<ModInfo> catalog)
        {
            var known = new HashSet<string>(Mods.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);
            foreach (var m in catalog)
            {
                if (known.Contains(m.Id))
                {
                    var sel = Mods.First(x => x.Id.Equals(m.Id, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrEmpty(m.Name) && m.Name != m.Id) sel.Name = m.Name;
                    continue;
                }
                Mods.Add(new ModSelection { Id = m.Id, Name = m.Name, Enabled = false });
            }
        }

        /// <summary>
        /// Takes over the selection and order of the official Mod Launcher: every content mod enabled
        /// there is selected for merging, and the launcher order is applied.
        /// </summary>
        public int ImportFromLauncher(List<ModInfo> catalog)
        {
            Reconcile(catalog);
            int added = 0;
            var byId = catalog.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var s in Mods)
            {
                if (!byId.TryGetValue(s.Id, out var m)) continue;
                if (m.LauncherEnabled && m.Mergeable && !s.Enabled) { s.Enabled = true; added++; }
            }
            var order = Mods.Select((s, i) => (s, i)).OrderBy(x => byId.TryGetValue(x.s.Id, out var m) ? m.LauncherIndex : int.MaxValue).ThenBy(x => x.i).Select(x => x.s).ToList();
            Mods = order;
            return added;
        }
    }
}
