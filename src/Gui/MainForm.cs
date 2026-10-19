using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using TG3ModLoader.Core;
using TG3ModLoader.Merge;
using TG3ModLoader.Util;

namespace TG3ModLoader.Gui
{
    /// <summary>Main window: choose and order mods, combine them, look at conflicts and start the game.</summary>
    public sealed class MainForm : Form
    {
        readonly string toolDir;
        GameEnv env;
        ToolConfig cfg;
        List<ModInfo> catalog = new List<ModInfo>();
        ReportData last;
        string lastFingerprint;
        bool busy, upToDate, statusKnown, firstStart;
        string transientTitle, transientDetail;
        DateTime transientUntil;

        readonly Timer statusTimer = new Timer { Interval = 300 };
        int statusRequest;

        // Layout
        HeaderBar header;
        Panel body, content, footer, logFrame;
        StatusBanner banner;
        NavTabs tabs;
        Panel pageMods, pageConflicts, pageLog;
        FlowLayoutPanel modsTools, logTools;
        TextBlock modsIntro, conflictsIntro, footerInfo;
        ModListView modList;
        ConflictListView conflictList;
        TextBox logBox;
        FlatButton btnPrimary, btnCombine, btnReport, btnHelp, btnMore, btnAll, btnNone, btnFromLauncher, btnRescan, btnCopyLog;
        ContextMenuStrip moreMenu, modMenu;
        readonly List<(ToolStripMenuItem Item, string Glyph)> menuGlyphs = new List<(ToolStripMenuItem, string)>();
        readonly List<Control> busyLocked = new List<Control>();

        public MainForm(string toolDir)
        {
            this.toolDir = toolDir;
            Text = "TG3 ModLoader";
            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Body;
            StartPosition = FormStartPosition.CenterScreen;
            Icon = LoadAppIcon(SystemInformation.IconSize.Width);
            Size = new Size(S(1120), S(780));
            MinimumSize = new Size(S(880), S(600));
            DoubleBuffered = true;
            BuildLayout();
            ApplyTheme();
            statusTimer.Tick += (s, e) => { statusTimer.Stop(); ComputeStatus(); };
            Theme.Changed += ApplyTheme;
            Load += (s, e) => { Theme.StartWatching(this); Reload(true); };
        }

        int S(float v) => (int)Math.Round(v * DeviceDpi / 96f);

        // ================= Layout =================

