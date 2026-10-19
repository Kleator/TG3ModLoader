using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TG3ModLoader.Merge;
using TG3ModLoader.Script;
using TG3ModLoader.Util;

namespace TG3ModLoader.Core
{
    /// <summary>
    /// Self test: lossless parsing of all vanilla and mod script files, and trial merges of every pair
    /// of installed content mods (plus all of them together) with full change verification.
    /// </summary>
    public static class SelfTest
    {
        static readonly string[] ScriptExt = { ".oc", ".oo", ".inc", ".loo" };

        public static bool Run(GameEnv env, Action<string> log, bool pairwise = true)
        {
            bool ok = true;
            log("=== Self test 1: lossless parsing of all script files ===");
            var builder = new Builder(env, new ToolConfig(), log);
            var vanilla = builder.LoadVanilla();
            var sources = new List<(string Label, string Path, byte[] Data)>();
            foreach (var kv in vanilla) sources.Add(("Vanilla", kv.Value.Path, kv.Value.Data));
            var locDir = Path.Combine(env.GameMediaDir, "localization");
            if (Directory.Exists(locDir))
                foreach (var f in Directory.GetFiles(locDir, "*.loo")) sources.Add(("Game", f, File.ReadAllBytes(f)));

            var lc = LauncherConfig.Load(GameEnv.LauncherModsYml);
            var catalog = ModCatalog.Scan(env, lc);
            foreach (var m in catalog.Where(m => m.Mergeable && m.MediaDir != null))
                foreach (var f in Directory.EnumerateFiles(m.MediaDir, "*", SearchOption.AllDirectories))
                    if (ScriptExt.Contains(Path.GetExtension(f).ToLowerInvariant())) sources.Add((m.Name, f, File.ReadAllBytes(f)));

            int parsed = 0, failedParse = 0, mismatch = 0;
            var parseFailures = new List<string>();
            var seen = new HashSet<string>();
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                foreach (var s in sources)
                {
                    // Identical copies (most mods ship the complete vanilla set) only need to be tested once.
                    if (!seen.Add(Convert.ToBase64String(md5.ComputeHash(s.Data)))) continue;
                    var text = TextIO.Decode(s.Data).Text;
                    try
                    {
                        var root = Parser.Parse(text, s.Label);
                        var back = Out.Of(root).Render();
                        if (back != text) { mismatch++; parseFailures.Add("MISMATCH " + s.Label + ": " + s.Path); }
                        else parsed++;
                    }
                    catch (Exception ex)
                    {
                        failedParse++;
                        parseFailures.Add("NOT PARSEABLE " + s.Label + ": " + s.Path + " - " + ex.Message);
                    }
                }
            }
            log($"  {parsed} distinct files parsed and reproduced byte-for-byte, {failedParse} not structurally parseable (line-based fallback), {mismatch} mismatches.");
            foreach (var f in parseFailures) log("  " + f);
            if (mismatch > 0) ok = false;

            if (!pairwise) return ok;
            var content = catalog.Where(m => m.Mergeable && !m.Missing).ToList();
            log("");
            log("=== Self test 2: trial merges (" + content.Count + " content mods, both orders) ===");
            var combos = new List<List<ModInfo>>();
            for (int i = 0; i < content.Count; i++)
                for (int j = i + 1; j < content.Count; j++)
                {
                    combos.Add(new List<ModInfo> { content[i], content[j] });
                    combos.Add(new List<ModInfo> { content[j], content[i] });
                }
            if (content.Count > 2)
            {
                combos.Add(content);
                var reversed = new List<ModInfo>(content);
                reversed.Reverse();
                combos.Add(reversed);
            }
            foreach (var combo in combos)
            {
                var cfg = new ToolConfig { Mods = combo.Select(m => new ModSelection { Id = m.Id, Name = m.Name, Enabled = true }).ToList() };
                var b = new Builder(env, cfg, _ => { });
                var r = b.Build(true, false);
                string names = string.Join(" + ", combo.Select(m => m.Name));
                if (!r.Success) { ok = false; log("  ERROR  " + names + ": " + r.Error); continue; }
                int merged = r.Files.Count(f => f.Method == "structural" || f.Method == "line");
                int lineMerged = r.Files.Count(f => f.Method == "line");
                log($"  {(r.Violations.Count == 0 ? "OK   " : "ERROR")}  {names}: {merged} files merged ({lineMerged} line-based), {r.Conflicts.Count} real conflicts, {r.Mods.Sum(m => m.CheckedChanges)} changes verified, {r.Violations.Count} verification errors");
                foreach (var v in r.Violations.Take(15)) log("          " + v);
                if (r.Violations.Count > 0) ok = false;
            }
            log("");
            log(ok ? "Self test passed." : "Self test finished with errors.");
            return ok;
        }
    }
}
