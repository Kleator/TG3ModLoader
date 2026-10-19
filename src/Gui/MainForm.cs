using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using TG3ModLoader.Core;
using TG3ModLoader.Merge;

namespace TG3ModLoader.Gui
{
    /// <summary>Main window: mod selection and order, merge, conflicts, test run and launcher integration.</summary>
    public sealed class MainForm : Form
    {
        readonly string toolDir;
        GameEnv env;
        ToolConfig cfg;
        List<ModInfo> catalog = new List<ModInfo>();
        List<Conflict> conflicts = new List<Conflict>();
        bool busy;
        bool filling;

        readonly Label lblPaths = new Label();
        readonly Label lblStatus = new Label();
        readonly ListView lvMods = new ListView();
        readonly ListView lvConflicts = new ListView();
        readonly TextBox txtLog = new TextBox();
        readonly TabControl tabs = new TabControl();
        readonly ProgressBar progress = new ProgressBar();
        readonly List<Button> actionButtons = new List<Button>();

        public MainForm(string toolDir)
        {
            this.toolDir = toolDir;
            Text = "TG3 ModLoader " + Builder.ToolVersion + " - merge mods for The Guild 3";
            Font = new Font("Segoe UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            Size = new Size(1180, 760);
            MinimumSize = new Size(900, 560);
            StartPosition = FormStartPosition.CenterScreen;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            BuildLayout();
            Load += (s, e) => Reload(true);
        }

        // ---------------- Layout ----------------

        void BuildLayout()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 74, Padding = new Padding(12, 8, 12, 4), BackColor = Color.FromArgb(38, 44, 52) };
            var title = new Label { Text = "TG3 ModLoader", ForeColor = Color.White, Font = new Font("Segoe UI Semibold", 15f), AutoSize = true, Location = new Point(12, 6) };
            lblPaths.ForeColor = Color.FromArgb(200, 205, 210);
            lblPaths.AutoSize = false;
            lblPaths.Location = new Point(14, 40);
            lblPaths.Size = new Size(1100, 30);
            lblPaths.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            header.Controls.Add(title);
            header.Controls.Add(lblPaths);

            // Mods tab
            var tabMods = new TabPage("Mods");
            var info = new Label
            {
                Dock = DockStyle.Top,
                Height = 48,
                Padding = new Padding(4, 6, 4, 0),
                Text = "Checked = the mod is merged. The mod at the top has the highest priority - it wins when two mods change the same value differently. " +
                       "All other changes of all mods are kept completely. Map-only mods are still enabled directly in the Mod Launcher."
            };
            lvMods.Dock = DockStyle.Fill;
            lvMods.View = View.Details;
            lvMods.CheckBoxes = true;
            lvMods.FullRowSelect = true;
            lvMods.HideSelection = false;
            lvMods.MultiSelect = false;
            lvMods.Columns.Add("#", 40);
            lvMods.Columns.Add("Mod", 300);
            lvMods.Columns.Add("Type", 170);
            lvMods.Columns.Add("Workshop ID / folder", 170);
            lvMods.Columns.Add("Files", 80);
            lvMods.Columns.Add("In Mod Launcher", 140);
            lvMods.ItemCheck += LvMods_ItemCheck;
            lvMods.ItemChecked += (s, e) => { if (!filling) SelectionChanged(); };

