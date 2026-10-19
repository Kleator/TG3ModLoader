using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using TG3ModLoader.Merge;
using TG3ModLoader.Script;
using TG3ModLoader.Util;

namespace TG3ModLoader.Core
{
    public sealed class FileOutcome
    {
        public string Path;
        /// <summary>vanilla, copy, structural, line, priority, error</summary>
        public string Method;
        public List<string> Mods = new List<string>();
        public int Conflicts;
        public int CheckedChanges;
        public string Note;
    }

    public sealed class ModStats
    {
        public string Id, Name;
        /// <summary>Position in the list (1 = top = highest priority).</summary>
        public int Position;
        public int ChangedFiles, ConflictsWon, ConflictsLost, CheckedChanges, Violations;
        public ModKind Kind;
    }

    public sealed class BuildResult
    {
        public bool Success;
        public bool DryRun;
        public string Error;
        public List<FileOutcome> Files = new List<FileOutcome>();
        public List<Conflict> Conflicts = new List<Conflict>();
        public List<string> Violations = new List<string>();
        public List<string> Warnings = new List<string>();
        public List<ModStats> Mods = new List<ModStats>();
        public int OutputFiles;
        public TimeSpan Duration;
        public string Fingerprint;
        public DateTime BuiltAt;
        public bool LauncherUpdated;
    }

    /// <summary>Builds the merged mod from the selected mods and vanilla.</summary>
    public sealed class Builder
    {
        public const string ToolVersion = "1.1.0";