        void BuildLayout()
        {
            header = new HeaderBar { Dock = DockStyle.Top, Height = S(88), Logo = LoadAppIcon(128)?.ToBitmap() };
            btnHelp = Ghost("How it works", Theme.GHelp, (s, e) => ShowHelp(), () => Theme.P.Header);
            btnMore = Ghost("More", Theme.GMore, (s, e) => moreMenu.Show(btnMore, new Point(btnMore.Width, btnMore.Height + S(4)), ToolStripDropDownDirection.BelowLeft), () => Theme.P.Header);
            header.Controls.Add(btnHelp);
            header.Controls.Add(btnMore);
            header.Resize += (s, e) => LayoutHeader();

            // Footer action bar
            footer = new Panel { Dock = DockStyle.Bottom, Height = S(84) };
            footer.Paint += (s, e) => { using (var pen = new Pen(Theme.P.Border)) e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
            btnPrimary = new FlatButton { Kind = ButtonKind.Primary, Text = "Combine & Play", Glyph = Theme.GPlay, Background = () => Theme.P.Header };
            btnPrimary.Click += (s, e) => PrimaryAction();
            btnCombine = new FlatButton { Kind = ButtonKind.Secondary, Text = "Combine only", Glyph = Theme.GSync, Background = () => Theme.P.Header };
            btnCombine.Click += (s, e) => Combine(false);
            btnReport = new FlatButton { Kind = ButtonKind.Ghost, Text = "Report", Glyph = Theme.GDocument, Background = () => Theme.P.Header };
            btnReport.Click += (s, e) => OpenReport();
            footerInfo = new TextBlock { AutoHeight = false, TextFont = Theme.Small, Background = () => Theme.P.Header, Color = () => Theme.P.TextMuted };
            footer.Controls.AddRange(new Control[] { btnPrimary, btnCombine, btnReport, footerInfo });
            footer.Resize += (s, e) => LayoutFooter();

            // Body: banner, tabs, pages
            body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(24), S(18), S(24), S(12)) };
            banner = new StatusBanner { Dock = DockStyle.Top };
            banner.ActionClicked += (s, e) => ChooseGameFolder();
            tabs = new NavTabs { Dock = DockStyle.Top, Height = S(46) };
            tabs.Items.AddRange(new[] { "Your mods", "Conflicts", "Activity" });
            tabs.SelectedChanged += (s, e) => ShowPage(tabs.Selected);
            content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, S(14), 0, 0) };

            // Page: mods
            pageMods = new Panel { Dock = DockStyle.Fill };
            modsIntro = new TextBlock
            {
                Dock = DockStyle.Top,
                Text = "Tick the mods you want to play with. Drag them into order: if two mods change the same setting, the mod higher up wins. " +
                       "Maps don't need combining - switch them on in the official Mod Launcher."
            };
            modsTools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = S(48), WrapContents = false, Padding = new Padding(0, S(6), 0, S(4)) };
            btnAll = Ghost("Select all", Theme.GSelectAll, (s, e) => SelectAll(true));
            btnNone = Ghost("Clear selection", Theme.GClear, (s, e) => SelectAll(false));
            btnFromLauncher = Ghost("Use Mod Launcher selection", Theme.GSync, (s, e) => ImportLauncher());
            btnRescan = Ghost("Rescan", Theme.GRefresh, (s, e) => Reload(false));
            modsTools.Controls.AddRange(new Control[] { btnAll, btnNone, btnFromLauncher, btnRescan });
            modList = new ModListView { Dock = DockStyle.Fill, Subtitle = SubtitleFor, EmptyText = "No mods found. Subscribe to mods in the Steam Workshop, then click Rescan." };
            modList.Toggled += row => { if (busy) return; row.Sel.Enabled = !row.Sel.Enabled; SaveConfig(); FillMods(row.Sel); ScheduleStatus(); };
            modList.Moved += MoveMod;
            modList.MenuRequested += (row, pt) => ShowModMenu(row, pt);
            pageMods.Controls.Add(modList);
            pageMods.Controls.Add(Spacer(S(6)));
            pageMods.Controls.Add(modsTools);
            pageMods.Controls.Add(modsIntro);

            // Page: conflicts
            pageConflicts = new Panel { Dock = DockStyle.Fill };
            conflictsIntro = new TextBlock
            {
                Dock = DockStyle.Top,
                Text = "These settings are changed by more than one of your mods, and the game can only use one value. All other changes of these mods are still used. " +
                       "By default the mod higher in your list wins - click another value to use it instead, then combine again."
            };
            conflictList = new ConflictListView { Dock = DockStyle.Fill, EmptyText = "None of your selected mods change the same setting differently - every change of every mod is used." };
            conflictList.OptionClicked += OnConflictOption;
            pageConflicts.Controls.Add(conflictList);
            pageConflicts.Controls.Add(Spacer(S(12)));
            pageConflicts.Controls.Add(conflictsIntro);

            // Page: activity log
            pageLog = new Panel { Dock = DockStyle.Fill };
            logTools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = S(44), WrapContents = false };
            btnCopyLog = Ghost("Copy to clipboard", Theme.GCopy, (s, e) => { if (logBox.TextLength > 0) Clipboard.SetText(logBox.Text); });
            logTools.Controls.Add(btnCopyLog);
            logFrame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(14), S(12), S(6), S(12)) };
            logFrame.Paint += (s, e) =>
            {
                Theme.Smooth(e.Graphics);
                var r = new RectangleF(0.5f, 0.5f, logFrame.Width - 1.5f, logFrame.Height - 1.5f);
                Theme.StrokeRound(e.Graphics, Theme.P.Border, r, S(12));
            };
            logFrame.Resize += (s, e) => logFrame.Invalidate();
            logBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, Font = Theme.Mono, WordWrap = true };
            logBox.HandleCreated += (s, e) => Theme.ApplyNativeTheme(logBox);
            logFrame.Controls.Add(logBox);
            pageLog.Controls.Add(logFrame);
            pageLog.Controls.Add(logTools);

            content.Controls.AddRange(new Control[] { pageMods, pageConflicts, pageLog });
            body.Controls.Add(content);
            body.Controls.Add(tabs);
            body.Controls.Add(Spacer(S(16)));
            body.Controls.Add(banner);

            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(header);

            BuildMenus();
            busyLocked.AddRange(new Control[] { btnPrimary, btnCombine, btnAll, btnNone, btnFromLauncher, btnRescan, modList, conflictList });
            ShowPage(0);
        }

        static Control Spacer(int h) => new Panel { Dock = DockStyle.Top, Height = h, BackColor = Color.Transparent };

        FlatButton Ghost(string text, string glyph, EventHandler click, Func<Color> background = null)
        {
            var b = new FlatButton { Kind = ButtonKind.Ghost, Text = text, Glyph = glyph, Background = background ?? (() => Theme.P.Window) };
            b.Size = new Size(b.PreferredWidth(), S(36));
            b.Margin = new Padding(0, 0, S(6), 0);
            b.Click += click;
            return b;
        }

        void LayoutHeader()
        {
            int y = (header.Height - S(38)) / 2;
            btnMore.SetBounds(header.Width - S(24) - btnMore.PreferredWidth(), y, btnMore.PreferredWidth(), S(38));
            btnHelp.SetBounds(btnMore.Left - S(6) - btnHelp.PreferredWidth(), y, btnHelp.PreferredWidth(), S(38));
        }

        void LayoutFooter()
        {
            int h = S(46), y = (footer.Height - h) / 2, x = S(24);
            btnPrimary.SetBounds(x, y, Math.Max(S(210), btnPrimary.PreferredWidth()), h);
            x = btnPrimary.Right + S(10);
            btnCombine.SetBounds(x, y, btnCombine.PreferredWidth(), h);
            x = btnCombine.Right + S(6);
            btnReport.SetBounds(x, y, btnReport.PreferredWidth(), h);
            int infoX = btnReport.Right + S(16);
            footerInfo.SetBounds(infoX, y + S(13), Math.Max(S(40), footer.Width - infoX - S(24)), S(22));
        }

        void ShowPage(int i)
        {
            pageMods.Visible = i == 0;
            pageConflicts.Visible = i == 1;
            pageLog.Visible = i == 2;
            (i == 0 ? (Control)modList : i == 1 ? conflictList : logBox).Focus();
        }

        void BuildMenus()
        {
            moreMenu = new ContextMenuStrip { Renderer = new MenuRenderer(), ShowImageMargin = true, Font = Theme.Body, ImageScalingSize = new Size(S(20), S(20)) };
            AddItem(moreMenu, "Test the game", Theme.GTest, (s, e) => RunTest());
            AddItem(moreMenu, "Rescan mods", Theme.GRefresh, (s, e) => Reload(false));
            AddItem(moreMenu, "Use the Mod Launcher's selection", Theme.GSync, (s, e) => ImportLauncher());
            moreMenu.Items.Add(new ToolStripSeparator());
            AddItem(moreMenu, "Open report", Theme.GDocument, (s, e) => OpenReport());
            AddItem(moreMenu, "Open the combined mod folder", Theme.GFolder, (s, e) => OpenPath(env?.OutputModDir));
            AddItem(moreMenu, "Open the Steam Workshop mods folder", Theme.GFolder, (s, e) => OpenPath(env?.WorkshopDir));
            moreMenu.Items.Add(new ToolStripSeparator());
            AddItem(moreMenu, "Remove from the Mod Launcher", Theme.GClear, (s, e) => Unregister());
            moreMenu.Items.Add(new ToolStripSeparator());
            AddItem(moreMenu, "Create a desktop shortcut", Theme.GLink, (s, e) => CreateShortcut(true));
            moreMenu.Items.Add(new ToolStripSeparator());
            AddItem(moreMenu, "Read me", Theme.GHelp, (s, e) => OpenReadme());
            AddItem(moreMenu, "Project page on GitHub", Theme.GGlobe, (s, e) => OpenUrl(ReportWriter.ProjectUrl));
            modMenu = new ContextMenuStrip { Renderer = new MenuRenderer(), ShowImageMargin = true, Font = Theme.Body, ImageScalingSize = new Size(S(20), S(20)) };
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.Tab: tabs.Selected = (tabs.Selected + 1) % tabs.Items.Count; return true;
                case Keys.Control | Keys.Shift | Keys.Tab: tabs.Selected = (tabs.Selected + tabs.Items.Count - 1) % tabs.Items.Count; return true;
                case Keys.Control | Keys.D1: tabs.Selected = 0; return true;
                case Keys.Control | Keys.D2: tabs.Selected = 1; return true;
                case Keys.Control | Keys.D3: tabs.Selected = 2; return true;
                case Keys.F5: if (!busy) Reload(false); return true;
                case Keys.F1: ShowHelp(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        ToolStripMenuItem AddItem(ToolStrip strip, string text, string glyph, EventHandler click)
        {
            var item = new ToolStripMenuItem(text, null, click) { Padding = new Padding(0, S(3), 0, S(3)) };
            strip.Items.Add(item);
            menuGlyphs.Add((item, glyph));
            item.Image = MenuRenderer.GlyphImage(glyph, Theme.P.TextMuted, S(20));
            return item;
        }

        void ApplyTheme()
        {
            var p = Theme.P;
            BackColor = p.Window;
            body.BackColor = content.BackColor = pageMods.BackColor = pageConflicts.BackColor = pageLog.BackColor = p.Window;
            modsTools.BackColor = logTools.BackColor = p.Window;
            footer.BackColor = p.Header;
            logFrame.BackColor = p.Input;
            logBox.BackColor = p.Input;
            logBox.ForeColor = p.Text;
            foreach (var m in new[] { moreMenu, modMenu }) { m.BackColor = p.Surface; m.ForeColor = p.Text; }
            foreach (var (item, glyph) in menuGlyphs) item.Image = MenuRenderer.GlyphImage(glyph, p.TextMuted, S(20));
            if (IsHandleCreated) { Theme.ApplyTitleBar(this); Theme.ApplyNativeTheme(logBox); }
            Invalidate(true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyTitleBar(this);
            Theme.ApplyNativeTheme(logBox);
        }

        // ================= Data =================

        void Reload(bool firstLoad)
        {
            try
            {
                var c0 = ToolConfig.Load(Path.Combine(GameEnv.DataDirFor(toolDir), "ModLoader.config.json"));
                env = GameEnv.Detect(toolDir, c0.GameDir, c0.WorkshopDir);
                var lc = LauncherConfig.Load(GameEnv.LauncherModsYml);
                catalog = env.WorkshopFound || lc.Entries.Count > 0 ? ModCatalog.Scan(env, lc) : new List<ModInfo>();
                cfg = c0;
                if (!cfg.Exists)
                {
                    int n = cfg.ImportFromLauncher(catalog);
                    SaveConfig();
                    firstStart = firstLoad;
                    Log("First start: took over the mods that are switched on in the Mod Launcher (" + n + ").");
                }
                else cfg.Reconcile(catalog);
                var man = Builder.ReadManifest(env);
                last = ReportData.FromManifest(man);
                lastFingerprint = man?.Str("fingerprint");
                if (firstLoad) RefreshOutdatedReport();
                header.Version = "Version " + Builder.ToolVersion;
                if (firstLoad)
                {
                    Log("TG3 ModLoader " + Builder.ToolVersion + " - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                    Log("Game folder:     " + (env.GameDir ?? "not found"));
                    Log("Workshop folder: " + (env.WorkshopDir ?? "not found"));
                    if (env.InstalledFromWorkshop) Log("Settings and the combined mod: " + env.DataDir + " (this copy is Steam Workshop item " + env.WorkshopItemId + ")");
                }
                Log(catalog.Count + " mod(s) found, " + SelectedCount + " selected for combining." +
                    (last == null ? " Not combined yet." : " Last combine: " + last.BuiltAt.ToString("yyyy-MM-dd HH:mm") + "."));
                FillMods();
                FillConflicts();
                statusKnown = false;
                UpdateStatus();
                ComputeStatus();
                if (firstLoad) { CheckLauncherState(); OfferShortcut(); }
            }
            catch (Exception ex)
            {
                Log("Error while scanning: " + ex);
                ThemedDialog.Show(this, BannerKind.Danger, "Something went wrong", ex.Message);
            }
        }

        /// <summary>After an update of the ModLoader the report file on disk still has the old layout - write it again.</summary>
        void RefreshOutdatedReport()
        {
            if (last == null) return;
            try
            {
                var report = new FileInfo(env.ReportPath);
                if (report.Exists && report.LastWriteTimeUtc >= File.GetLastWriteTimeUtc(Application.ExecutablePath)) return;
                ReportWriter.Write(env.ReportPath, last, env);
            }
            catch (Exception ex) { Log("Could not update the report: " + ex.Message); }
        }

        void CheckLauncherState()
        {
            var foreign = catalog.Where(m => m.LauncherEnabled && m.Mergeable && !cfg.Mods.Any(s => s.Enabled && s.Id.Equals(m.Id, StringComparison.OrdinalIgnoreCase))).ToList();
            if (foreign.Count == 0) return;
            var r = ThemedDialog.Show(this, BannerKind.Neutral, "Add the mods from the Mod Launcher?",
                "These gameplay mods are switched on in the official Mod Launcher but not ticked here. The Mod Launcher can only start them through the ModLoader:",
                foreign.Select(m => new ThemedDialog.Line(Theme.GCheck, Theme.P.Accent, m.Name)),
                new ThemedDialog.Choice("Not now", DialogResult.No), new ThemedDialog.Choice("Add them", DialogResult.Yes, ButtonKind.Primary));
            if (r == DialogResult.Yes) ImportLauncher();
        }

        ModInfo Info(string id) => catalog.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        ModStats StatsFor(string id) => last?.Mods.FirstOrDefault(m => m.Id != null && m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        void FillMods(ModSelection keep = null)
        {
            var rows = new List<ModRow>();
            foreach (var s in cfg.Mods)
            {
                var m = Info(s.Id);
                if (m == null) continue;
                rows.Add(new ModRow { Sel = s, Info = m, Stats = StatsFor(s.Id) });
            }
            modList.SetRows(rows, keep ?? modList.SelectedRow?.Sel);
            int sel = rows.Count(r => r.Checked), total = rows.Count(r => r.CanCheck);
            footerInfo.Text = total == 0 ? "" : sel + " of " + total + " gameplay mods selected";
        }

        string SubtitleFor(ModRow r)
        {
            if (r.Info.Missing) return "Not downloaded - is it still subscribed in the Steam Workshop?";
            if (r.Info.Kind == ModKind.Map) return "Map - switch it on in the official Mod Launcher (maps never conflict)";
            if (r.Info.Kind == ModKind.Empty) return "This mod contains no files";
            if (!r.Checked) return r.Info.Kind == ModKind.Mixed ? "Map and gameplay changes - tick it to include it" : "Not included - tick it to include it";
            var st = r.Stats;
            if (st == null || last == null) return "Included in the next combine";
            if (!last.Complete) return "Part of your combined mods";
            var parts = new List<string> { "Changes " + st.ChangedFiles + " game file" + (st.ChangedFiles == 1 ? "" : "s") };
            int total = st.ConflictsWon + st.ConflictsLost;
            parts.Add(total == 0 ? "no conflicts" : "wins " + st.ConflictsWon + " of " + total + " conflict" + (total == 1 ? "" : "s"));
            return string.Join("  ·  ", parts);
        }

        void MoveMod(ModRow row, int newIndex)
        {
            if (busy) return;
            var shown = modList.Rows.Select(r => r.Sel).ToList();
            shown.Remove(row.Sel);
            shown.Insert(Math.Max(0, Math.Min(shown.Count, newIndex)), row.Sel);
            var hidden = cfg.Mods.Where(m => !shown.Contains(m)).ToList();
            cfg.Mods = shown.Concat(hidden).ToList();
            SaveConfig();
            FillMods(row.Sel);
            FillConflicts();
            ScheduleStatus();
        }

        void SelectAll(bool on)
        {
            foreach (var s in cfg.Mods)
            {
                var m = Info(s.Id);
                s.Enabled = on && m != null && m.Mergeable && !m.Missing;
            }
            SaveConfig();
            FillMods();
            ScheduleStatus();
        }

        void ImportLauncher()
        {
            if (busy) return;
            var lc = LauncherConfig.Load(GameEnv.LauncherModsYml);
            catalog = ModCatalog.Scan(env, lc);
            int n = cfg.ImportFromLauncher(catalog);
            SaveConfig();
            FillMods();
            FillConflicts();
            ScheduleStatus();
            Log(n + " mod(s) taken over from the Mod Launcher; order aligned with the launcher.");
        }

        void SaveConfig()
        {
            try { cfg.Save(env.ConfigPath); }
            catch (Exception ex) { Log("Could not save the settings: " + ex.Message); }
        }

        // ================= Conflicts =================

        void FillConflicts()
        {
            var items = new List<ConflictItem>();
            if (last != null && last.Success)
            {
                foreach (var c in last.Conflicts.OrderBy(c => Friendly.Area(c.File)).ThenBy(c => c.File).ThenBy(c => c.Path))
                    items.Add(BuildItem(c));
            }
            conflictList.SetItems(items);
            tabs.Counts[1] = items.Count;
            tabs.Attention.Clear();
            if (items.Any(i => i.Pending)) tabs.Attention.Add(1);
            tabs.Invalidate();
        }

        ConflictItem BuildItem(Conflict c)
        {
            var (title, context) = Friendly.Setting(c.Path);
            var it = new ConflictItem { C = c, Title = title, Context = context, Area = Friendly.Area(c.File) };
            var vanilla = c.Candidates.FirstOrDefault(x => x.ModName == "Vanilla");
            bool scalar = c.Kind == "value" && c.Candidates.All(x => ConflictPolicy.IsValidScalar(x.Text));
            foreach (var cand in c.Candidates.Where(x => x.ModName != "Vanilla"))
            {
                var sel = cfg.Mods.FirstOrDefault(m => m.Name == cand.ModName);
                it.Options.Add(new ConflictOption
                {
                    Label = cand.ModName,
                    ModId = sel?.Id,
                    Value = c.Kind == "file" ? "own version of the file" : cand.Text == "(removed)" ? "removes it" : Friendly.Value(cand.Text)
                });
            }
            if (scalar && vanilla.ModName != null)
                it.Options.Add(new ConflictOption { Label = "Original game", Value = Friendly.Value(vanilla.Text), RawValue = vanilla.Text, IsVanilla = true });
            if (scalar) it.Options.Add(new ConflictOption { Label = "Your own value…", Value = "", IsCustom = true });

            // Current choice: a decision of the user, otherwise the mod highest in the current list.
            cfg.ConflictOverrides.TryGetValue(c.Id, out var decision);
            ConflictOption chosen = null;
            if (decision != null && decision.StartsWith(ConflictPolicy.CustomPrefix))
            {
                var v = decision.Substring(ConflictPolicy.CustomPrefix.Length);
                chosen = it.Options.FirstOrDefault(o => o.IsVanilla && o.RawValue != null && o.RawValue.Trim() == v.Trim());
                if (chosen == null)
                {
                    chosen = it.Options.FirstOrDefault(o => o.IsCustom);
                    if (chosen != null) { chosen.Value = Friendly.Value(v); chosen.Label = "Your own value"; }
                }
            }
            else if (decision != null) chosen = it.Options.FirstOrDefault(o => o.ModId == decision);
            bool byUser = chosen != null;
            if (chosen == null) chosen = DefaultWinner(it);
            if (chosen != null) chosen.Chosen = true;
            it.Reason = byUser ? "Chosen by you" : chosen != null ? "Used because " + chosen.Label + " is higher in your list" : "";

            // Pending: the current choice differs from what the last combine used.
            string usedNow = chosen == null ? null : chosen.IsCustom || chosen.IsVanilla ? ConflictPolicy.CustomPrefix + (chosen.IsVanilla ? chosen.RawValue : CustomValue(decision)) : chosen.ModId;
            string usedThen = c.WinnerId;
            if (usedThen != null && usedNow != null)
            {
                if (usedThen.StartsWith(ConflictPolicy.CustomPrefix) && usedNow.StartsWith(ConflictPolicy.CustomPrefix))
                    it.Pending = CustomValue(usedThen).Trim() != CustomValue(usedNow).Trim();
                else it.Pending = usedThen != usedNow;
            }
            return it;
        }

        static string CustomValue(string decision) =>
            decision != null && decision.StartsWith(ConflictPolicy.CustomPrefix) ? decision.Substring(ConflictPolicy.CustomPrefix.Length) : null;

        ConflictOption DefaultWinner(ConflictItem it)
        {
            foreach (var s in cfg.Mods)
            {
                var o = it.Options.FirstOrDefault(x => x.ModId == s.Id && !x.IsVanilla && !x.IsCustom);
                if (o != null) return o;
            }
            return it.Options.FirstOrDefault(o => !o.IsVanilla && !o.IsCustom);
        }

        void OnConflictOption(ConflictItem it, ConflictOption o)
        {
            if (busy) return;
            string id = it.C.Id;
            if (o.IsCustom)
            {
                var vanilla = it.Options.FirstOrDefault(x => x.IsVanilla);
                cfg.ConflictOverrides.TryGetValue(id, out var cur);
                string initial = CustomValue(cur) ?? it.Options.FirstOrDefault(x => x.Chosen)?.Value;
                var v = ThemedDialog.Ask(this, "Your own value", "Enter the value to use for “" + it.Title + "”." + (vanilla != null ? " The original game uses " + vanilla.Value + "." : ""), initial);
                if (v == null) return;
                if (!ConflictPolicy.IsValidScalar(v))
                {
                    ThemedDialog.Show(this, BannerKind.Warning, "That doesn't look like a single value", "Please enter one value, for example 25, 0.5 or \"Text\".");
                    return;
                }
                cfg.ConflictOverrides[id] = ConflictPolicy.CustomPrefix + v;
            }
            else if (o.IsVanilla) cfg.ConflictOverrides[id] = ConflictPolicy.CustomPrefix + o.RawValue.Trim();
            else
            {
                var def = DefaultWinner(it);
                if (def != null && def.ModId == o.ModId) cfg.ConflictOverrides.Remove(id);
                else if (o.ModId != null) cfg.ConflictOverrides[id] = o.ModId;
            }
            SaveConfig();
            FillConflicts();
            ScheduleStatus();
        }

        // ================= Status =================

        void ScheduleStatus()
        {
            // Until the check is done the old state ("Ready to play") may be wrong.
            statusKnown = false;
            statusTimer.Stop();
            statusTimer.Start();
            UpdateStatus();
        }

        void ComputeStatus()
        {
            int request = ++statusRequest;
            var snapshot = cfg.Clone();
            var e = env;
            Task.Run(() => { try { return new Builder(e, snapshot, null).CurrentFingerprint(); } catch { return null; } }).ContinueWith(t =>
            {
                if (request != statusRequest || IsDisposed) return;
                upToDate = t.Result != null && t.Result == lastFingerprint;
                statusKnown = true;
                UpdateStatus();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        int SelectedCount => cfg?.Mods.Count(s => s.Enabled && (Info(s.Id)?.Mergeable ?? false) && !(Info(s.Id)?.Missing ?? true)) ?? 0;

        bool LauncherEntryReady()
        {
            var own = LauncherConfig.Load(GameEnv.LauncherModsYml).FindFolder(env.OutputModDir);
            return own != null && own.Enabled;
        }

        void UpdateStatus()
        {
            if (busy || env == null || cfg == null) return;
            int sel = SelectedCount;
            bool ready = statusKnown && upToDate && last != null && sel > 0;
            btnPrimary.Text = ready ? "Play" : "Combine & Play";
            btnPrimary.Glyph = Theme.GPlay;
            btnPrimary.Enabled = env.GameFound && sel > 0;
            btnCombine.Enabled = env.GameFound && sel > 0;
            LayoutFooter();

            if (transientTitle != null && DateTime.Now < transientUntil) { banner.Set(BannerKind.Success, transientTitle, transientDetail); return; }
            transientTitle = null;

            if (!env.GameFound)
            {
                banner.Set(BannerKind.Danger, "The Guild 3 was not found",
                    "Put the TG3ModLoader folder into the Steam Workshop folder of The Guild 3 (...\\steamapps\\workshop\\content\\311260), or choose the game folder.", "Choose game folder");
                return;
            }
            if (sel == 0)
            {
                banner.Set(BannerKind.Neutral, firstStart ? "Welcome!" : "Choose your mods", "Tick the mods you want to play with below, then click Combine & Play.");
                return;
            }
            if (!statusKnown) { banner.Set(BannerKind.Neutral, "Checking your mods…", sel + " mod" + (sel == 1 ? "" : "s") + " selected."); return; }

            var notes = new List<string>();
            if (last != null && last.Warnings.Any(w => w.Contains("syntax error"))) notes.Add("One of your mods contains a small error of its own - see the report.");
            var deployLog = Path.Combine(GameEnv.LauncherDeployDir, "scripts.log");
            if (File.Exists(deployLog) && new FileInfo(deployLog).Length > 0) notes.Add("The last game start reported script messages - \"More > Test the game\" checks the combination.");
            string extra = notes.Count > 0 ? " " + string.Join(" ", notes) : "";

            if (last == null)
                banner.Set(BannerKind.Warning, "Not combined yet", sel + " mod" + (sel == 1 ? " is" : "s are") + " selected. Click Combine & Play to combine them and start the game." + extra);
            else if (!upToDate)
                banner.Set(BannerKind.Warning, "Please combine again",
                    "Your selection, the order, a conflict choice or a mod update has changed since the last combine. Click Combine & Play to update." + extra);
            else
            {
                int conflicts = last.Conflicts.Count;
                banner.Set(BannerKind.Success, "Ready to play",
                    sel + " mod" + (sel == 1 ? " is" : "s are") + " combined" + (conflicts > 0 ? " (" + conflicts + " conflict" + (conflicts == 1 ? "" : "s") + " decided)" : "") +
                    ". Click Play to open the official Mod Launcher, then click Play there." + extra);
            }
        }

        void SetBusy(bool on, string title = null, string detail = null)
        {
            busy = on;
            foreach (var c in busyLocked) c.Enabled = !on;
            UseWaitCursor = on;
            if (on) banner.Set(BannerKind.Busy, title, detail);
        }

        void Log(string s)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), s); return; }
            logBox.AppendText(s + Environment.NewLine);
            var progress = busy ? Progress(s) : null;
            if (progress != null) banner.SetDetail(progress);
        }

        static string Progress(string line)
        {
            if (line.StartsWith("Loading vanilla")) return "Reading the original game files…";
            var m = Regex.Match(line, @"\.\.\. (\d+)/(\d+) files processed");
            if (m.Success) return "Combining files (" + m.Groups[1].Value + " of " + m.Groups[2].Value + ")…";
            if (line.StartsWith("Mods to merge")) return "Looking at your mods…";
            if (line.StartsWith("Output mod updated")) return "Saving the combined mod…";
            if (line.StartsWith("Mod Launcher configuration")) return "Preparing the official Mod Launcher…";
            if (line.StartsWith("Starting:")) return "Starting the game…";
            if (line.StartsWith("Main menu reached")) return "Main menu reached - closing the game again…";
            return null;
        }

        // ================= Actions =================

        void PrimaryAction()
        {
            if (busy) return;
            if (btnPrimary.Text == "Play" && LauncherEntryReady()) StartLauncher();
            else Combine(true);
        }

        bool EnsureLauncherClosed()
        {
            if (!LauncherConfig.LauncherRunning()) return true;
            var r = ThemedDialog.Show(this, BannerKind.Neutral, "Close the Mod Launcher?",
                "The official Mod Launcher is open. The ModLoader has to close it to switch the combined mod on and the single mods off.",
                new ThemedDialog.Choice("Cancel", DialogResult.Cancel),
                new ThemedDialog.Choice("Continue without", DialogResult.No),
                new ThemedDialog.Choice("Close it", DialogResult.Yes, ButtonKind.Primary));
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.No) return true;
            foreach (var p in Process.GetProcessesByName("Guild3ModLauncher"))
            {
                try
                {
                    p.CloseMainWindow();
                    if (!p.WaitForExit(4000)) { p.Kill(); p.WaitForExit(4000); }
                }
                catch { }
            }
            return true;
        }

        void Combine(bool play)
        {
            if (busy) return;
            if (SelectedCount == 0 && play) return;
            if (Builder.GameUsesFolder(env.OutputModDir))
            {
                ThemedDialog.Show(this, BannerKind.Neutral, "The game is running", "The Guild 3 is running with the combined mod. Please close the game first.");
                return;
            }
            if (!EnsureLauncherClosed()) return;
            SaveConfig();
            SetBusy(true, "Combining your mods…", "This usually takes a few seconds.");
            Log("");
            Log("=== Combine " + DateTime.Now.ToString("HH:mm:ss") + " ===");
            var builder = new Builder(env, cfg, Log);
            Task.Run(() => builder.Build(false, true)).ContinueWith(t =>
            {
                SetBusy(false);
                if (t.IsFaulted)
                {
                    var ex = t.Exception.GetBaseException();
                    Log("Error: " + ex);
                    UpdateStatus();
                    ThemedDialog.Show(this, BannerKind.Danger, "Combining failed", ex.Message + "\n\nThe Activity tab has details.");
                    return;
                }
                var r = t.Result;
                foreach (var w in r.Warnings) Log("Note: " + w);
                foreach (var v in r.Violations) Log("Verification: " + v);
                catalog = ModCatalog.Scan(env, LauncherConfig.Load(GameEnv.LauncherModsYml));
                var man = Builder.ReadManifest(env);
                if (r.Success) { last = ReportData.FromManifest(man); lastFingerprint = man?.Str("fingerprint"); upToDate = true; statusKnown = true; }
                firstStart = false;
                FillMods();
                FillConflicts();
                if (!r.Success)
                {
                    UpdateStatus();
                    ThemedDialog.Show(this, BannerKind.Danger, "Combining failed", r.Error + "\n\nYour previous combination is unchanged. The Activity tab has details.");
                    return;
                }
                if (play && r.Violations.Count == 0 && StartLauncher())
                {
                    transientTitle = "Combined - the Mod Launcher is opening";
                    transientDetail = "Click Play in the official Mod Launcher to start the game." +
                                      (r.Conflicts.Count > 0 ? " " + r.Conflicts.Count + " conflict" + (r.Conflicts.Count == 1 ? " was" : "s were") + " decided - see the Conflicts tab." : "");
                    transientUntil = DateTime.Now.AddSeconds(20);
                    var clear = new Timer { Interval = 20500 };
                    clear.Tick += (s, a) => { clear.Stop(); clear.Dispose(); UpdateStatus(); };
                    clear.Start();
                    UpdateStatus();
                    ComputeStatus();
                    return;
                }
                UpdateStatus();
                ComputeStatus();
                ShowResult(r);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void ShowResult(BuildResult r)
        {
            var p = Theme.P;
            int n = r.Mods.Count;
            var lines = new List<ThemedDialog.Line>
            {
                new ThemedDialog.Line(Theme.GCheck, p.Success, n + " mod" + (n == 1 ? " was" : "s were") + " combined into one."),
                r.Violations.Count == 0
                    ? new ThemedDialog.Line(Theme.GCheck, p.Success, "Every change of every mod is included.")
                    : new ThemedDialog.Line(Theme.GWarning, p.Warning, r.Violations.Count + " change(s) could not be confirmed - see the Activity tab."),
                r.Conflicts.Count == 0
                    ? new ThemedDialog.Line(Theme.GCheck, p.Success, "No conflicts between your mods.")
                    : new ThemedDialog.Line(Theme.GInfo, p.Info, r.Conflicts.Count + " setting" + (r.Conflicts.Count == 1 ? " is" : "s are") + " changed by several mods - " +
                        (r.Conflicts.Any(c => c.ByOverride) ? "your choices and the order of your list decide which value is used." : "the value of the mod higher in your list is used.") +
                        " See the Conflicts tab.")
            };
            if (r.LauncherUpdated) lines.Add(new ThemedDialog.Line(Theme.GCheck, p.Success, "The Mod Launcher is prepared: the combined mod is switched on, the single mods are switched off."));
            foreach (var w in r.Warnings.Take(3)) lines.Add(new ThemedDialog.Line(Theme.GWarning, p.Warning, Friendly.Warning(w)));
            var choices = File.Exists(env.LauncherExe)
                ? new[] { new ThemedDialog.Choice("Close", DialogResult.Cancel), new ThemedDialog.Choice("Play now", DialogResult.OK, ButtonKind.Primary) }
                : new[] { new ThemedDialog.Choice("Close", DialogResult.Cancel, ButtonKind.Primary) };
            var res = ThemedDialog.Show(this, r.Violations.Count == 0 ? BannerKind.Success : BannerKind.Warning, "Your mods are combined", null, lines, choices);
            if (res == DialogResult.OK) StartLauncher();
        }

        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);

        bool StartLauncher()
        {
            var running = Process.GetProcessesByName("Guild3ModLauncher").FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
            if (running != null) { ShowWindow(running.MainWindowHandle, 9); SetForegroundWindow(running.MainWindowHandle); return true; }
            if (!File.Exists(env.LauncherExe))
            {
                ThemedDialog.Show(this, BannerKind.Warning, "Mod Launcher not found", "The official Mod Launcher was not found at:\n" + env.LauncherExe);
                return false;
            }
            Program.StartLauncher(env, Log);
            return true;
        }

        void OpenReport()
        {
            var d = env == null ? null : ReportData.FromManifest(Builder.ReadManifest(env));
            if (d == null)
            {
                ThemedDialog.Show(this, BannerKind.Neutral, "No report yet", "The report describes your last combination. Combine your mods first.");
                return;
            }
            try
            {
                ReportWriter.Write(env.ReportPath, d, env);
                OpenPath(env.ReportPath);
            }
            catch (Exception ex) { ThemedDialog.Show(this, BannerKind.Danger, "The report could not be created", ex.Message); }
        }

        void RunTest()
        {
            if (busy) return;
            if (!Directory.Exists(env.OutputModDir)) { ThemedDialog.Show(this, BannerKind.Neutral, "Nothing to test yet", "Combine your mods first."); return; }
            var ok = ThemedDialog.Show(this, BannerKind.Neutral, "Test the game?",
                "The ModLoader starts The Guild 3 with your combined mods, waits until the main menu is loaded, checks for script errors and crashes, and closes the game again. This can take a few minutes.",
                new ThemedDialog.Choice("Cancel", DialogResult.Cancel), new ThemedDialog.Choice("Start test", DialogResult.OK, ButtonKind.Primary));
            if (ok != DialogResult.OK) return;
            SetBusy(true, "Testing the game…", "The game starts, loads the main menu and closes again.");
            Log("");
            Log("=== Test run " + DateTime.Now.ToString("HH:mm:ss") + " ===");
            Task.Run(() => TestRun.Run(env, env.OutputModDir, 600, Log)).ContinueWith(t =>
            {
                SetBusy(false);
                UpdateStatus();
                if (t.IsFaulted)
                {
                    var ex = t.Exception.GetBaseException();
                    Log("Error: " + ex);
                    ThemedDialog.Show(this, BannerKind.Danger, "The test could not run", ex.Message + "\n\nThe Activity tab has details.");
                    return;
                }
                var r = t.Result;
                Log("Result: " + r.Summary);
                if (!string.IsNullOrWhiteSpace(r.ScriptsLog)) Log("scripts.log:" + Environment.NewLine + r.ScriptsLog);
                if (r.Ok)
                    ThemedDialog.Show(this, BannerKind.Success, "The game works with your mods",
                        "The main menu was loaded without script errors or crashes (" + r.SecondsToMenu.ToString("0") + " s).");
                else
                    ThemedDialog.Show(this, BannerKind.Warning, "The test found a problem", r.Summary + (string.IsNullOrWhiteSpace(r.DialogText) ? "" : "\n\nMessage of the game: " + r.DialogText) + "\n\nThe Activity tab has details.");
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void ChooseGameFolder()
        {
            using (var d = new FolderBrowserDialog { Description = "Choose the folder of The Guild 3 (the folder that contains Guild3.exe)" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (!File.Exists(Path.Combine(d.SelectedPath, "Guild3.exe")))
                {
                    ThemedDialog.Show(this, BannerKind.Warning, "Guild3.exe was not found", "Please choose the game folder that contains Guild3.exe.");
                    return;
                }
                var path = Path.Combine(GameEnv.DataDirFor(toolDir), "ModLoader.config.json");
                var c = ToolConfig.Load(path);
                c.GameDir = d.SelectedPath;
                c.Save(path);
                Reload(false);
            }
        }

        void Unregister()
        {
            if (busy) return;
            var r = ThemedDialog.Show(this, BannerKind.Neutral, "Remove the ModLoader from the Mod Launcher?",
                "The combined mod is removed from the official Mod Launcher and your mods are switched on there again (only one gameplay mod, because the Mod Launcher cannot load more).",
                new ThemedDialog.Choice("Cancel", DialogResult.Cancel), new ThemedDialog.Choice("Remove", DialogResult.OK, ButtonKind.Primary));
            if (r != DialogResult.OK || !EnsureLauncherClosed()) return;
            bool ok = new Builder(env, cfg, Log).Unregister(out var msg);
            Log(msg);
            Reload(false);
            ThemedDialog.Show(this, ok ? BannerKind.Success : BannerKind.Warning, ok ? "Removed" : "Not removed", msg);
        }

        void ShowHelp()
        {
            var p = Theme.P;
            var res = ThemedDialog.Show(this, BannerKind.Neutral, "How it works",
                "The official Mod Launcher can only start one gameplay mod at a time. The ModLoader combines all your mods into one, so they work together.",
                new[]
                {
                    new ThemedDialog.Line("", p.Accent, "Tick the mods you want to play with."),
                    new ThemedDialog.Line(Theme.GUp, p.Accent, "Drag them into order. If two mods change the same setting, the mod higher up wins - or pick the winner yourself on the Conflicts tab."),
                    new ThemedDialog.Line(Theme.GPlay, p.Accent, "Click Combine & Play. The ModLoader combines your mods, prepares the official Mod Launcher and opens it - just click Play there."),
                    new ThemedDialog.Line(Theme.GInfo, p.Info, "Maps don't need combining - switch them on in the Mod Launcher."),
                    new ThemedDialog.Line(Theme.GRefresh, p.Info, "After a mod update in the Steam Workshop, combine again - the ModLoader tells you when.")
                },
                new ThemedDialog.Choice("Read the full guide", DialogResult.Retry), new ThemedDialog.Choice("Got it", DialogResult.OK, ButtonKind.Primary));
            if (res == DialogResult.Retry) OpenReadme();
        }

        void ShowModMenu(ModRow row, Point pt)
        {
            var old = modMenu.Items.Cast<ToolStripItem>().ToList();
            modMenu.Items.Clear();
            menuGlyphs.RemoveAll(x => x.Item.Owner == null);
            foreach (var item in old) { item.Image?.Dispose(); item.Dispose(); }
            AddItem(modMenu, row.Checked ? "Don't include" : "Include", Theme.GCheck, (s, e) => { if (row.CanCheck) { row.Sel.Enabled = !row.Sel.Enabled; SaveConfig(); FillMods(row.Sel); ScheduleStatus(); } }).Enabled = row.CanCheck && !busy;
            AddItem(modMenu, "Move to top", Theme.GUp, (s, e) => MoveMod(row, 0)).Enabled = !busy;
            AddItem(modMenu, "Move to bottom", Theme.GDown, (s, e) => MoveMod(row, modList.Rows.Count - 1)).Enabled = !busy;
            modMenu.Items.Add(new ToolStripSeparator());
            AddItem(modMenu, "Open mod folder", Theme.GFolder, (s, e) => OpenPath(row.Info.Dir)).Enabled = row.Info.Dir != null && Directory.Exists(row.Info.Dir);
            if (row.Info.IsWorkshop && row.Info.Id.All(char.IsDigit))
                AddItem(modMenu, "Open the Steam Workshop page", Theme.GGlobe, (s, e) => OpenUrl("https://steamcommunity.com/sharedfiles/filedetails/?id=" + row.Info.Id));
            modMenu.Show(modList, pt);
        }

        void OpenReadme()
        {
            foreach (var name in new[] { "TG3ModLoader-README.txt", "README.txt" })
            {
                var p = Path.Combine(toolDir, name);
                if (File.Exists(p)) { OpenPath(p); return; }
            }
            OpenUrl(ReportWriter.ProjectUrl);
        }

        /// <summary>A copy from the Steam Workshop sits deep inside the Steam folder - offer a desktop shortcut once.</summary>
        void OfferShortcut()
        {
            if (!env.InstalledFromWorkshop || cfg.ShortcutOffered) return;
            cfg.ShortcutOffered = true;
            SaveConfig();
            if (File.Exists(ShellShortcut.DesktopPath("TG3 ModLoader"))) return;
            var r = ThemedDialog.Show(this, BannerKind.Neutral, "Create a desktop shortcut?",
                "You got the ModLoader from the Steam Workshop, so it lives deep inside the Steam folder. With a shortcut on your desktop you can start it with a double-click. (You can also do this later under More.)",
                new ThemedDialog.Choice("No, thanks", DialogResult.No), new ThemedDialog.Choice("Create shortcut", DialogResult.Yes, ButtonKind.Primary));
            if (r == DialogResult.Yes) CreateShortcut(false);
        }

        void CreateShortcut(bool confirm)
        {
            var lnk = ShellShortcut.DesktopPath("TG3 ModLoader");
            try
            {
                ShellShortcut.Create(lnk, Application.ExecutablePath, "Play several mods for The Guild 3 together");
                Log("Desktop shortcut created: " + lnk);
                if (confirm) ThemedDialog.Show(this, BannerKind.Success, "Shortcut created", "You can now start the ModLoader with \u201CTG3 ModLoader\u201D on your desktop.");
            }
            catch (Exception ex)
            {
                Log("Could not create the desktop shortcut: " + ex.Message);
                ThemedDialog.Show(this, BannerKind.Warning, "The shortcut could not be created", ex.Message);
            }
        }

        void OpenPath(string p)
        {
            if (string.IsNullOrEmpty(p) || (!File.Exists(p) && !Directory.Exists(p))) { Log("Does not exist: " + p); return; }
            try { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true }); }
            catch (Exception ex) { Log(ex.Message); }
        }

        void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Log(ex.Message); }
        }

        // ================= Icon (also fixes the taskbar icon) =================

        static Icon LoadAppIcon(int size)
        {
            try
            {
                using (var s = typeof(MainForm).Assembly.GetManifestResourceStream("TG3ModLoader.app.ico"))
                    if (s != null) return new Icon(s, size, size);
            }
            catch { }
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { return null; }
        }

        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        Icon taskbarBig, taskbarSmall;

        /// <summary>
        /// The Windows 11 taskbar keeps a generic icon for a new window and only picks up an icon that is set
        /// after the window became visible - so the icon is sent again once the window is shown.
        /// </summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyTaskbarIcon();
            var t = new Timer { Interval = 1000 };
            t.Tick += (s, a) => { t.Stop(); t.Dispose(); ApplyTaskbarIcon(); };
            t.Start();
        }

        void ApplyTaskbarIcon()
        {
            if (!IsHandleCreated) return;
            if (taskbarBig == null)
            {
                taskbarBig = LoadAppIcon(SystemInformation.IconSize.Width);
                taskbarSmall = LoadAppIcon(SystemInformation.SmallIconSize.Width);
                if (taskbarBig == null || taskbarSmall == null) return;
            }
            const int WM_SETICON = 0x80;
            SendMessage(Handle, WM_SETICON, (IntPtr)1, taskbarBig.Handle);
            SendMessage(Handle, WM_SETICON, (IntPtr)0, taskbarSmall.Handle);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy && ThemedDialog.Show(this, BannerKind.Warning, "Still working", "The ModLoader is still busy. Close it anyway?",
                    new ThemedDialog.Choice("Keep open", DialogResult.Cancel), new ThemedDialog.Choice("Close", DialogResult.OK, ButtonKind.Primary)) != DialogResult.OK)
                e.Cancel = true;
            base.OnFormClosing(e);
        }

        /// <summary>Header with the program icon, the name and a short description.</summary>
        sealed class HeaderBar : Painted
        {
            public Bitmap Logo;
            public string Version = "";

            public HeaderBar() { Background = () => Theme.P.Header; }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                Theme.Smooth(g);
                var p = Theme.P;
                int x = S(24);
                if (Logo != null)
                {
                    int sz = S(52);
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(Logo, new Rectangle(x, (Height - sz) / 2, sz, sz));
                    x += sz + S(16);
                }
                int mid = Height / 2;
                TextRenderer.DrawText(g, "TG3 ModLoader", Theme.Title, new Rectangle(x, mid - S(28), Width / 2, S(32)), p.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, "Play several mods for The Guild 3 together", Theme.Body, new Rectangle(x + S(1), mid + S(4), Width / 2, S(22)), p.TextMuted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                int tw = Measure("TG3 ModLoader", Theme.Title);
                TextRenderer.DrawText(g, Version, Theme.Small, new Rectangle(x + tw + S(8), mid - S(22), S(120), S(24)), p.TextFaint, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                using (var pen = new Pen(p.Border)) g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            }
        }
    }
}