            var side = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 210, FlowDirection = FlowDirection.TopDown, Padding = new Padding(6), WrapContents = false };
            side.Controls.Add(SideButton("▲  Move up (higher priority)", (s, e) => MoveSelected(-1)));
            side.Controls.Add(SideButton("▼  Move down (lower priority)", (s, e) => MoveSelected(1)));
            side.Controls.Add(Spacer());
            side.Controls.Add(SideButton("Take over from Mod Launcher", (s, e) => ImportLauncher()));
            side.Controls.Add(SideButton("Select all content mods", (s, e) => SelectAll(true)));
            side.Controls.Add(SideButton("Clear selection", (s, e) => SelectAll(false)));
            side.Controls.Add(Spacer());
            side.Controls.Add(SideButton("Rescan", (s, e) => Reload(false)));
            side.Controls.Add(SideButton("Open mod folder", (s, e) => OpenSelectedFolder()));
            side.Controls.Add(SideButton("Open output folder", (s, e) => OpenPath(env?.OutputModDir)));
            side.Controls.Add(Spacer());
            side.Controls.Add(SideButton("Remove from Mod Launcher", (s, e) => UnregisterFromLauncher()));
            tabMods.Controls.Add(lvMods);
            tabMods.Controls.Add(side);
            tabMods.Controls.Add(info);