        static readonly HashSet<string> ScriptExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".oc", ".oo", ".inc", ".loo" };
        static readonly HashSet<string> AdditiveExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".loo" };
        static readonly HashSet<string> TextExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".slate", ".xml", ".txt", ".tss", ".materials", ".ini", ".cfg", ".csv", ".json", ".lua", ".yaml", ".yml", ".html", ".htm", ".hlsl", ".fx"
        };

        readonly GameEnv env;
        readonly ToolConfig cfg;
        readonly Action<string> log;

        static Dictionary<string, (string Path, byte[] Data)> vanillaCache;
        static string vanillaCacheKey;
        HashSet<string> uiZipEntries;

        public Builder(GameEnv env, ToolConfig cfg, Action<string> log)
        {
            this.env = env;
            this.cfg = cfg;
            this.log = log ?? (_ => { });
        }

        sealed class ModInput
        {
            public ModInfo Info;
            public ModVersion V;
            public Dictionary<string, (string Rel, string Full)> Files = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
            public ModStats Stats;
        }

        /// <summary>A mod file; its content is only read when needed (large binary files are copied by path).</summary>
        sealed class Present
        {
            public ModInput Inp;
            public string Full;
            public long Size;
            byte[] data;
            public byte[] Data => data ?? (data = File.ReadAllBytes(Full));
        }

        /// <summary>An output file: either in-memory content or a source file to copy.</summary>
        struct OutEntry
        {
            public string Rel;
            public byte[] Data;
            public string Source;
            public OutEntry(string rel, byte[] data) { Rel = rel; Data = data; Source = null; }
            public OutEntry(string rel, string source) { Rel = rel; Data = null; Source = source; }
            public long Length => Data != null ? Data.LongLength : new FileInfo(Source).Length;
        }

        public Dictionary<string, (string Path, byte[] Data)> LoadVanilla()
        {
            var fi = new FileInfo(env.ScriptsArchive);
            if (!fi.Exists) throw new FileNotFoundException("Vanilla scripts not found: " + env.ScriptsArchive);
            var key = fi.FullName + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            if (vanillaCache == null || vanillaCacheKey != key)
            {
                log("Loading vanilla scripts from " + fi.Name + " ...");
                vanillaCache = Archive.ReadAll(fi.FullName, n => n.StartsWith("media/", StringComparison.OrdinalIgnoreCase));
                vanillaCacheKey = key;
                log("  " + vanillaCache.Count + " vanilla files loaded.");
            }
            return vanillaCache;
        }

        /// <summary>The mod at the top of the list (index 0) gets the highest priority and wins real conflicts.</summary>
        static ModVersion Version(ModInfo m, int index, int count) =>
            new ModVersion { Priority = count - index, Order = index, Id = m.Id, Name = m.Name };

        public List<ModInfo> SelectedMods(List<ModInfo> catalog, List<string> warnings)
        {
            var byId = catalog.ToDictionary(m => m.Id, StringComparer.OrdinalIgnoreCase);
            var res = new List<ModInfo>();
            foreach (var s in cfg.Mods.Where(m => m.Enabled))
            {
                if (!byId.TryGetValue(s.Id, out var m) || m.Missing) { warnings?.Add("Mod not found (not subscribed/downloaded?): " + s.Name + " (" + s.Id + ")"); continue; }
                if (m.Kind == ModKind.Map) { warnings?.Add("'" + m.Name + "' is a map-only mod - enable it directly in the Mod Launcher (maps are not merged)."); continue; }
                if (m.Kind == ModKind.Empty) { warnings?.Add("'" + m.Name + "' contains no files and is skipped."); continue; }
                res.Add(m);
            }
            return res;
        }

        public BuildResult Build(bool dryRun, bool updateLauncher)
        {
            var sw = Stopwatch.StartNew();
            var r = new BuildResult { DryRun = dryRun, BuiltAt = DateTime.Now };
            try
            {
                if (!env.GameFound) throw new InvalidOperationException("Game directory not found.");
                if (!dryRun && GameUsesFolder(env.OutputModDir))
                    throw new InvalidOperationException("The Guild 3 is running with this merged mod. Please close the game first.");

                var lc = LauncherConfig.Load(GameEnv.LauncherModsYml);
                var catalog = ModCatalog.Scan(env, lc);
                var selected = SelectedMods(catalog, r.Warnings);
                var vanilla = LoadVanilla();
                var policy = new ConflictPolicy(cfg.ConflictOverrides);

                log("Mods to merge (top = highest priority):");
                var inputs = new List<ModInput>();
                for (int i = 0; i < selected.Count; i++)
                {
                    var m = selected[i];
                    var inp = new ModInput
                    {
                        Info = m,
                        V = Version(m, i, selected.Count),
                        Stats = new ModStats { Id = m.Id, Name = m.Name, Position = i + 1, Kind = m.Kind }
                    };
                    if (m.MediaDir != null) AddFiles(inp, m.MediaDir, "media/");
                    if (m.MapsDir != null && m.Kind == ModKind.Mixed && cfg.IncludeMapsOfMixedMods) AddFiles(inp, m.MapsDir, "maps/");
                    inputs.Add(inp);
                    r.Mods.Add(inp.Stats);
                    log("  " + (i + 1) + ". " + m.Name + " [" + m.Id + "] - " + inp.Files.Count + " files");
                }
                r.Fingerprint = ComputeFingerprint(inputs);

                // Union of all paths: complete vanilla script set + everything the mods provide.
                var allKeys = new SortedSet<string>(vanilla.Keys, StringComparer.Ordinal);
                foreach (var inp in inputs) foreach (var k in inp.Files.Keys) allKeys.Add(k);

                var output = new Dictionary<string, OutEntry>(StringComparer.Ordinal);
                int done = 0;
                foreach (var key in allKeys)
                {
                    done++;
                    if (done % 500 == 0) log("  ... " + done + "/" + allKeys.Count + " files processed");
                    ProcessFile(key, vanilla, inputs, policy, output, r);
                }

                // Statistics per mod.
                foreach (var c in r.Conflicts)
                {
                    foreach (var inp in inputs)
                    {
                        if (!c.Candidates.Any(x => x.ModName == inp.V.Name)) continue;
                        if (c.WinnerId == inp.V.Id) inp.Stats.ConflictsWon++; else inp.Stats.ConflictsLost++;
                    }
                }
                foreach (var inp in inputs) inp.Stats.Violations = r.Violations.Count(v => v.StartsWith(inp.V.Name + ": ", StringComparison.Ordinal));

                // Launcher-level problems: other content mods enabled in the launcher would collide with our mod.
                foreach (var m in catalog.Where(m => m.LauncherEnabled && m.Mergeable && !selected.Contains(m)))
                    r.Warnings.Add("'" + m.Name + "' is enabled in the Mod Launcher but not merged - the launcher would report a copy conflict. Select it in the ModLoader or disable it in the launcher.");

                // The game itself cannot open paths with 260 or more characters (MAX_PATH).
                int longestRel = output.Count == 0 ? 0 : output.Values.Max(e => e.Rel.Length);
                CheckPathLength(GameEnv.LauncherDeployDir, longestRel, "the Mod Launcher's copy of the mod", r);
                CheckPathLength(env.OutputModDir, longestRel, "test runs", r);

                r.OutputFiles = output.Count;
                if (!dryRun)
                {
                    WriteOutput(output);
                    if (updateLauncher && cfg.UpdateLauncherConfig) r.LauncherUpdated = UpdateLauncher(selected, r);
                    WriteManifest(r, selected);
                }
                r.Success = true;
            }
            catch (Exception ex)
            {
                r.Success = false;
                r.Error = ex.Message;
                log("ERROR: " + ex);
            }
            r.Duration = sw.Elapsed;
            if (!dryRun)
            {
                try { ReportWriter.Write(env.ReportPath, r, env); }
                catch (Exception ex) { log("Could not write the report: " + ex.Message); }
            }
            log(r.Success
                ? $"Done in {r.Duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s: {r.OutputFiles} files, {r.Files.Count(f => f.Method == "structural" || f.Method == "line")} merged from several mods, {r.Conflicts.Count} real conflicts, {r.Violations.Count} verification errors."
                : "Aborted: " + r.Error);
            return r;
        }

        /// <summary>
        /// True if a running game instance was started directly with this folder (-mod &lt;folder&gt;).
        /// A game started through the Mod Launcher uses the launcher's own copy, so merging is safe then.
        /// </summary>
        public static bool GameUsesFolder(string folder)
        {
            if (Process.GetProcessesByName("Guild3").Length == 0) return false;
            try
            {
                var f = folder.TrimEnd('\\').ToLowerInvariant();
                using (var s = new System.Management.ManagementObjectSearcher("SELECT CommandLine FROM Win32_Process WHERE Name = 'Guild3.exe'"))
                    foreach (var o in s.Get())
                    {
                        var cl = (o["CommandLine"] as string ?? "").ToLowerInvariant();
                        if (cl.Contains(f)) return true;
                    }
                return false;
            }
            catch { return true; }
        }

        public const int GameMaxPath = 259;

        static void CheckPathLength(string root, int longestRel, string what, BuildResult r)
        {
            int len = root.TrimEnd('\\').Length + 1 + longestRel;
            if (len > GameMaxPath)
                r.Warnings.Add("The longest file path for " + what + " has " + len + " characters - The Guild 3 cannot open paths with 260 or more characters (" + root + "). Use a shorter folder.");
        }

        static void AddFiles(ModInput inp, string root, string prefix)
        {
            root = TextIO.LongPath(root);
            foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var rel = prefix + TextIO.NormPath(f.Substring(root.Length));
                var name = Path.GetFileName(f);
                if (name.Equals("scripts.log", StringComparison.OrdinalIgnoreCase) || name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase) || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                inp.Files[TextIO.Key(rel)] = (rel, f);
            }
        }

        byte[] GetBase(string key, string rel, Dictionary<string, (string Path, byte[] Data)> vanilla, out bool fromScripts)
        {
            fromScripts = false;
            if (vanilla.TryGetValue(key, out var v)) { fromScripts = true; return v.Data; }
            var loose = Path.Combine(env.GameDir, rel.Replace('/', '\\'));
            if (File.Exists(loose)) return File.ReadAllBytes(loose);
            if (key.StartsWith("media/userinterface/", StringComparison.Ordinal) && File.Exists(env.UiArchive))
            {
                try
                {
                    if (uiZipEntries == null) uiZipEntries = Archive.ListZip(env.UiArchive);
                    var zk = key.Substring("media/".Length);
                    if (uiZipEntries.Contains(zk)) return Archive.ReadZipEntry(env.UiArchive, zk);
                }
                catch (Exception ex) { log("  Note: UI archive not readable: " + ex.Message); uiZipEntries = new HashSet<string>(); }
            }
            return null;
        }

        void ProcessFile(string key, Dictionary<string, (string Path, byte[] Data)> vanilla, List<ModInput> inputs, ConflictPolicy policy,
            Dictionary<string, OutEntry> output, BuildResult r)
        {
            string rel = vanilla.TryGetValue(key, out var vv) ? vv.Path : inputs.Select(i => i.Files.TryGetValue(key, out var f) ? f.Rel : null).First(x => x != null);
            var present = new List<Present>();
            foreach (var inp in inputs)
                if (inp.Files.TryGetValue(key, out var f)) present.Add(new Present { Inp = inp, Full = f.Full, Size = new FileInfo(f.Full).Length });

            bool isMap = key.StartsWith("maps/", StringComparison.Ordinal);
            byte[] baseData = isMap ? null : GetBase(key, rel, vanilla, out _);
            bool inScripts = vanilla.ContainsKey(key);
            string ext = Path.GetExtension(rel);
            bool isText = ScriptExt.Contains(ext) || TextExt.Contains(ext);

            var changed = new List<Present>();
            foreach (var p in present)
            {
                if (baseData != null)
                {
                    if (isText) { if (TextIO.BytesEqual(p.Data, baseData) || TextIO.TextEquivalent(p.Data, baseData)) continue; }
                    else if (p.Size == baseData.LongLength && TextIO.BytesEqual(p.Data, baseData)) continue;
                }
                changed.Add(p);
                p.Inp.Stats.ChangedFiles++;
            }

            if (changed.Count == 0)
            {
                if (inScripts) output[key] = new OutEntry(rel, baseData);
                return;
            }

            var fo = new FileOutcome { Path = rel, Mods = changed.Select(c => c.Inp.V.Name).ToList() };
            r.Files.Add(fo);
            if (changed.Count == 1)
            {
                fo.Method = "copy";
                output[key] = new OutEntry(rel, changed[0].Full);
                if (ScriptExt.Contains(ext)) CheckSyntax(rel, ext, changed[0].Inp.V.Name, changed[0].Data, baseData, fo, r);
                return;
            }

            // Several mods changed the same file.
            bool allSame = isText
                ? changed.Skip(1).All(c => TextIO.BytesEqual(c.Data, changed[0].Data) || TextIO.TextEquivalent(c.Data, changed[0].Data))
                : changed.Skip(1).All(c => c.Size == changed[0].Size && FilesEqual(c.Full, changed[0].Full));
            if (allSame)
            {
                fo.Method = "copy";
                fo.Note = "identical change";
                output[key] = new OutEntry(rel, changed.Last().Full);
                return;
            }

            var pairs = isText ? changed.Select(c => (c.Inp, c.Data)).ToList() : null;
            byte[] merged = null;
            if (ScriptExt.Contains(ext)) merged = MergeScript(rel, ext, baseData, pairs, policy, fo, r);
            if (merged == null && isText && baseData != null) merged = MergeLines(rel, baseData, pairs, policy, fo, r);
            if (merged != null)
            {
                output[key] = new OutEntry(rel, merged);
                return;
            }
            var winner = policy.Pick(rel, "(file)", changed.Select(c => c.Inp.V), out bool byOverride);
            var conflict = new Conflict { File = rel, Path = "(file)", Kind = "file", WinnerName = winner.Name, WinnerId = winner.Id, ByOverride = byOverride };
            foreach (var ch in changed) conflict.Candidates.Add((ch.Inp.V.Name, ch.Size + " bytes"));
            r.Conflicts.Add(conflict);
            fo.Method = "priority";
            fo.Conflicts = 1;
            output[key] = new OutEntry(rel, changed.Last(x => x.Inp.V == winner).Full);
        }

        static bool FilesEqual(string a, string b)
        {
            using (var fa = File.OpenRead(a))
            using (var fb = File.OpenRead(b))
            {
                if (fa.Length != fb.Length) return false;
                var ba = new byte[1 << 16];
                var bb = new byte[1 << 16];
                while (true)
                {
                    int na = fa.Read(ba, 0, ba.Length);
                    int nb = fb.Read(bb, 0, na);
                    while (nb < na) { int k = fb.Read(bb, nb, na - nb); if (k <= 0) return false; nb += k; }
                    if (na == 0) return true;
                    for (int i = 0; i < na; i++) if (ba[i] != bb[i]) return false;
                }
            }
        }

        /// <summary>Warns about syntax errors in files a mod ships (they would also break the mod on its own).</summary>
        void CheckSyntax(string rel, string ext, string modName, byte[] data, byte[] baseData, FileOutcome fo, BuildResult r)
        {
            if (!ScriptExt.Contains(ext)) return;
            try { Parser.Parse(TextIO.Decode(data).Text, modName); }
            catch (Exception ex) when (ex is ParseException || ex is LexerException)
            {
                bool baseOk = true;
                if (baseData != null)
                {
                    try { Parser.Parse(TextIO.Decode(baseData).Text, "Vanilla"); } catch { baseOk = false; }
                }
                if (!baseOk) return;
                fo.Note = "syntax error in the mod: " + ex.Message;
                r.Warnings.Add("'" + modName + "' contains a syntax error in " + rel + ": " + ex.Message +
                               " - this comes from the mod itself (not from the merge). The game may tolerate it, but this part of the mod may not work as intended. The file is taken over unchanged.");
            }
        }

        byte[] MergeScript(string rel, string ext, byte[] baseData, List<(ModInput Inp, byte[] Data)> changed, ConflictPolicy policy, FileOutcome fo, BuildResult r)
        {
            bool additive = AdditiveExt.Contains(ext);
            DecodedText baseDec = baseData != null ? TextIO.Decode(baseData) : null;
            Node baseRoot;
            var vns = new List<VN>();
            try
            {
                baseRoot = Parser.Parse(baseDec?.Text ?? "", "Vanilla");
                foreach (var c in changed) vns.Add(new VN(c.Inp.V, Parser.Parse(TextIO.Decode(c.Data).Text, c.Inp.V.Name)));
            }
            catch (Exception ex) when (ex is ParseException || ex is LexerException)
            {
                fo.Note = "structural parsing not possible (" + ex.Message + "), merged line by line";
                log("  " + rel + ": " + fo.Note);
                return null;
            }

            var merger = new TreeMerger(rel, policy, additive);
            string text = merger.Merge(baseRoot, vns);
            Node mergedRoot;
            try { mergedRoot = Parser.Parse(text, "Result"); }
            catch (Exception ex)
            {
                r.Warnings.Add(rel + ": result of the structural merge is not readable (" + ex.Message + ") - using the line-based fallback.");
                return null;
            }

            var verifier = new Verifier(rel, merger.Conflicts, additive);
            for (int i = 0; i < vns.Count; i++)
            {
                int before = verifier.CheckedChanges;
                verifier.Check(baseRoot, vns[i].V, vns[i].N, mergedRoot);
                changed[i].Inp.Stats.CheckedChanges += verifier.CheckedChanges - before;
            }
            r.Violations.AddRange(verifier.Violations);
            fo.CheckedChanges = verifier.CheckedChanges;

            r.Conflicts.AddRange(merger.Conflicts);
            fo.Conflicts = merger.Conflicts.Count;
            fo.Method = "structural";
            var enc = baseDec ?? TextIO.Decode(changed[0].Data);
            return enc.Encode(text);
        }

        byte[] MergeLines(string rel, byte[] baseData, List<(ModInput Inp, byte[] Data)> changed, ConflictPolicy policy, FileOutcome fo, BuildResult r)
        {
            var baseDec = TextIO.Decode(baseData);
            var conflicts = new List<Conflict>();
            var text = LineMerger.Merge(rel, baseDec.Text, changed.Select(c => (c.Inp.V, TextIO.Decode(c.Data).Text)).ToList(), policy, conflicts);
            r.Conflicts.AddRange(conflicts);
            fo.Conflicts = conflicts.Count;
            fo.Method = "line";
            return baseDec.Encode(text);
        }

        /// <summary>
        /// Writes the output incrementally: unchanged files are left untouched (keeps timestamps and thus the
        /// mod id the game derives - savegames are stored per mod id), obsolete files are removed.
        /// </summary>
        void WriteOutput(Dictionary<string, OutEntry> output)
        {
            var root = TextIO.LongPath(env.OutputModDir);
            Directory.CreateDirectory(root);
            int written = 0, unchanged = 0, deleted = 0;
            foreach (var kv in output)
            {
                var e = kv.Value;
                var path = Path.Combine(root, e.Rel.Replace('/', '\\'));
                if (File.Exists(path) && new FileInfo(path).Length == e.Length)
                {
                    bool same = e.Data != null ? TextIO.BytesEqual(File.ReadAllBytes(path), e.Data) : FilesEqual(path, e.Source);
                    if (same) { unchanged++; continue; }
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (e.Data != null) File.WriteAllBytes(path, e.Data);
                else File.Copy(e.Source, path, true);
                written++;
            }
            foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                var rel = TextIO.NormPath(f.Substring(root.Length));
                if (output.ContainsKey(TextIO.Key(rel))) continue;
                File.Delete(f);
                deleted++;
            }
            foreach (var d in Directory.GetDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d);
            log($"Output mod updated: {written} written, {unchanged} unchanged, {deleted} removed ({env.OutputModDir}).");
        }

        bool UpdateLauncher(List<ModInfo> selected, BuildResult r)
        {
            if (LauncherConfig.LauncherRunning())
            {
                r.Warnings.Add("The Mod Launcher is running - mods.yml was not updated. Close the launcher and merge again (or disable the merged mods in the launcher manually).");
                return false;
            }
            var path = GameEnv.LauncherModsYml;
            var lc = LauncherConfig.Load(path);
            var backup = path + ".modloader_backup";
            if (File.Exists(path) && !File.Exists(backup)) File.Copy(path, backup);

            var own = lc.FindFolder(env.OutputModDir);
            if (own == null)
            {
                own = new LauncherEntry { Name = env.OutputModDir, Id = 0 };
                lc.Entries.Insert(0, own);
            }
            own.Enabled = selected.Count > 0;
            foreach (var m in selected)
            {
                foreach (var e in lc.Entries)
                {
                    if (e == own) continue;
                    if (e.Key.Equals(m.Id, StringComparison.OrdinalIgnoreCase) && e.Enabled)
                    {
                        e.Enabled = false;
                        log("  Disabled in the Mod Launcher (now loaded through the ModLoader): " + m.Name);
                    }
                }
            }
            lc.Save(path);
            log("Mod Launcher configuration (mods.yml) updated - ModLoader " + (own.Enabled ? "enabled" : "disabled") + ".");
            return true;
        }

        /// <summary>
        /// Removes the merged mod from the official Mod Launcher and re-enables the mods it contained
        /// (only the first one if several script mods were merged - the launcher cannot load more).
        /// </summary>
        public bool Unregister(out string message)
        {
            if (LauncherConfig.LauncherRunning()) { message = "The Mod Launcher is running - please close it first."; return false; }
            var path = GameEnv.LauncherModsYml;
            var lc = LauncherConfig.Load(path);
            var own = lc.FindFolder(env.OutputModDir);
            if (own == null) { message = "The ModLoader is not registered in the Mod Launcher."; return true; }
            lc.Entries.Remove(own);
            var catalog = ModCatalog.Scan(env, lc);
            var selected = SelectedMods(catalog, null);
            var reenabled = new List<string>();
            bool scriptModEnabled = catalog.Any(m => m.LauncherEnabled && m.Mergeable);
            foreach (var m in selected)
            {
                var e = lc.Entries.FirstOrDefault(x => x.Key.Equals(m.Id, StringComparison.OrdinalIgnoreCase));
                if (e == null || e.Enabled) continue;
                if (m.Mergeable && scriptModEnabled) continue;
                e.Enabled = true;
                reenabled.Add(m.Name);
                if (m.Mergeable) scriptModEnabled = true;
            }
            lc.Save(path);
            message = "ModLoader removed from the Mod Launcher." + (reenabled.Count > 0 ? " Re-enabled: " + string.Join(", ", reenabled) + "." : "");
            return true;
        }

        string ComputeFingerprint(List<ModInput> inputs)
        {
            var sb = new StringBuilder();
            sb.Append(ToolVersion).Append('|');
            var vf = new FileInfo(env.ScriptsArchive);
            sb.Append(vf.Exists ? vf.Length + ":" + vf.LastWriteTimeUtc.Ticks : "-").Append('|');
            sb.Append(cfg.IncludeMapsOfMixedMods).Append('|');
            foreach (var kv in cfg.ConflictOverrides.OrderBy(k => k.Key, StringComparer.Ordinal)) sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
            foreach (var inp in inputs)
            {
                sb.Append('#').Append(inp.V.Id).Append(':');
                foreach (var kv in inp.Files.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    var fi = new FileInfo(kv.Value.Full);
                    sb.Append(kv.Key).Append(',').Append(fi.Length).Append(',').Append(fi.LastWriteTimeUtc.Ticks).Append(';');
                }
            }
            using (var sha = SHA1.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>Fingerprint of the current inputs, used to detect whether a rebuild is required.</summary>
        public string CurrentFingerprint()
        {
            var lc = LauncherConfig.Load(GameEnv.LauncherModsYml);
            var catalog = ModCatalog.Scan(env, lc);
            var selected = SelectedMods(catalog, null);
            var inputs = new List<ModInput>();
            for (int i = 0; i < selected.Count; i++)
            {
                var m = selected[i];
                var inp = new ModInput { Info = m, V = Version(m, i, selected.Count) };
                if (m.MediaDir != null) AddFiles(inp, m.MediaDir, "media/");
                if (m.MapsDir != null && m.Kind == ModKind.Mixed && cfg.IncludeMapsOfMixedMods) AddFiles(inp, m.MapsDir, "maps/");
                inputs.Add(inp);
            }
            return ComputeFingerprint(inputs);
        }

        void WriteManifest(BuildResult r, List<ModInfo> selected)
        {
            var d = new Dictionary<string, object>
            {
                ["toolVersion"] = ToolVersion,
                ["builtAt"] = r.BuiltAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                ["fingerprint"] = r.Fingerprint,
                ["outputDir"] = env.OutputModDir,
                ["outputFiles"] = r.OutputFiles,
                ["mergedFiles"] = r.Files.Count(f => f.Method == "structural" || f.Method == "line"),
                ["conflicts"] = r.Conflicts.Count,
                ["violations"] = r.Violations.Count,
                ["mods"] = selected.Select((m, i) => (object)new Dictionary<string, object> { ["position"] = i + 1, ["id"] = m.Id, ["name"] = m.Name }).ToList(),
                ["warnings"] = r.Warnings.Cast<object>().ToList(),
                ["conflictList"] = r.Conflicts.Select(c => (object)new Dictionary<string, object>
                {
                    ["file"] = c.File, ["path"] = c.Path, ["kind"] = c.Kind, ["winner"] = c.WinnerName, ["winnerId"] = c.WinnerId, ["byOverride"] = c.ByOverride,
                    ["candidates"] = c.Candidates.Select(x => (object)new Dictionary<string, object> { ["mod"] = x.ModName, ["text"] = x.Text }).ToList()
                }).ToList()
            };
            File.WriteAllText(env.ManifestPath, Json.Write(d), new UTF8Encoding(false));
        }

        /// <summary>Restores the conflict list of the last build from the manifest.</summary>
        public static List<Conflict> ConflictsFromManifest(Dictionary<string, object> man)
        {
            var res = new List<Conflict>();
            if (man == null || !man.TryGetValue("conflictList", out var o) || !(o is List<object> list)) return res;
            foreach (var d in list.OfType<Dictionary<string, object>>())
            {
                var c = new Conflict { File = d.Str("file"), Path = d.Str("path"), Kind = d.Str("kind"), WinnerName = d.Str("winner"), WinnerId = d.Str("winnerId"), ByOverride = d.Bool("byOverride", false) };
                if (d.TryGetValue("candidates", out var co) && co is List<object> cl)
                    foreach (var cd in cl.OfType<Dictionary<string, object>>()) c.Candidates.Add((cd.Str("mod"), cd.Str("text")));
                res.Add(c);
            }
            return res;
        }

        public static Dictionary<string, object> ReadManifest(GameEnv env)
        {
            try { return File.Exists(env.ManifestPath) ? Json.Parse(File.ReadAllText(env.ManifestPath)) as Dictionary<string, object> : null; }
            catch { return null; }
        }
    }
}
