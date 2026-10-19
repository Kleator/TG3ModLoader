using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using TG3ModLoader.Core;
using TG3ModLoader.Util;

namespace TG3ModLoader
{
    static class Program
    {
        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

        [STAThread]
        static int Main(string[] args)
        {
            var toolDir = AppDomain.CurrentDomain.BaseDirectory;
            if (args.Length == 0)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Gui.MainForm(toolDir));
                return 0;
            }

            AttachConsole(-1);
            try { Console.OutputEncoding = new UTF8Encoding(false); } catch { }
            StreamWriter logFile = null;
            void Log(string s)
            {
                Console.WriteLine(s);
                try { logFile?.WriteLine(s); logFile?.Flush(); } catch { }
            }

            var opts = ParseArgs(args, out var cmd);
            if (opts.TryGetValue("tooldir", out var td) && td.Length > 0) toolDir = td;
            var cfg0 = ToolConfig.Load(Path.Combine(GameEnv.DataDirFor(toolDir), "ModLoader.config.json"));
            var env = GameEnv.Detect(toolDir,
                opts.TryGetValue("gamedir", out var gd) ? gd : cfg0.GameDir,
                opts.TryGetValue("workshopdir", out var wd) ? wd : cfg0.WorkshopDir);
            try { env.EnsureDataDir(); logFile = new StreamWriter(env.LogPath, false, new UTF8Encoding(false)); } catch { }
            Log("TG3 ModLoader " + Builder.ToolVersion + " - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Log("Game:     " + (env.GameDir ?? "(not found)"));
            Log("Workshop: " + (env.WorkshopDir ?? "(not found)"));
            Log("Output:   " + env.OutputModDir);
            if (env.InstalledFromWorkshop) Log("Program:  " + env.ToolDir + " (Steam Workshop item " + env.WorkshopItemId + ")");
            Log("");
            if (!env.GameFound) { Log("ERROR: The Guild 3 was not found. Specify it with --gamedir <path>."); return 2; }

            try
            {
                switch (cmd)
                {
                    case "build": return CmdBuild(env, opts, Log);
                    case "selftest": return SelfTest.Run(env, Log, !opts.ContainsKey("no-pairs")) ? 0 : 1;
                    case "testrun": return CmdTestRun(env, opts, Log);
                    case "status": return CmdStatus(env, Log);
                    case "import-launcher":
                        {
                            var cfg = LoadConfig(env);
                            var cat = ModCatalog.Scan(env, LauncherConfig.Load(GameEnv.LauncherModsYml));
                            int n = cfg.ImportFromLauncher(cat);
                            cfg.Save(env.ConfigPath);
                            Log(n + " mod(s) taken over from the Mod Launcher.");
                            return CmdStatus(env, Log);
                        }
                    case "launch":
                        StartLauncher(env, Log);
                        return 0;
                    case "analyze":
                        return Analyze(env, Log);
                    case "report":
                        {
                            // Writes the report of the last combine again, with the current layout (like the Report button).
                            var data = ReportData.FromManifest(Builder.ReadManifest(env));
                            if (data == null) { Log("Not combined yet - there is no report."); return 1; }
                            var path = opts.TryGetValue("out", out var o) && o.Length > 0 ? o : env.ReportPath;
                            ReportWriter.Write(path, data, env);
                            Log("Report written: " + path);
                            return 0;
                        }
                    case "unregister":
                        {
                            bool ok = new Builder(env, LoadConfig(env), Log).Unregister(out var msg);
                            Log(msg);
                            return ok ? 0 : 1;
                        }
                    case "resolve":
                        {
                            // resolve "<file>::<path>" <modId | =value | vanilla | clear>
                            if (args.Length < 3) { Log("Usage: resolve \"<file>::<path>\" <modId | =value | clear>"); return 1; }
                            var cfg = LoadConfig(env);
                            string id = args[1], decision = args[2];
                            if (decision.Equals("clear", StringComparison.OrdinalIgnoreCase)) cfg.ConflictOverrides.Remove(id);
                            else if (decision.StartsWith(Merge.ConflictPolicy.CustomPrefix) && !Merge.ConflictPolicy.IsValidScalar(decision.Substring(1)))
                            { Log("Not a valid single value: " + decision.Substring(1)); return 1; }
                            else cfg.ConflictOverrides[id] = decision;
                            cfg.Save(env.ConfigPath);
                            Log("Conflict decision saved: " + id + " -> " + decision + " (run 'build' to apply).");
                            return 0;
                        }
                    default:
                        Log("Usage: TG3ModLoader.exe [command] [options]");
                        Log("  (no command)          graphical user interface");
                        Log("  build                 merge the selected mods");
                        Log("      --mods id1,id2    set selection and order (first = highest priority)");
                        Log("      --all             select all content mods");
                        Log("      --import-launcher take over the mods enabled in the Mod Launcher");
                        Log("      --no-launcher     do not modify the Mod Launcher configuration (mods.yml)");
                        Log("      --dry-run         check only, write nothing");
                        Log("      --launch          start the Mod Launcher afterwards");
                        Log("  status                show mods, selection and state");
                        Log("  import-launcher       take over selection and order from the Mod Launcher");
                        Log("  selftest [--no-pairs] parser and merge tests");
                        Log("  testrun [--dir P] [--vanilla] [--timeout S] [--screenshot F]");
                        Log("                        start the game with the mod and check for script errors");
                        Log("  resolve \"<file>::<path>\" <modId | =value | clear>");
                        Log("                        decide a real conflict (winning mod or own value)");
                        Log("  report [--out F]      write the report of the last combine again");
                        Log("  unregister            remove the merged mod from the Mod Launcher again");
                        Log("  launch                start the official Mod Launcher");
                        Log("Global options: --gamedir <path>  --workshopdir <path>");
                        return cmd == "help" ? 0 : 1;
                }
            }
            catch (Exception ex)
            {
                Log("ERROR: " + ex);
                return 3;
            }
            finally { logFile?.Dispose(); }
        }