            // Conflicts tab
            var tabConf = new TabPage("Conflicts");
            var confInfo = new Label
            {
                Dock = DockStyle.Top,
                Height = 40,
                Padding = new Padding(4, 6, 4, 0),
                Text = "Real conflicts of the last merge: several mods change the same value differently. By default the mod further up in the list wins. " +
                       "Use \"Choose winner\" to pick a different mod for a location (then merge again)."
            };
            lvConflicts.Dock = DockStyle.Fill;
            lvConflicts.View = View.Details;
            lvConflicts.FullRowSelect = true;
            lvConflicts.HideSelection = false;
            lvConflicts.Columns.Add("File", 250);
            lvConflicts.Columns.Add("Location", 330);
            lvConflicts.Columns.Add("Variants", 380);
            lvConflicts.Columns.Add("Winner", 150);
            lvConflicts.DoubleClick += (s, e) => ChooseWinner();
            var confSide = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 210, FlowDirection = FlowDirection.TopDown, Padding = new Padding(6), WrapContents = false };
            confSide.Controls.Add(SideButton("Choose winner ...", (s, e) => ChooseWinner()));
            confSide.Controls.Add(SideButton("Remove choice", (s, e) => RemoveOverride()));
            confSide.Controls.Add(SideButton("Remove all choices", (s, e) => { cfg.ConflictOverrides.Clear(); SaveConfig(); FillConflicts(); UpdateStatus(); }));
            tabConf.Controls.Add(lvConflicts);
            tabConf.Controls.Add(confSide);
            tabConf.Controls.Add(confInfo);

            // Log tab
            var tabLog = new TabPage("Log");
            txtLog.Dock = DockStyle.Fill;
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Both;
            txtLog.WordWrap = false;
            txtLog.Font = new Font("Consolas", 9f);
            txtLog.BackColor = Color.White;
            tabLog.Controls.Add(txtLog);

            tabs.Dock = DockStyle.Fill;
            tabs.TabPages.Add(tabMods);
            tabs.TabPages.Add(tabConf);
            tabs.TabPages.Add(tabLog);

            // Bottom bar
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 92, Padding = new Padding(10, 6, 10, 8) };
            lblStatus.Dock = DockStyle.Top;
            lblStatus.Height = 36;
            lblStatus.Font = new Font("Segoe UI Semibold", 9.5f);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            buttons.Controls.Add(ActionButton("Merge", (s, e) => RunBuild(false), 150, true));
            buttons.Controls.Add(ActionButton("Merge && start Mod Launcher", (s, e) => RunBuild(true), 240, true));
            buttons.Controls.Add(ActionButton("Test run (check game)", (s, e) => RunTest(), 180, false));
            buttons.Controls.Add(ActionButton("Open report", (s, e) => OpenPath(env?.ReportPath), 120, false));
            buttons.Controls.Add(ActionButton("Start Mod Launcher", (s, e) => Program.StartLauncher(env, Log), 160, false));
            progress.Style = ProgressBarStyle.Marquee;
            progress.Width = 120;
            progress.Height = 22;
            progress.Margin = new Padding(12, 10, 0, 0);
            progress.Visible = false;
            buttons.Controls.Add(progress);
            bottom.Controls.Add(buttons);
            bottom.Controls.Add(lblStatus);

            Controls.Add(tabs);
            Controls.Add(bottom);
            Controls.Add(header);
        }

        Button SideButton(string text, EventHandler click)
        {
            var b = new Button { Text = text, Width = 192, Height = 30, TextAlign = ContentAlignment.MiddleLeft };
            b.Click += click;
            actionButtons.Add(b);
            return b;
        }

        static Control Spacer() => new Label { Height = 10, Width = 10 };

        Button ActionButton(string text, EventHandler click, int width, bool primary)
        {
            var b = new Button { Text = text, Width = width, Height = 34, Margin = new Padding(0, 4, 8, 0) };
            if (primary)
            {
                b.BackColor = Color.FromArgb(46, 125, 50);
                b.ForeColor = Color.White;
                b.FlatStyle = FlatStyle.Flat;
                b.FlatAppearance.BorderSize = 0;
                b.Font = new Font("Segoe UI Semibold", 9.5f);
            }
            b.Click += click;
            actionButtons.Add(b);
            return b;
        }

        // ---------------- Data ----------------

        void Reload(bool firstLoad)
        {
            try
            {
                var c0 = ToolConfig.Load(Path.Combine(toolDir, "ModLoader.config.json"));
                env = GameEnv.Detect(toolDir, c0.GameDir, c0.WorkshopDir);
                lblPaths.Text = "Game: " + (env.GameFound ? env.GameDir : "NOT FOUND") + "     Workshop: " + (env.WorkshopFound ? env.WorkshopDir : "NOT FOUND") +
                                "\nOutput mod: " + env.OutputModDir;
                if (!env.GameFound)
                {
                    MessageBox.Show(this, "The Guild 3 was not found.\n\nPlease put the ModLoader folder into the Steam Workshop folder of the game " +
                                          "(...\\steamapps\\workshop\\content\\311260\\TG3ModLoader) or set \"gameDir\" in ModLoader.config.json.",
                        "TG3 ModLoader", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                var lc = LauncherConfig.Load(GameEnv.LauncherModsYml);
                catalog = env.WorkshopFound || lc.Entries.Count > 0 ? ModCatalog.Scan(env, lc) : new List<ModInfo>();
                cfg = c0;
                if (!cfg.Exists)
                {
                    int n = cfg.ImportFromLauncher(catalog);
                    SaveConfig();
                    if (firstLoad) Log("First start: selection and order taken over from the Mod Launcher (" + n + " content mod(s) selected).");
                }
                else cfg.Reconcile(catalog);
                FillMods();
                conflicts = Builder.ConflictsFromManifest(Builder.ReadManifest(env));
                FillConflicts();
                UpdateStatus();
                if (firstLoad) CheckLauncherState();
            }
            catch (Exception ex)
            {
                Log("Error while scanning: " + ex);
                MessageBox.Show(this, ex.Message, "TG3 ModLoader", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void CheckLauncherState()
        {
            var foreign = catalog.Where(m => m.LauncherEnabled && m.Mergeable && !cfg.Mods.Any(s => s.Enabled && s.Id.Equals(m.Id, StringComparison.OrdinalIgnoreCase))).ToList();
            if (foreign.Count == 0) return;
            var r = MessageBox.Show(this, "The following content mods are enabled in the official Mod Launcher but not merged by the ModLoader yet:\n\n" +
                                          string.Join("\n", foreign.Select(m => "  • " + m.Name)) +
                                          "\n\nAdd them to the selection?", "TG3 ModLoader", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r == DialogResult.Yes) ImportLauncher();
        }

        ModInfo Info(string id) => catalog.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

        void FillMods()
        {
            filling = true;
            lvMods.BeginUpdate();
            var selId = lvMods.SelectedItems.Count > 0 ? (lvMods.SelectedItems[0].Tag as ModSelection)?.Id : null;
            lvMods.Items.Clear();
            int n = 0;
            foreach (var s in cfg.Mods)
            {
                var m = Info(s.Id);
                if (m == null) continue;
                var it = new ListViewItem((++n).ToString()) { Tag = s, Checked = s.Enabled && m.Mergeable };
                it.SubItems.Add(s.Name);
                it.SubItems.Add(m.Kind == ModKind.Map ? "Map (enable in launcher)" : m.KindText);
                it.SubItems.Add(m.IsWorkshop ? m.Id : m.Dir);
                it.SubItems.Add(m.Missing ? "missing" : (m.MediaFiles + m.MapFiles).ToString());
                it.SubItems.Add(!m.InLauncher ? "-" : m.LauncherEnabled ? "enabled" : "disabled");
                if (!m.Mergeable) it.ForeColor = Color.Gray;
                if (m.Missing) it.ForeColor = Color.Firebrick;
                lvMods.Items.Add(it);
                if (s.Id == selId) it.Selected = true;
            }
            lvMods.EndUpdate();
            filling = false;
        }

        void LvMods_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (filling) return;
            var s = lvMods.Items[e.Index].Tag as ModSelection;
            var m = s != null ? Info(s.Id) : null;
            if (m == null || !m.Mergeable)
            {
                e.NewValue = CheckState.Unchecked;
                if (m != null && m.Kind == ModKind.Map)
                    Log("'" + m.Name + "' is a map-only mod - enable it directly in the Mod Launcher (maps do not collide).");
            }
        }

        void SelectionChanged()
        {
            foreach (ListViewItem it in lvMods.Items)
                if (it.Tag is ModSelection s) s.Enabled = it.Checked;
            SaveConfig();
            UpdateStatus();
        }

        void MoveSelected(int dir)
        {
            if (lvMods.SelectedItems.Count == 0) return;
            var s = lvMods.SelectedItems[0].Tag as ModSelection;
            int i = cfg.Mods.IndexOf(s);
            int j = i + dir;
            // Skip entries that are not shown (unknown mods) to keep the move intuitive.
            while (j >= 0 && j < cfg.Mods.Count && Info(cfg.Mods[j].Id) == null) j += dir;
            if (j < 0 || j >= cfg.Mods.Count) return;
            cfg.Mods.RemoveAt(i);
            cfg.Mods.Insert(j, s);
            SaveConfig();
            FillMods();
            UpdateStatus();
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
            UpdateStatus();
        }

        void ImportLauncher()
        {
            var lc = LauncherConfig.Load(GameEnv.LauncherModsYml);
            catalog = ModCatalog.Scan(env, lc);
            int n = cfg.ImportFromLauncher(catalog);
            SaveConfig();
            FillMods();
            UpdateStatus();
            Log(n + " mod(s) taken over from the Mod Launcher, order aligned with the launcher.");
        }

        void SaveConfig()
        {
            try { cfg.Save(env.ConfigPath); }
            catch (Exception ex) { Log("Could not save the configuration: " + ex.Message); }
        }

        void FillConflicts()
        {
            lvConflicts.BeginUpdate();
            lvConflicts.Items.Clear();
            foreach (var c in conflicts)
            {
                var it = new ListViewItem(c.File) { Tag = c };
                it.SubItems.Add(c.Path);
                it.SubItems.Add(string.Join("  |  ", c.Candidates.Select(x => x.ModName + ": " + x.Text)));
                string over = null;
                if (cfg.ConflictOverrides.TryGetValue(c.Id, out var oid))
                    over = oid.StartsWith(ConflictPolicy.CustomPrefix)
                        ? "value " + oid.Substring(ConflictPolicy.CustomPrefix.Length) + " (chosen)"
                        : (cfg.Mods.FirstOrDefault(m => m.Id == oid)?.Name ?? oid) + " (chosen)";
                it.SubItems.Add(over ?? c.WinnerName);
                if (over != null || c.ByOverride) it.ForeColor = Color.DarkBlue;
                lvConflicts.Items.Add(it);
            }
            lvConflicts.EndUpdate();
            tabs.TabPages[1].Text = "Conflicts (" + conflicts.Count + ")";
        }

        /// <summary>Lets the user pick the winning mod, the vanilla value or an own value for a conflict.</summary>
        void ChooseWinner()
        {
            if (lvConflicts.SelectedItems.Count == 0) return;
            var c = (Conflict)lvConflicts.SelectedItems[0].Tag;
            var mods = c.Candidates.Where(x => x.ModName != "Vanilla")
                .Select(x => cfg.Mods.FirstOrDefault(m => m.Name == x.ModName)).Where(m => m != null).Distinct().ToList();
            if (mods.Count == 0) return;
            // Own values are possible for single values (numbers, strings) - not for whole objects, text hunks or binary files.
            var vanilla = c.Candidates.FirstOrDefault(x => x.ModName == "Vanilla").Text;
            bool scalar = c.Kind == "value" && c.Candidates.All(x => ConflictPolicy.IsValidScalar(x.Text));
            cfg.ConflictOverrides.TryGetValue(c.Id, out var current);
            string currentCustom = current != null && current.StartsWith(ConflictPolicy.CustomPrefix) ? current.Substring(ConflictPolicy.CustomPrefix.Length) : null;

            int rows = mods.Count + (scalar ? 2 : 0);
            using (var dlg = new Form { Text = "Resolve conflict", Size = new Size(640, 170 + 32 * rows), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, ShowIcon = false, ShowInTaskbar = false, Font = Font })
            {
                dlg.Controls.Add(new Label { Text = c.File + "\n" + c.Path, Location = new Point(12, 10), Size = new Size(600, 40) });
                var radios = new List<RadioButton>();
                int y = 56;
                foreach (var m in mods)
                {
                    var text = c.Candidates.FirstOrDefault(x => x.ModName == m.Name).Text;
                    var rb = new RadioButton { Text = m.Name + ":  " + text, Tag = m, Location = new Point(16, y), Size = new Size(600, 26), Checked = currentCustom == null && m.Id == c.WinnerId };
                    radios.Add(rb);
                    dlg.Controls.Add(rb);
                    y += 32;
                }
                RadioButton rbVanilla = null, rbCustom = null;
                TextBox txtCustom = null;
                if (scalar)
                {
                    rbVanilla = new RadioButton { Text = "Vanilla (original value):  " + vanilla, Location = new Point(16, y), Size = new Size(600, 26), Checked = currentCustom != null && currentCustom == vanilla };
                    dlg.Controls.Add(rbVanilla);
                    y += 32;
                    rbCustom = new RadioButton { Text = "Own value:", Location = new Point(16, y), Size = new Size(110, 26), Checked = currentCustom != null && currentCustom != vanilla };
                    txtCustom = new TextBox { Location = new Point(130, y), Width = 300, Text = currentCustom ?? c.Candidates.Last().Text };
                    txtCustom.TextChanged += (s, e) => rbCustom.Checked = true;
                    dlg.Controls.Add(rbCustom);
                    dlg.Controls.Add(txtCustom);
                    y += 32;
                }
                var ok = new Button { Text = "Apply", DialogResult = DialogResult.OK, Location = new Point(410, y + 8), Width = 100 };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(516, y + 8), Width = 100 };
                dlg.Controls.Add(ok);
                dlg.Controls.Add(cancel);
                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                string decision, label;
                if (rbVanilla != null && rbVanilla.Checked) { decision = ConflictPolicy.CustomPrefix + vanilla; label = "vanilla value " + vanilla; }
                else if (rbCustom != null && rbCustom.Checked)
                {
                    var v = txtCustom.Text.Trim();
                    if (!ConflictPolicy.IsValidScalar(v))
                    {
                        MessageBox.Show(this, "\"" + v + "\" is not a valid single value (use e.g. 25, 0.5 or \"Text\").", "Resolve conflict", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    decision = ConflictPolicy.CustomPrefix + v;
                    label = "own value " + v;
                }
                else
                {
                    var chosen = radios.FirstOrDefault(r => r.Checked)?.Tag as ModSelection;
                    if (chosen == null) return;
                    decision = chosen.Id;
                    label = chosen.Name;
                }
                cfg.ConflictOverrides[c.Id] = decision;
                SaveConfig();
                FillConflicts();
                UpdateStatus();
                Log("Conflict " + c.Id + ": " + label + " (takes effect after merging again).");
            }
        }

        void RemoveOverride()
        {
            if (lvConflicts.SelectedItems.Count == 0) return;
            var c = (Conflict)lvConflicts.SelectedItems[0].Tag;
            if (cfg.ConflictOverrides.Remove(c.Id)) { SaveConfig(); FillConflicts(); UpdateStatus(); }
        }

        void UpdateStatus()
        {
            if (env == null || cfg == null) return;
            var man = Builder.ReadManifest(env);
            int sel = cfg.Mods.Count(s => s.Enabled && (Info(s.Id)?.Mergeable ?? false));
            string text;
            Color color;
            string fp = null;
            try { fp = new Builder(env, cfg, null).CurrentFingerprint(); } catch { }
            if (sel == 0) { text = "No mod selected. Check the mods you want and click \"Merge\"."; color = Color.DimGray; }
            else if (man == null) { text = sel + " mod(s) selected - not merged yet. Please click \"Merge\"."; color = Color.DarkOrange; }
            else if (fp != null && man.TryGetValue("fingerprint", out var f) && f as string == fp)
            {
                text = "Up to date: " + sel + " mod(s) merged on " + man["builtAt"] + ". Keep \"" + GameEnv.OutputFolderName + "\" enabled in the Mod Launcher and click \"Play\".";
                color = Color.FromArgb(27, 120, 50);
            }
            else { text = "Selection, order or mod files have changed (e.g. a Workshop update) - please merge again."; color = Color.DarkOrange; }

            var deployLog = Path.Combine(GameEnv.LauncherDeployDir, "scripts.log");
            if (File.Exists(deployLog) && new FileInfo(deployLog).Length > 0)
                text += "\nNote: the last game start through the Mod Launcher produced a scripts.log with messages (" + deployLog + ").";
            lblStatus.Text = text;
            lblStatus.ForeColor = color;
        }

        // ---------------- Actions ----------------

        void SetBusy(bool b)
        {
            busy = b;
            foreach (var btn in actionButtons) btn.Enabled = !b;
            lvMods.Enabled = !b;
            progress.Visible = b;
            UseWaitCursor = b;
        }

        void Log(string s)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), s); return; }
            txtLog.AppendText(s + Environment.NewLine);
        }

        bool EnsureLauncherClosed()
        {
            if (!LauncherConfig.LauncherRunning()) return true;
            var r = MessageBox.Show(this, "The official Mod Launcher is open.\n\nTo enable the merged mod there and disable the individually merged mods, " +
                                          "the launcher has to be closed. Close it now?\n\n(No = merge without updating the launcher)",
                "TG3 ModLoader", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
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

        void RunBuild(bool launchAfter)
        {
            if (busy) return;
            if (Builder.GameUsesFolder(env.OutputModDir))
            {
                MessageBox.Show(this, "The Guild 3 is running with the merged mod. Please close the game first.", "TG3 ModLoader", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!EnsureLauncherClosed()) return;
            SaveConfig();
            SetBusy(true);
            tabs.SelectedIndex = 2;
            Log("");
            Log("=== Merge " + DateTime.Now.ToString("HH:mm:ss") + " ===");
            var builder = new Builder(env, cfg, Log);
            Task.Run(() => builder.Build(false, true)).ContinueWith(t =>
            {
                SetBusy(false);
                var r = t.Result;
                foreach (var w in r.Warnings) Log("Note: " + w);
                foreach (var v in r.Violations) Log("Verification error: " + v);
                var lc = LauncherConfig.Load(GameEnv.LauncherModsYml);
                catalog = ModCatalog.Scan(env, lc);
                FillMods();
                conflicts = r.Conflicts;
                FillConflicts();
                UpdateStatus();
                if (!r.Success)
                {
                    MessageBox.Show(this, "Merge failed:\n\n" + r.Error, "TG3 ModLoader", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                int merged = r.Files.Count(f => f.Method == "structural" || f.Method == "line");
                var msg = r.Mods.Count + " mod(s) merged successfully.\n\n" +
                          "• " + r.Files.Count + " files are changed by mods, " + merged + " of them combined from several mods\n" +
                          "• " + r.Conflicts.Count + " real conflicts (see the \"Conflicts\" tab)\n" +
                          "• Verification: " + (r.Violations.Count == 0 ? "every change of every mod is present in the result" : r.Violations.Count + " errors (see log)") +
                          (r.LauncherUpdated ? "\n\nIn the Mod Launcher, \"" + GameEnv.OutputFolderName + "\" was enabled and the individual mods were disabled." : "") +
                          (r.Warnings.Count > 0 ? "\n\nNotes:\n" + string.Join("\n", r.Warnings.Take(6).Select(w => "• " + w)) : "");
                if (launchAfter)
                {
                    Program.StartLauncher(env, Log);
                    Log(msg);
                }
                else MessageBox.Show(this, msg, "TG3 ModLoader", MessageBoxButtons.OK, r.Violations.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void RunTest()
        {
            if (busy) return;
            if (!Directory.Exists(env.OutputModDir)) { MessageBox.Show(this, "Please merge first.", "TG3 ModLoader"); return; }
            if (MessageBox.Show(this, "The test run starts The Guild 3 with the merged mod, waits for the main menu, checks for script errors and crashes and closes the game again.\n\nStart now?",
                    "Test run", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            SetBusy(true);
            tabs.SelectedIndex = 2;
            Log("");
            Log("=== Test run " + DateTime.Now.ToString("HH:mm:ss") + " ===");
            Task.Run(() => TestRun.Run(env, env.OutputModDir, 600, Log)).ContinueWith(t =>
            {
                SetBusy(false);
                var r = t.Result;
                Log("Result: " + r.Summary);
                if (!string.IsNullOrWhiteSpace(r.ScriptsLog)) Log("scripts.log:\n" + r.ScriptsLog);
                MessageBox.Show(this, r.Summary + (string.IsNullOrWhiteSpace(r.DialogText) ? "" : "\n\nDialog: " + r.DialogText),
                    "Test run", MessageBoxButtons.OK, r.Ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void UnregisterFromLauncher()
        {
            if (busy) return;
            if (MessageBox.Show(this, "Remove \"" + GameEnv.OutputFolderName + "\" from the official Mod Launcher and re-enable the merged mods there " +
                                      "(only one script mod, because the launcher cannot load more)?", "TG3 ModLoader", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            if (!EnsureLauncherClosed()) return;
            bool ok = new Builder(env, cfg, Log).Unregister(out var msg);
            Log(msg);
            Reload(false);
            MessageBox.Show(this, msg, "TG3 ModLoader", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        void OpenSelectedFolder()
        {
            if (lvMods.SelectedItems.Count == 0) return;
            var m = Info(((ModSelection)lvMods.SelectedItems[0].Tag).Id);
            OpenPath(m?.Dir);
        }

        void OpenPath(string p)
        {
            if (string.IsNullOrEmpty(p) || (!File.Exists(p) && !Directory.Exists(p))) { Log("Does not exist: " + p); return; }
            try { Process.Start(new ProcessStartInfo(p) { UseShellExecute = true }); }
            catch (Exception ex) { Log(ex.Message); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy && MessageBox.Show(this, "An operation is still running. Exit anyway?", "TG3 ModLoader", MessageBoxButtons.YesNo) != DialogResult.Yes) e.Cancel = true;
            base.OnFormClosing(e);
        }
    }
}
