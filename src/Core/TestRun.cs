using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace TG3ModLoader.Core
{
    public sealed class TestRunResult
    {
        public bool ReachedMainMenu;
        public bool ScriptErrorDialog;
        public bool Crashed;
        public bool CrashReport;
        public bool TimedOut;
        public bool PathTooLong;
        public string DialogText;
        public string ScriptsLog;
        public double SecondsToMenu;
        public string ScreenshotPath;
        public string Summary;

        public bool Ok => ReachedMainMenu && !ScriptErrorDialog && string.IsNullOrWhiteSpace(ScriptsLog) && !Crashed;
    }

    /// <summary>
    /// Starts the game with a mod folder (exactly like the Mod Launcher does: Guild3.exe -mod &lt;folder&gt;),
    /// waits until the main menu is loaded and checks for script errors.
    /// </summary>
    public static class TestRun
    {
        public static TestRunResult Run(GameEnv env, string modDir, int timeoutSeconds, Action<string> log, string screenshotPath = null)
        {
            var res = new TestRunResult();
            if (Process.GetProcessesByName("Guild3").Length > 0) { res.Summary = "The game is already running."; return res; }

            if (modDir != null)
            {
                // The game cannot open paths with 260 or more characters; such a folder would crash it.
                var longest = Directory.EnumerateFiles(Util.TextIO.LongPath(modDir), "*", SearchOption.AllDirectories)
                    .Select(f => f.StartsWith(@"\\?\") ? f.Substring(4).Length : f.Length).DefaultIfEmpty(0).Max();
                if (longest > Builder.GameMaxPath)
                {
                    res.PathTooLong = true;
                    res.Summary = "Not started: the mod folder contains paths with " + longest + " characters - The Guild 3 cannot open paths with 260 or more characters. Use a shorter folder.";
                    return res;
                }
            }
            var scriptsLog = modDir != null ? Path.Combine(modDir, "scripts.log") : null;
            if (scriptsLog != null && File.Exists(scriptsLog)) File.Delete(scriptsLog);
            var loadLog = Path.Combine(env.GameDir, "loadingtime.log");
            var start = DateTime.Now;

            var psi = new ProcessStartInfo(env.GameExe)
            {
                WorkingDirectory = env.GameDir,
                UseShellExecute = false,
                Arguments = modDir != null ? "-mod " + Quote(modDir) : ""
            };
            psi.EnvironmentVariables["SteamAppId"] = GameEnv.AppId;
            psi.EnvironmentVariables["SteamGameId"] = GameEnv.AppId;
            log?.Invoke("Starting: Guild3.exe " + psi.Arguments);
            var proc = Process.Start(psi);
            var sw = Stopwatch.StartNew();
            try
            {
                while (sw.Elapsed.TotalSeconds < timeoutSeconds)
                {
                    Thread.Sleep(1000);
                    if (proc.HasExited)
                    {
                        // The game may hand over to a Steam-relaunched instance.
                        var other = Process.GetProcessesByName("Guild3").FirstOrDefault();
                        if (other != null) { proc = other; continue; }
                        res.Crashed = !res.ReachedMainMenu;
                        log?.Invoke("Game process exited (exit code " + SafeExitCode(proc) + ").");
                        break;
                    }
                    foreach (var dlg in DialogWindows(proc.Id))
                    {
                        var text = WindowText(dlg);
                        res.DialogText = (res.DialogText == null ? "" : res.DialogText + " | ") + text;
                        if (text.IndexOf("script", StringComparison.OrdinalIgnoreCase) >= 0) res.ScriptErrorDialog = true;
                        log?.Invoke("Game dialog: " + text);
                        CloseDialog(dlg);
                    }
                    if (MainMenuReached(loadLog, start))
                    {
                        res.ReachedMainMenu = true;
                        res.SecondsToMenu = sw.Elapsed.TotalSeconds;
                        log?.Invoke($"Main menu reached after {res.SecondsToMenu:0} s.");
                        Thread.Sleep(4000);
                        foreach (var dlg in DialogWindows(proc.Id))
                        {
                            var text = WindowText(dlg);
                            res.DialogText = (res.DialogText == null ? "" : res.DialogText + " | ") + text;
                            if (text.IndexOf("script", StringComparison.OrdinalIgnoreCase) >= 0) res.ScriptErrorDialog = true;
                            CloseDialog(dlg);
                        }
                        if (screenshotPath != null) res.ScreenshotPath = Screenshot(proc, screenshotPath) ? screenshotPath : null;
                        break;
                    }
                }
                if (!res.ReachedMainMenu && !res.Crashed && sw.Elapsed.TotalSeconds >= timeoutSeconds) res.TimedOut = true;
            }
            finally
            {
                try
                {
                    foreach (var p in Process.GetProcessesByName("Guild3")) { p.Kill(); p.WaitForExit(15000); }
                }
                catch { }
            }
            Thread.Sleep(1500);
            // The game writes a crash dump (Sentry) when it crashes, e.g. on fatal script syntax errors.
            try
            {
                var reports = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Guild3", "sentry", "reports");
                if (Directory.Exists(reports) && Directory.GetFiles(reports, "*.dmp").Any(f => File.GetLastWriteTime(f) >= start))
                {
                    res.Crashed = true;
                    res.CrashReport = true;
                }
            }
            catch { }
            if (scriptsLog != null && File.Exists(scriptsLog))
            {
                res.ScriptsLog = File.ReadAllText(scriptsLog);
                try
                {
                    var keep = Path.Combine(env.ToolDir, "testrun_scripts.log");
                    File.Copy(scriptsLog, keep, true);
                    File.Delete(scriptsLog);
                }
                catch { }
            }
            var sb = new StringBuilder();
            if (res.Ok) sb.Append("OK: main menu reached without script errors (" + res.SecondsToMenu.ToString("0") + " s).");
            else
            {
                if (res.ScriptErrorDialog) sb.Append("A script error dialog was shown. ");
                if (!string.IsNullOrWhiteSpace(res.ScriptsLog)) sb.Append("scripts.log contains messages. ");
                if (res.Crashed) sb.Append(res.CrashReport ? "The game crashed (crash report created - typical for broken scripts). " : "The game exited prematurely. ");
                if (res.TimedOut) sb.Append("Timeout - main menu not reached. ");
                if (res.ReachedMainMenu) sb.Append("(The main menu was reached.)");
            }
            res.Summary = sb.ToString().Trim();
            return res;
        }

        static string SafeExitCode(Process p)
        {
            try { return p.ExitCode.ToString(); } catch { return "?"; }
        }

        static string Quote(string s) => "\"" + s.TrimEnd('\\') + "\"";

        static bool MainMenuReached(string loadLog, DateTime start)
        {
            try
            {
                var fi = new FileInfo(loadLog);
                if (!fi.Exists || fi.LastWriteTime < start.AddSeconds(-2)) return false;
                string text;
                using (var fs = new FileStream(loadLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs)) text = sr.ReadToEnd();
                int idx = text.LastIndexOf("STARTED: main", StringComparison.Ordinal);
                return idx >= 0 && text.IndexOf("FINISHED", idx, StringComparison.Ordinal) > idx;
            }
            catch { return false; }
        }

        // ---- Win32 helpers ----
        delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lParam);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        const uint BM_CLICK = 0x00F5, WM_CLOSE = 0x0010;

        static string ClassOf(IntPtr h)
        {
            var sb = new StringBuilder(256);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        static List<IntPtr> DialogWindows(int pid)
        {
            var res = new List<IntPtr>();
            EnumWindows((h, l) =>
            {
                GetWindowThreadProcessId(h, out uint p);
                if (p == pid && IsWindowVisible(h) && ClassOf(h) == "#32770") res.Add(h);
                return true;
            }, IntPtr.Zero);
            return res;
        }

        static string WindowText(IntPtr dlg)
        {
            var parts = new List<string>();
            var sb = new StringBuilder(2048);
            GetWindowText(dlg, sb, sb.Capacity);
            if (sb.Length > 0) parts.Add(sb.ToString());
            EnumChildWindows(dlg, (h, l) =>
            {
                if (ClassOf(h) == "Static")
                {
                    var s = new StringBuilder(4096);
                    GetWindowText(h, s, s.Capacity);
                    if (s.Length > 0) parts.Add(s.ToString());
                }
                return true;
            }, IntPtr.Zero);
            return string.Join(" / ", parts).Replace("\r", " ").Replace("\n", " ");
        }

        static void CloseDialog(IntPtr dlg)
        {
            IntPtr button = IntPtr.Zero;
            EnumChildWindows(dlg, (h, l) =>
            {
                if (ClassOf(h) == "Button") { button = h; return false; }
                return true;
            }, IntPtr.Zero);
            if (button != IntPtr.Zero) SendMessage(button, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
            else PostMessage(dlg, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();

        /// <summary>
        /// Captures the game window only: PrintWindow first; DirectX windows often render black that way, so the
        /// screen is captured instead - but only while the game is the foreground window (never other windows).
        /// </summary>
        static bool Screenshot(Process p, string path)
        {
            try
            {
                p.Refresh();
                var h = p.MainWindowHandle;
                if (h == IntPtr.Zero || !GetWindowRect(h, out var r) || r.Right <= r.Left) return false;
                using (var bmp = new Bitmap(r.Right - r.Left, r.Bottom - r.Top))
                {
                    bool ok;
                    using (var g = Graphics.FromImage(bmp))
                    {
                        var hdc = g.GetHdc();
                        ok = PrintWindow(h, hdc, 2);
                        g.ReleaseHdc(hdc);
                    }
                    if (!ok || IsBlack(bmp))
                    {
                        if (GetForegroundWindow() != h) return false;
                        using (var g = Graphics.FromImage(bmp))
                            g.CopyFromScreen(new Point(r.Left, r.Top), Point.Empty, bmp.Size);
                    }
                    bmp.Save(path, ImageFormat.Png);
                }
                return true;
            }
            catch { return false; }
        }

        static bool IsBlack(Bitmap bmp)
        {
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    var c = bmp.GetPixel(bmp.Width * (2 * x + 1) / 32, bmp.Height * (2 * y + 1) / 32);
                    if (c.R + c.G + c.B > 24) return false;
                }
            return true;
        }
    }
}
