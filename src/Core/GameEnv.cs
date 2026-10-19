using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TG3ModLoader.Core
{
    /// <summary>Locates the game, the Steam Workshop folder, the official Mod Launcher and our own folders.</summary>
    public sealed class GameEnv
    {
        public const string AppId = "311260";
        public const string OutputFolderName = "MergedMod";

        public string ToolDir;
        public string GameDir;
        public string WorkshopDir;

        public string OutputModDir => Path.Combine(ToolDir, OutputFolderName);
        public string ConfigPath => Path.Combine(ToolDir, "ModLoader.config.json");
        public string ManifestPath => Path.Combine(ToolDir, "build_manifest.json");
        public string ReportPath => Path.Combine(ToolDir, "ModLoader_Report.html");
        public string LogPath => Path.Combine(ToolDir, "ModLoader.log");
        public string GameExe => Path.Combine(GameDir, "Guild3.exe");
        public string ModdingDir => Path.Combine(GameDir, "Modding");
        public string LauncherExe => Path.Combine(ModdingDir, "Guild3ModLauncher.exe");
        public string ScriptsArchive => Path.Combine(ModdingDir, "TG3_Scripts.zip");
        public string UiArchive => Path.Combine(ModdingDir, "TG3_UserInterface.zip");
        public string GameMediaDir => Path.Combine(GameDir, "media");
        public static string LauncherDataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Guild3ModLauncher");
        public static string LauncherModsYml => Path.Combine(LauncherDataDir, "mods.yml");
        public static string LauncherDeployDir => Path.Combine(LauncherDataDir, "mod");

        public bool GameFound => GameDir != null && File.Exists(GameExe);
        public bool WorkshopFound => WorkshopDir != null && Directory.Exists(WorkshopDir);

        public static GameEnv Detect(string toolDir, string gameDirOverride = null, string workshopOverride = null)
        {
            var env = new GameEnv { ToolDir = Path.GetFullPath(toolDir) };

            if (!string.IsNullOrEmpty(workshopOverride) && Directory.Exists(workshopOverride)) env.WorkshopDir = Path.GetFullPath(workshopOverride);
            else
            {
                // Normal installation: <steamapps>\workshop\content\311260\TG3ModLoader\TG3ModLoader.exe
                var parent = Directory.GetParent(env.ToolDir);
                if (parent != null && parent.Name == AppId && parent.Parent != null && parent.Parent.Name.Equals("content", StringComparison.OrdinalIgnoreCase))
                    env.WorkshopDir = parent.FullName;
            }

            if (!string.IsNullOrEmpty(gameDirOverride) && File.Exists(Path.Combine(gameDirOverride, "Guild3.exe"))) env.GameDir = Path.GetFullPath(gameDirOverride);
            if (env.GameDir == null && env.WorkshopDir != null)
            {
                var steamapps = Directory.GetParent(env.WorkshopDir)?.Parent?.Parent;
                if (steamapps != null)
                {
                    var g = Path.Combine(steamapps.FullName, "common", "The Guild 3");
                    if (File.Exists(Path.Combine(g, "Guild3.exe"))) env.GameDir = g;
                }
            }
            if (env.GameDir == null || env.WorkshopDir == null)
            {
                foreach (var lib in SteamLibraries())
                {
                    var g = Path.Combine(lib, "steamapps", "common", "The Guild 3");
                    if (env.GameDir == null && File.Exists(Path.Combine(g, "Guild3.exe"))) env.GameDir = g;
                    var w = Path.Combine(lib, "steamapps", "workshop", "content", AppId);
                    if (env.WorkshopDir == null && Directory.Exists(w) && env.GameDir != null && env.GameDir.StartsWith(lib, StringComparison.OrdinalIgnoreCase))
                        env.WorkshopDir = w;
                }
            }
            return env;
        }

        static IEnumerable<string> SteamLibraries()
        {
            var roots = new List<string>();
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    var p = k?.GetValue("SteamPath") as string;
                    if (!string.IsNullOrEmpty(p)) roots.Add(p.Replace('/', '\\'));
                }
            }
            catch { }
            roots.Add(@"C:\Program Files (x86)\Steam");
            var libs = new List<string>();
            foreach (var r in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                libs.Add(r);
                var vdf = Path.Combine(r, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                try
                {
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                        libs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
                }
                catch { }
            }
            return libs.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
        }
    }
}
