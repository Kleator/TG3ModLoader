using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace TG3ModLoader.Core
{
    /// <summary>Writes the human readable build report (ModLoader_Report.html).</summary>
    public static class ReportWriter
    {
        static string H(string s) => WebUtility.HtmlEncode(s ?? "");

        public static void Write(string path, BuildResult r, GameEnv env)
        {
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>TG3 ModLoader - Report</title><style>");
            sb.Append("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#222;background:#fafafa}h1{margin:0 0 4px}h2{margin-top:28px;border-bottom:1px solid #ccc;padding-bottom:4px}");
            sb.Append("table{border-collapse:collapse;width:100%;font-size:13px;background:#fff}th,td{border:1px solid #ddd;padding:4px 8px;text-align:left;vertical-align:top}th{background:#eee}");
            sb.Append(".ok{color:#1a7f37;font-weight:600}.bad{color:#b42318;font-weight:600}.warn{color:#9a6700}.muted{color:#666}code{font-size:12px}.win{font-weight:600}");
            sb.Append("</style></head><body>");
            sb.Append("<h1>TG3 ModLoader - Report</h1>");
            sb.Append("<div class=\"muted\">Created: ").Append(H(r.BuiltAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append(" &middot; version ").Append(Builder.ToolVersion)
              .Append(" &middot; duration ").Append(r.Duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)).Append(" s</div>");

            sb.Append("<h2>Result</h2><p>");
            if (!r.Success) sb.Append("<span class=\"bad\">Failed: ").Append(H(r.Error)).Append("</span>");
            else
            {
                sb.Append("<span class=\"ok\">Success.</span> ").Append(r.OutputFiles).Append(" files written to <code>").Append(H(env.OutputModDir)).Append("</code>. ");
                int merged = r.Files.Count(f => f.Method == "structural" || f.Method == "line");
                sb.Append(merged).Append(" files combined from several mods, ").Append(r.Conflicts.Count).Append(" real conflicts (the same value changed differently by several mods). ");
                sb.Append(r.Violations.Count == 0 ? "<span class=\"ok\">Verification: every change of every mod is present in the result.</span>" : "<span class=\"bad\">" + r.Violations.Count + " verification errors</span>");
                if (r.LauncherUpdated) sb.Append("<br>The official Mod Launcher was updated: the ModLoader output is enabled, the merged mods are disabled there.");
            }
            sb.Append("</p>");

            if (r.Warnings.Count > 0)
            {
                sb.Append("<h2>Notes</h2><ul>");
                foreach (var w in r.Warnings) sb.Append("<li class=\"warn\">").Append(H(w)).Append("</li>");
                sb.Append("</ul>");
            }

            sb.Append("<h2>Mods (priority order)</h2><p class=\"muted\">The mod at the top has the highest priority and wins real conflicts.</p>");
            sb.Append("<table><tr><th>#</th><th>Mod</th><th>ID</th><th>Type</th><th>Changed files</th><th>Verified changes</th><th>Conflicts won</th><th>Conflicts lost</th><th>Verification</th></tr>");
            foreach (var m in r.Mods)
            {
                sb.Append("<tr><td>").Append(m.Position).Append("</td><td>").Append(H(m.Name)).Append("</td><td>").Append(H(m.Id)).Append("</td><td>").Append(H(m.Kind.ToString()))
                  .Append("</td><td>").Append(m.ChangedFiles).Append("</td><td>").Append(m.CheckedChanges).Append("</td><td>").Append(m.ConflictsWon)
                  .Append("</td><td>").Append(m.ConflictsLost).Append("</td><td>")
                  .Append(m.Violations == 0 ? "<span class=\"ok\">OK</span>" : "<span class=\"bad\">" + m.Violations + " errors</span>").Append("</td></tr>");
            }
            sb.Append("</table>");

            sb.Append("<h2>Files changed by mods</h2><table><tr><th>File</th><th>Method</th><th>Mods</th><th>Conflicts</th><th>Note</th></tr>");
            foreach (var f in r.Files.OrderBy(f => f.Method == "copy" ? 1 : 0).ThenBy(f => f.Path))
            {
                string method;
                switch (f.Method)
                {
                    case "structural": method = "merged structurally"; break;
                    case "line": method = "merged line by line"; break;
                    case "priority": method = "priority (binary file)"; break;
                    default: method = "taken over"; break;
                }
                sb.Append("<tr><td><code>").Append(H(f.Path)).Append("</code></td><td>").Append(method).Append("</td><td>").Append(H(string.Join(", ", f.Mods)))
                  .Append("</td><td>").Append(f.Conflicts).Append("</td><td>").Append(H(f.Note)).Append("</td></tr>");
            }
            sb.Append("</table>");

            sb.Append("<h2>Real conflicts</h2>");
            if (r.Conflicts.Count == 0) sb.Append("<p class=\"ok\">None.</p>");
            else
            {
                sb.Append("<p class=\"muted\">Several mods changed the same value differently. Only this value is affected - all other changes of these mods are kept. " +
                          "By default the mod with the highest priority (further up in the list) wins. On the \"Conflicts\" tab of the ModLoader you can choose another mod, " +
                          "the vanilla value or an own value for every location.</p>");
                sb.Append("<table><tr><th>File</th><th>Location</th><th>Kind</th><th>Variants</th><th>Winner</th></tr>");
                foreach (var c in r.Conflicts)
                {
                    sb.Append("<tr><td><code>").Append(H(c.File)).Append("</code></td><td><code>").Append(H(c.Path)).Append("</code></td><td>").Append(H(c.Kind)).Append("</td><td>");
                    foreach (var cand in c.Candidates)
                        sb.Append(cand.ModName == c.WinnerName ? "<div class=\"win\">" : "<div>").Append(H(cand.ModName)).Append(": <code>").Append(H(cand.Text)).Append("</code></div>");
                    sb.Append("</td><td class=\"win\">").Append(H(c.WinnerName)).Append(c.ByOverride ? " (manual)" : "").Append("</td></tr>");
                }
                sb.Append("</table>");
            }

            if (r.Violations.Count > 0)
            {
                sb.Append("<h2>Verification errors</h2><ul>");
                foreach (var v in r.Violations) sb.Append("<li class=\"bad\">").Append(H(v)).Append("</li>");
                sb.Append("</ul>");
            }
            sb.Append("</body></html>");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
