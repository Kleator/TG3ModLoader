using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using TG3ModLoader.Merge;

namespace TG3ModLoader.Core
{
    /// <summary>
    /// Writes the report of the last combine (ModLoader_Report.html) for players: plain language first,
    /// technical details folded away. Follows the light/dark setting of the browser.
    /// </summary>
    public static class ReportWriter
    {
        public const string ProjectUrl = "https://github.com/Kleator/TG3ModLoader";

        static string H(string s) => WebUtility.HtmlEncode(s ?? "");

        public static void Write(string path, ReportData d, GameEnv env) => File.WriteAllText(path, Render(d), new UTF8Encoding(false));

        const string Css = @"
:root{--bg:#f3f4f6;--surface:#fff;--sunken:#f6f7f9;--border:#e1e4e8;--text:#1b2129;--muted:#5b6573;--faint:#8c95a1;
--accent:#a8721f;--accent-soft:#f7eedf;--ok:#2e9e4f;--ok-soft:#e4f4e8;--warn:#b86f0e;--warn-soft:#fbf0dd;--bad:#d64545;--bad-soft:#fbe6e6;--info:#2f7fd1;--info-soft:#e5f0fb}
@media (prefers-color-scheme:dark){:root{--bg:#111418;--surface:#1c2128;--sunken:#161a20;--border:#2c333d;--text:#e9edf2;--muted:#a3adba;--faint:#6e7887;
--accent:#e0a84a;--accent-soft:#2c2416;--ok:#4fbf6b;--ok-soft:#17281c;--warn:#e9a23b;--warn-soft:#2e2412;--bad:#ef5e5e;--bad-soft:#321b1b;--info:#5aa9f0;--info-soft:#16253a}}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--text);font:15px/1.55 'Segoe UI Variable Text','Segoe UI',system-ui,sans-serif}
.wrap{max-width:980px;margin:0 auto;padding:36px 22px 56px}
header{display:flex;gap:18px;align-items:center;margin-bottom:26px}
header img{width:68px;height:68px}
h1{font-size:28px;line-height:1.2;margin:0;font-weight:650;letter-spacing:-.3px}
h2{font-size:19px;margin:0 0 4px;font-weight:650}
.sub{color:var(--muted);margin-top:4px}
.card{background:var(--surface);border:1px solid var(--border);border-radius:16px;padding:22px 26px;margin:18px 0;box-shadow:0 1px 3px rgba(0,0,0,.05)}
.lead{color:var(--muted);margin:2px 0 14px}
.hero{display:flex;gap:18px;align-items:flex-start}
.dot{flex:none;width:46px;height:46px;border-radius:50%;display:grid;place-items:center;font-size:24px;font-weight:700}
.dot.ok{background:var(--ok-soft);color:var(--ok)}.dot.warn{background:var(--warn-soft);color:var(--warn)}.dot.bad{background:var(--bad-soft);color:var(--bad)}
.hero h2{font-size:22px}
.hero p{margin:4px 0 0;color:var(--muted)}
.stats{display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:12px;margin-top:20px}
.stat{background:var(--sunken);border:1px solid var(--border);border-radius:12px;padding:14px 16px}
.stat b{display:block;font-size:28px;line-height:1.1;font-weight:650}
.stat span{color:var(--muted);font-size:13.5px}
.stat.ok b{color:var(--ok)}.stat.warn b{color:var(--warn)}
.mod{display:flex;gap:14px;align-items:center;padding:13px 0;border-top:1px solid var(--border)}
.mod:first-of-type{border-top:0}
.rank{flex:none;width:32px;height:32px;border-radius:50%;background:var(--accent-soft);color:var(--accent);font-weight:700;display:grid;place-items:center}
.mod .name{font-weight:600}
.mod .info{color:var(--muted);font-size:14px}
.grow{flex:1;min-width:0}
.badge{flex:none;font-size:12.5px;font-weight:600;padding:3px 11px;border-radius:999px;background:var(--info-soft);color:var(--info)}
.badge.map{background:var(--ok-soft);color:var(--ok)}.badge.mixed{background:var(--accent-soft);color:var(--accent)}
.hint{margin-top:14px;padding:11px 14px;border-radius:10px;background:var(--sunken);color:var(--muted);font-size:14px}
.area{margin:22px 0 8px;font-size:13px;font-weight:700;letter-spacing:.6px;text-transform:uppercase;color:var(--faint)}
.conflict{border:1px solid var(--border);border-radius:12px;padding:14px 16px;margin:10px 0;background:var(--surface)}
.c-title{font-weight:600}
.c-ctx{color:var(--muted);font-size:13.5px}
.opts{margin-top:10px;display:grid;gap:4px}
.opt{display:flex;gap:12px;align-items:center;padding:7px 12px;border-radius:9px;background:var(--sunken)}
.opt .who{flex:1;min-width:0}
.opt .val{font-family:Consolas,'Cascadia Mono',monospace;font-size:13.5px;color:var(--muted);text-align:right;overflow-wrap:anywhere}
.opt.used{background:var(--ok-soft)}
.opt.used .who{font-weight:600}
.opt.used .val{color:var(--text)}
.tag{flex:none;font-size:12px;font-weight:700;color:var(--ok)}
.why{margin-top:8px;color:var(--faint);font-size:13px}
.note{display:flex;gap:12px;padding:12px 0;border-top:1px solid var(--border)}
.note:first-of-type{border-top:0}
.note .ic{flex:none;color:var(--warn);font-weight:700}
details{margin-top:6px}
summary{cursor:pointer;color:var(--muted);font-weight:600}
table{width:100%;border-collapse:collapse;font-size:13.5px;margin-top:12px}
th,td{text-align:left;padding:7px 9px;border-bottom:1px solid var(--border);vertical-align:top}
th{color:var(--muted);font-weight:600}
code{font-family:Consolas,'Cascadia Mono',monospace;font-size:12.5px;overflow-wrap:anywhere}
.muted{color:var(--muted)}
footer{margin-top:30px;color:var(--faint);font-size:13px;text-align:center}
footer a{color:var(--accent)}
";

        public static string Render(ReportData d)
        {
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
            sb.Append("<meta name=\"color-scheme\" content=\"light dark\"><meta name=\"generator\" content=\"TG3 ModLoader ").Append(H(Builder.ToolVersion)).Append("\">");
            sb.Append("<title>TG3 ModLoader - Report</title><style>").Append(Css).Append("</style></head><body><div class=\"wrap\">");

            var icon = IconDataUri();
            sb.Append("<header>");
            if (icon != null) sb.Append("<img alt=\"\" src=\"").Append(icon).Append("\">");
            sb.Append("<div><h1>Mod report</h1><div class=\"sub\">");
            if (d.BuiltAt != default) sb.Append("Combined on ").Append(H(d.BuiltAt.ToString("dddd, d MMMM yyyy 'at' HH:mm", CultureInfo.GetCultureInfo("en-US"))));
            sb.Append(" &middot; TG3 ModLoader ").Append(H(Builder.ToolVersion)).Append("</div></div></header>");

            Hero(sb, d);
            if (d.Mods.Count > 0) Mods(sb, d);
            Conflicts(sb, d);
            Notes(sb, d);
            Technical(sb, d);

            sb.Append("<footer>Created by TG3 ModLoader &middot; <a href=\"").Append(ProjectUrl).Append("\">").Append(ProjectUrl.Replace("https://", "")).Append("</a></footer>");
            sb.Append("</div></body></html>");
            return sb.ToString();
        }

        static void Hero(StringBuilder sb, ReportData d)
        {
            int n = d.Mods.Count, conflicts = d.Conflicts.Count, problems = d.Violations.Count;
            string cls, symbol, title, text;
            if (!d.Success)
            {
                cls = "bad"; symbol = "!"; title = "Combining failed";
                text = "The mods could not be combined: " + H(d.Error) + ". Your previous combination is unchanged.";
            }
            else if (n == 0)
            {
                cls = "warn"; symbol = "i"; title = "No mods selected";
                text = "Tick the mods you want to play with in the ModLoader and combine them.";
            }
            else if (problems > 0)
            {
                cls = "warn"; symbol = "!"; title = "Combined - but some changes could not be confirmed";
                text = n + " mods were combined. For " + problems + " change" + (problems == 1 ? "" : "s") + " the automatic check could not confirm that it is included - see the technical details below.";
            }
            else
            {
                cls = "ok"; symbol = "✓"; title = n == 1 ? "Your mod is ready to play" : "Your " + n + " mods are combined and ready to play";
                text = n == 1
                    ? "The mod was prepared for the official Mod Launcher. All of its changes are included."
                    : "They were combined into one mod. Every change of every mod is included" +
                      (conflicts > 0
                          ? " - except where several mods change the same setting differently. For those " + conflicts + " setting" + (conflicts == 1 ? "" : "s") + " one value had to be chosen (see below)."
                          : ". None of your mods get in each other's way.");
            }
            sb.Append("<section class=\"card\"><div class=\"hero\"><div class=\"dot ").Append(cls).Append("\">").Append(symbol).Append("</div><div>");
            sb.Append("<h2>").Append(H(title)).Append("</h2><p>").Append(text).Append("</p>");
            if (d.Success && d.LauncherUpdated) sb.Append("<p>The official Mod Launcher was prepared: <b>MergedMod</b> is switched on and the single mods are switched off. Just click <b>Play</b> there.</p>");
            if (!d.Complete) sb.Append("<p class=\"muted\">This combination was made with an older version - combine again to see all details here.</p>");
            sb.Append("</div></div>");
            if (d.Success && n > 0)
            {
                sb.Append("<div class=\"stats\">");
                Stat(sb, n.ToString(), n == 1 ? "mod combined" : "mods combined", "");
                if (d.Complete) Stat(sb, d.Files.Count.ToString(), "game files changed", "");
                Stat(sb, conflicts.ToString(), conflicts == 1 ? "conflict" : "conflicts", conflicts > 0 ? "warn" : "ok");
                if (d.Complete) Stat(sb, problems == 0 ? "✓" : problems.ToString(), problems == 0 ? "all changes included" : "changes not confirmed", problems == 0 ? "ok" : "warn");
                sb.Append("</div>");
            }
            sb.Append("</section>");
        }

        static void Stat(StringBuilder sb, string value, string label, string cls) =>
            sb.Append("<div class=\"stat ").Append(cls).Append("\"><b>").Append(H(value)).Append("</b><span>").Append(H(label)).Append("</span></div>");

        static void Mods(StringBuilder sb, ReportData d)
        {
            sb.Append("<section class=\"card\"><h2>Your mods</h2><p class=\"lead\">In the order of your list - the mod at the top wins when two mods change the same setting differently.</p>");
            foreach (var m in d.Mods.OrderBy(m => m.Position))
            {
                string badgeCls = m.Kind == ModKind.Map ? "map" : m.Kind == ModKind.Mixed ? "mixed" : "";
                sb.Append("<div class=\"mod\"><div class=\"rank\">").Append(m.Position).Append("</div><div class=\"grow\"><div class=\"name\">").Append(H(m.Name)).Append("</div><div class=\"info\">");
                var parts = new System.Collections.Generic.List<string>();
                if (d.Complete) parts.Add("changes " + m.ChangedFiles + " game file" + (m.ChangedFiles == 1 ? "" : "s"));
                int total = m.ConflictsWon + m.ConflictsLost;
                if (total > 0) parts.Add("wins " + m.ConflictsWon + " of " + total + " conflict" + (total == 1 ? "" : "s"));
                else if (d.Complete) parts.Add("no conflicts");
                if (m.Violations > 0) parts.Add(m.Violations + " change" + (m.Violations == 1 ? "" : "s") + " not confirmed");
                sb.Append(H(string.Join(" · ", parts))).Append("</div></div>");
                sb.Append("<span class=\"badge ").Append(badgeCls).Append("\">").Append(H(Friendly.Kind(m.Kind))).Append("</span></div>");
            }
            sb.Append("<div class=\"hint\">You can change the order, or choose the winner of a conflict yourself, in the ModLoader. Then combine again.</div></section>");
        }

        static void Conflicts(StringBuilder sb, ReportData d)
        {
            if (!d.Success || d.Mods.Count < 2) return;
            sb.Append("<section class=\"card\"><h2>Conflicts</h2>");
            if (d.Conflicts.Count == 0)
            {
                sb.Append("<p class=\"lead\">None - no two of your mods change the same setting differently, so every change is used.</p></section>");
                return;
            }
            sb.Append("<p class=\"lead\">These settings are changed by more than one of your mods, and the game can only use one value. All other changes of these mods are still used.</p>");
            foreach (var group in d.Conflicts.GroupBy(c => Friendly.Area(c.File)).OrderBy(g => g.Key))
            {
                sb.Append("<div class=\"area\">").Append(H(group.Key)).Append(" &middot; ").Append(group.Count()).Append("</div>");
                foreach (var c in group) ConflictCard(sb, c);
            }
            sb.Append("</section>");
        }

        static void ConflictCard(StringBuilder sb, Conflict c)
        {
            var (title, context) = Friendly.Setting(c.Path);
            sb.Append("<div class=\"conflict\"><div class=\"c-title\">").Append(H(title)).Append("</div>");
            if (context.Length > 0) sb.Append("<div class=\"c-ctx\">").Append(H(context)).Append("</div>");
            sb.Append("<div class=\"opts\">");
            bool custom = c.WinnerId != null && c.WinnerId.StartsWith(ConflictPolicy.CustomPrefix);
            foreach (var cand in c.Candidates.Where(x => x.ModName != "Vanilla"))
            {
                bool used = !custom && cand.ModName == c.WinnerName;
                Option(sb, cand.ModName, CandidateValue(c, cand.Text), used);
            }
            var vanilla = c.Candidates.FirstOrDefault(x => x.ModName == "Vanilla");
            string customValue = custom ? c.WinnerId.Substring(ConflictPolicy.CustomPrefix.Length) : null;
            if (vanilla.ModName != null)
                Option(sb, "Original game", CandidateValue(c, vanilla.Text), custom && Friendly.Value(customValue) == Friendly.Value(vanilla.Text));
            if (custom && (vanilla.ModName == null || Friendly.Value(customValue) != Friendly.Value(vanilla.Text)))
                Option(sb, "Your own value", Friendly.Value(customValue), true);
            sb.Append("</div><div class=\"why\">");
            sb.Append(c.ByOverride ? "Chosen by you." : "Used because " + H(c.WinnerName) + " is higher in your list.");
            sb.Append("</div></div>");
        }

        static string CandidateValue(Conflict c, string text)
        {
            if (c.Kind == "file") return "its own version of this file";
            if (text == "(removed)") return "removes this setting";
            return Friendly.Value(text);
        }

        static void Option(StringBuilder sb, string who, string value, bool used)
        {
            sb.Append("<div class=\"opt").Append(used ? " used" : "").Append("\"><span class=\"who\">").Append(H(who)).Append("</span><span class=\"val\">").Append(H(value)).Append("</span>");
            if (used) sb.Append("<span class=\"tag\">✓ used</span>");
            sb.Append("</div>");
        }

        static void Notes(StringBuilder sb, ReportData d)
        {
            if (d.Warnings.Count == 0) return;
            sb.Append("<section class=\"card\"><h2>Good to know</h2>");
            foreach (var w in d.Warnings) sb.Append("<div class=\"note\"><span class=\"ic\">!</span><div>").Append(H(Friendly.Warning(w))).Append("</div></div>");
            sb.Append("</section>");
        }

        static void Technical(StringBuilder sb, ReportData d)
        {
            sb.Append("<section class=\"card\"><details><summary>Technical details</summary>");
            sb.Append("<p class=\"muted\">Combined mod: <code>").Append(H(d.OutputDir)).Append("</code><br>");
            sb.Append(d.OutputFiles).Append(" files written");
            if (d.DurationSeconds > 0) sb.Append(" in ").Append(d.DurationSeconds.ToString("0.0", CultureInfo.InvariantCulture)).Append(" s");
            if (d.Complete) sb.Append(" &middot; ").Append(d.CheckedChanges).Append(" individual changes checked, ").Append(d.Violations.Count).Append(" not confirmed");
            sb.Append("</p>");
            if (d.Files.Count > 0)
            {
                sb.Append("<table><tr><th>File</th><th>Area</th><th>How</th><th>Mods</th><th>Conflicts</th></tr>");
                foreach (var f in d.Files.OrderBy(f => f.Method == "copy" ? 1 : 0).ThenBy(f => f.Path))
                {
                    sb.Append("<tr><td><code>").Append(H(f.Path)).Append("</code>");
                    if (!string.IsNullOrEmpty(f.Note)) sb.Append("<div class=\"muted\">").Append(H(f.Note)).Append("</div>");
                    sb.Append("</td><td>").Append(H(Friendly.Area(f.Path))).Append("</td><td>").Append(H(Friendly.Method(f.Method)))
                      .Append("</td><td>").Append(H(string.Join(", ", f.Mods))).Append("</td><td>").Append(f.Conflicts).Append("</td></tr>");
                }
                sb.Append("</table>");
            }
            if (d.Conflicts.Count > 0)
            {
                sb.Append("<table><tr><th>Conflict location</th><th>Kind</th><th>Used</th></tr>");
                foreach (var c in d.Conflicts)
                    sb.Append("<tr><td><code>").Append(H(c.File)).Append(" :: ").Append(H(c.Path)).Append("</code></td><td>").Append(H(c.Kind))
                      .Append("</td><td>").Append(H(c.WinnerName)).Append(c.ByOverride ? " (your choice)" : "").Append("</td></tr>");
                sb.Append("</table>");
            }
            if (d.Violations.Count > 0)
            {
                sb.Append("<table><tr><th>Changes not confirmed by the automatic check</th></tr>");
                foreach (var v in d.Violations) sb.Append("<tr><td><code>").Append(H(v)).Append("</code></td></tr>");
                sb.Append("</table>");
            }
            sb.Append("</details></section>");
        }

        static string iconCache;

        static string IconDataUri()
        {
            if (iconCache != null) return iconCache;
            try
            {
                using (var s = typeof(ReportWriter).Assembly.GetManifestResourceStream("TG3ModLoader.app.ico"))
                {
                    if (s == null) return null;
                    using (var ic = new Icon(s, 128, 128))
                    using (var bmp = ic.ToBitmap())
                    using (var ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Png);
                        return iconCache = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
            catch { return null; }
        }
    }
}
