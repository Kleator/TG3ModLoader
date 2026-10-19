using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TG3ModLoader.Merge;
using TG3ModLoader.Util;

namespace TG3ModLoader.Core
{
    /// <summary>
    /// Everything the report and the GUI show about the last combine. Stored in build_manifest.json, so the
    /// report can be re-created at any time with the current design.
    /// </summary>
    public sealed class ReportData
    {
        public bool Success = true;
        public string Error;
        public DateTime BuiltAt;
        public double DurationSeconds;
        public string ToolVersion;
        public string OutputDir;
        public int OutputFiles;
        public bool LauncherUpdated;
        public List<ModStats> Mods = new List<ModStats>();
        public List<FileOutcome> Files = new List<FileOutcome>();
        public List<Conflict> Conflicts = new List<Conflict>();
        public List<string> Warnings = new List<string>();
        public List<string> Violations = new List<string>();
        /// <summary>False for manifests written by older versions (only part of the data is available).</summary>
        public bool Complete;

        public int CheckedChanges => Mods.Sum(m => m.CheckedChanges);

        public static ReportData FromResult(BuildResult r, GameEnv env) => new ReportData
        {
            Success = r.Success,
            Error = r.Error,
            BuiltAt = r.BuiltAt,
            DurationSeconds = r.Duration.TotalSeconds,
            ToolVersion = Builder.ToolVersion,
            OutputDir = env.OutputModDir,
            OutputFiles = r.OutputFiles,
            LauncherUpdated = r.LauncherUpdated,
            Mods = r.Mods,
            Files = r.Files,
            Conflicts = r.Conflicts,
            Warnings = r.Warnings,
            Violations = r.Violations,
            Complete = true
        };

        public void WriteTo(Dictionary<string, object> d)
        {
            d["success"] = Success;
            d["error"] = Error;
            d["durationSeconds"] = DurationSeconds;
            d["launcherUpdated"] = LauncherUpdated;
            d["modStats"] = Mods.Select(m => (object)new Dictionary<string, object>
            {
                ["position"] = m.Position, ["id"] = m.Id, ["name"] = m.Name, ["kind"] = m.Kind.ToString(),
                ["changedFiles"] = m.ChangedFiles, ["checkedChanges"] = m.CheckedChanges,
                ["conflictsWon"] = m.ConflictsWon, ["conflictsLost"] = m.ConflictsLost, ["violations"] = m.Violations
            }).ToList();
            d["files"] = Files.Select(f => (object)new Dictionary<string, object>
            {
                ["path"] = f.Path, ["method"] = f.Method, ["mods"] = f.Mods.Cast<object>().ToList(),
                ["conflicts"] = f.Conflicts, ["checkedChanges"] = f.CheckedChanges, ["note"] = f.Note
            }).ToList();
            d["violationList"] = Violations.Cast<object>().ToList();
        }

        public static ReportData FromManifest(Dictionary<string, object> m)
        {
            if (m == null) return null;
            var r = new ReportData
            {
                ToolVersion = m.Str("toolVersion"),
                OutputDir = m.Str("outputDir"),
                OutputFiles = Int(m, "outputFiles"),
                Success = m.Bool("success", true),
                Error = m.Str("error"),
                DurationSeconds = Dbl(m, "durationSeconds"),
                LauncherUpdated = m.Bool("launcherUpdated", false),
                Conflicts = Builder.ConflictsFromManifest(m)
            };
            DateTime.TryParseExact(m.Str("builtAt") ?? "", "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out r.BuiltAt);
            if (m.TryGetValue("warnings", out var w) && w is List<object> wl) r.Warnings = wl.Select(x => x?.ToString()).Where(x => x != null).ToList();
            if (m.TryGetValue("violationList", out var v) && v is List<object> vl) r.Violations = vl.Select(x => x?.ToString()).Where(x => x != null).ToList();
            if (m.TryGetValue("modStats", out var ms) && ms is List<object> msl)
            {
                r.Complete = true;
                foreach (var o in msl.OfType<Dictionary<string, object>>())
                {
                    Enum.TryParse(o.Str("kind") ?? "Content", out ModKind kind);
                    r.Mods.Add(new ModStats
                    {
                        Position = Int(o, "position"), Id = o.Str("id"), Name = o.Str("name"), Kind = kind,
                        ChangedFiles = Int(o, "changedFiles"), CheckedChanges = Int(o, "checkedChanges"),
                        ConflictsWon = Int(o, "conflictsWon"), ConflictsLost = Int(o, "conflictsLost"), Violations = Int(o, "violations")
                    });
                }
            }
            else if (m.TryGetValue("mods", out var mods) && mods is List<object> ml)
            {
                foreach (var o in ml.OfType<Dictionary<string, object>>())
                    r.Mods.Add(new ModStats { Position = Int(o, "position") > 0 ? Int(o, "position") : Int(o, "priority"), Id = o.Str("id"), Name = o.Str("name") });
            }
            if (m.TryGetValue("files", out var fs) && fs is List<object> fl)
            {
                foreach (var o in fl.OfType<Dictionary<string, object>>())
                {
                    var f = new FileOutcome { Path = o.Str("path"), Method = o.Str("method"), Conflicts = Int(o, "conflicts"), CheckedChanges = Int(o, "checkedChanges"), Note = o.Str("note") };
                    if (o.TryGetValue("mods", out var fm) && fm is List<object> fml) f.Mods = fml.Select(x => x?.ToString()).ToList();
                    r.Files.Add(f);
                }
            }
            return r;
        }

        static int Int(Dictionary<string, object> d, string k) => d.TryGetValue(k, out var v) && v is double x ? (int)x : 0;
        static double Dbl(Dictionary<string, object> d, string k) => d.TryGetValue(k, out var v) && v is double x ? x : 0;
    }
}