        static Dictionary<string, string> ParseArgs(string[] args, out string cmd)
        {
            cmd = "help";
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (a.StartsWith("--"))
                {
                    var k = a.Substring(2);
                    string v = "";
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) v = args[++i];
                    d[k] = v;
                }
                else if (i == 0) cmd = a.ToLowerInvariant();
            }
            return d;
        }

        static ToolConfig LoadConfig(GameEnv env)
        {
            var cfg = ToolConfig.Load(env.ConfigPath);
            var cat = ModCatalog.Scan(env, LauncherConfig.Load(GameEnv.LauncherModsYml));
            if (!cfg.Exists) cfg.ImportFromLauncher(cat);
            else cfg.Reconcile(cat);
            return cfg;
        }

        static int CmdBuild(GameEnv env, Dictionary<string, string> opts, Action<string> log)
        {
            var cfg = LoadConfig(env);
            var cat = ModCatalog.Scan(env, LauncherConfig.Load(GameEnv.LauncherModsYml));
            if (opts.ContainsKey("import-launcher")) cfg.ImportFromLauncher(cat);
            if (opts.TryGetValue("mods", out var ids))
            {
                var list = ids.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
                var ordered = new List<ModSelection>();
                foreach (var id in list)
                {
                    var s = cfg.Mods.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                    if (s == null) { log("Unknown mod id: " + id); return 1; }
                    s.Enabled = true;
                    ordered.Add(s);
                }
                foreach (var s in cfg.Mods.Where(m => !ordered.Contains(m))) { s.Enabled = false; ordered.Add(s); }
                cfg.Mods = ordered;
            }
            if (opts.ContainsKey("all"))
                foreach (var s in cfg.Mods) s.Enabled = cat.Any(m => m.Id == s.Id && m.Mergeable && !m.Missing);
            bool dry = opts.ContainsKey("dry-run");
            if (!dry) cfg.Save(env.ConfigPath);

            var b = new Builder(env, cfg, log);
            var r = b.Build(dry, !opts.ContainsKey("no-launcher"));
            foreach (var w in r.Warnings) log("Note: " + w);
            foreach (var v in r.Violations) log("Verification error: " + v);
            if (r.Conflicts.Count > 0)
            {
                log("");
                log("Real conflicts (" + r.Conflicts.Count + "):");
                foreach (var c in r.Conflicts.Take(200))
                    log("  " + c.File + " :: " + c.Path + " -> " + c.WinnerName + "  [" + string.Join(" | ", c.Candidates.Select(x => x.ModName + "=" + x.Text)) + "]");
            }
            if (!dry) log("Report: " + env.ReportPath);
            if (r.Success && opts.ContainsKey("launch")) StartLauncher(env, log);
            return r.Success && r.Violations.Count == 0 ? 0 : 1;
        }

        static int CmdTestRun(GameEnv env, Dictionary<string, string> opts, Action<string> log)
        {
            string dir = opts.ContainsKey("vanilla") ? null : (opts.TryGetValue("dir", out var d) && d.Length > 0 ? d : env.OutputModDir);
            int timeout = opts.TryGetValue("timeout", out var t) && int.TryParse(t, out var ti) ? ti : 600;
            opts.TryGetValue("screenshot", out var shot);
            var res = TestRun.Run(env, dir, timeout, log, string.IsNullOrEmpty(shot) ? null : shot);
            log("Result: " + res.Summary);
            if (!string.IsNullOrWhiteSpace(res.DialogText)) log("Dialog text: " + res.DialogText);
            if (!string.IsNullOrWhiteSpace(res.ScriptsLog))
            {
                log("scripts.log:");
                foreach (var l in res.ScriptsLog.Split('\n').Take(60)) log("  " + l.TrimEnd());
            }
            if (res.ScreenshotPath != null) log("Screenshot: " + res.ScreenshotPath);
            return res.Ok ? 0 : 1;
        }

        static int CmdStatus(GameEnv env, Action<string> log)
        {
            var lc = LauncherConfig.Load(GameEnv.LauncherModsYml);
            var cat = ModCatalog.Scan(env, lc);
            var cfg = LoadConfig(env);
            log("Mods found:");
            foreach (var m in cat)
                log($"  [{m.Id}] {m.Name} - {m.KindText}, {m.MediaFiles} media / {m.MapFiles} map files, launcher: {(m.InLauncher ? (m.LauncherEnabled ? "enabled" : "disabled") : "-")}");
            log("");
            log("ModLoader selection (top = highest priority):");
            int i = 0;
            foreach (var s in cfg.Mods.Where(s => s.Enabled)) log($"  {++i}. {s.Name} [{s.Id}]");
            if (i == 0) log("  (none)");
            var man = Builder.ReadManifest(env);
            var fp = new Builder(env, cfg, null).CurrentFingerprint();
            log("");
            if (man == null) log("Not merged yet.");
            else log("Last merge: " + man.Str("builtAt") + (man.Str("fingerprint") == fp ? " (up to date)" : " (OUTDATED - please merge again!)"));
            var own = lc.FindFolder(env.OutputModDir);
            log("Registered in the Mod Launcher: " + (own == null ? "no" : own.Enabled ? "yes, enabled" : "yes, disabled"));
            return 0;
        }

        /// <summary>Developer statistics: duplicate keys and container kinds in the vanilla scripts.</summary>
        static int Analyze(GameEnv env, Action<string> log)
        {
            var vanilla = new Builder(env, new ToolConfig(), log).LoadVanilla();
            var dupKeys = new Dictionary<string, int>();
            int files = 0;
            void Walk(Script.Node n, string file)
            {
                var c = n.AsContainer;
                if (c == null) return;
                foreach (var ch in c.Children)
                {
                    if (ch.Key != null && ch.Key.Contains("#"))
                    {
                        var k = (c.Kind == Script.NodeKind.List ? c.HeadType : c.Kind.ToString()) + " > " + ch.Key.Substring(0, ch.Key.IndexOf('#'));
                        dupKeys.TryGetValue(k, out int x);
                        dupKeys[k] = x + 1;
                    }
                    Walk(ch, file);
                }
            }
            foreach (var kv in vanilla)
            {
                try { Walk(Script.Parser.Parse(TextIO.Decode(kv.Value.Data).Text, "v"), kv.Value.Path); files++; }
                catch { }
            }
            log(files + " files analysed. Duplicate keys (container > key: extra occurrences):");
            foreach (var kv in dupKeys.OrderByDescending(k => k.Value).Take(60)) log("  " + kv.Value + "  " + kv.Key);
            return 0;
        }

        public static void StartLauncher(GameEnv env, Action<string> log)
        {
            if (!File.Exists(env.LauncherExe)) { log("Mod Launcher not found: " + env.LauncherExe); return; }
            Process.Start(new ProcessStartInfo(env.LauncherExe) { WorkingDirectory = env.ModdingDir, UseShellExecute = true });
            log("Mod Launcher started.");
        }
    }
}
