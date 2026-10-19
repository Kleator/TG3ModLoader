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
        public const string ExeName = "TG3ModLoader.exe";
        /// <summary>Folder for settings and the combined mod when the program itself was subscribed in the Workshop.</summary>
        public const string DataFolderName = "TG3ModLoader";

        /// <summary>Where TG3ModLoader.exe is.</summary>
        public string ToolDir;
        /// <summary>Where settings, the combined mod, the manifest and the report are kept (usually the same as <see cref="ToolDir"/>).</summary>
        public string DataDir;
        /// <summary>Steam Workshop item id of this copy, if it was subscribed in the Workshop.</summary>
        public string WorkshopItemId;
        public string GameDir;
        public string WorkshopDir;

        public bool InstalledFromWorkshop => WorkshopItemId != null;
        public string OutputModDir => Path.Combine(DataDir, OutputFolderName);
        public string ConfigPath => Path.Combine(DataDir, "ModLoader.config.json");
        public string ManifestPath => Path.Combine(DataDir, "build_manifest.json");
        public string ReportPath => Path.Combine(DataDir, "ModLoader_Report.html");
        public string LogPath => Path.Combine(DataDir, "ModLoader.log");
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

        static string Normalize(string dir)
        {
            var full = Path.GetFullPath(dir);
            var trimmed = full.TrimEnd('\\', '/');
            return trimmed.EndsWith(":") ? full : trimmed;
        }

        /// <summary>The Steam Workshop folder of the game if <paramref name="dir"/> is directly inside it (...\workshop\content\311260\&lt;folder&gt;).</summary>
        static DirectoryInfo WorkshopParent(string dir)
        {
            var parent = Directory.GetParent(Normalize(dir));
            return parent != null && parent.Name == AppId && parent.Parent != null && parent.Parent.Name.Equals("content", StringComparison.OrdinalIgnoreCase) ? parent : null;
        }

        /// <summary>
        /// A copy subscribed in the Steam Workshop lives in ...\workshop\content\311260\&lt;item id&gt;\. Steam replaces that
        /// folder on every update and deletes it on unsubscribe, so settings and the combined mod are kept next to it in
        /// ...\311260\TG3ModLoader\ - the folder a manual installation uses, so both share their settings.
        /// </summary>
        public static string DataDirFor(string toolDir)
        {
            var dir = Normalize(toolDir);
            var name = Path.GetFileName(dir);
            var workshop = WorkshopParent(dir);
            return workshop != null && name.Length > 0 && name.All(char.IsDigit) ? Path.Combine(workshop.FullName, DataFolderName) : dir;
        }

        /// <summary>True for a folder that contains a copy of the ModLoader program (it is a tool, never a game mod).</summary>
        public static bool IsModLoaderFolder(string dir) => !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, ExeName));

        public void EnsureDataDir() => Directory.CreateDirectory(DataDir);

        public static GameEnv Detect(string toolDir, string gameDirOverride = null, string workshopOverride = null)
        {
            var env = new GameEnv { ToolDir = Normalize(toolDir) };
            env.DataDir = DataDirFor(env.ToolDir);
            if (!env.DataDir.Equals(env.ToolDir, StringComparison.OrdinalIgnoreCase)) env.WorkshopItemId = Path.GetFileName(env.ToolDir);

            if (!string.IsNullOrEmpty(workshopOverride) && Directory.Exists(workshopOverride)) env.WorkshopDir = Path.GetFullPath(workshopOverride);
            else
            {
                // Installed in the Workshop folder: <steamapps>\workshop\content\311260\<TG3ModLoader or item id>\TG3ModLoader.exe
                var parent = WorkshopParent(env.ToolDir);
                if (parent != null) env.WorkshopDir = parent.FullName;
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
