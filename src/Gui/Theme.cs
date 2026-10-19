using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TG3ModLoader.Gui
{
    /// <summary>Colours of one theme (light or dark), derived from the gold and navy of the program icon.</summary>
    public sealed class Palette
    {
        public bool Dark;
        public Color Window, Header, Surface, SurfaceHover, Border, Text, TextMuted, TextFaint;
        public Color Accent, AccentHover, AccentPressed, AccentText, AccentSoft;
        public Color Success, SuccessSoft, Warning, WarningSoft, Danger, DangerSoft, Info, InfoSoft;
        public Color Input, InputBorder, ScrollThumb, Selection;

        public static Palette Light()
        {
            var p = new Palette
            {
                Dark = false,
                Window = C(0xF3F4F6), Header = C(0xFFFFFF), Surface = C(0xFFFFFF), SurfaceHover = C(0xF7F8FA), Border = C(0xE1E4E8),
                Text = C(0x1B2129), TextMuted = C(0x5B6573), TextFaint = C(0x8C95A1),
                Accent = C(0xA8721F), AccentHover = C(0x94641A), AccentPressed = C(0x7E5515), AccentText = C(0xFFFFFF),
                Success = C(0x2E9E4F), Warning = C(0xB86F0E), Danger = C(0xD64545), Info = C(0x2F7FD1),
                Input = C(0xFFFFFF), InputBorder = C(0xCDD2D9), ScrollThumb = C(0xC6CBD3)
            };
            p.Finish(0.11f);
            return p;
        }

        public static Palette DarkTheme()
        {
            var p = new Palette
            {
                Dark = true,
                Window = C(0x111418), Header = C(0x161A20), Surface = C(0x1C2128), SurfaceHover = C(0x232A33), Border = C(0x2C333D),
                Text = C(0xE9EDF2), TextMuted = C(0xA3ADBA), TextFaint = C(0x6E7887),
                Accent = C(0xE0A84A), AccentHover = C(0xEDB862), AccentPressed = C(0xC99336), AccentText = C(0x17130B),
                Success = C(0x4FBF6B), Warning = C(0xE9A23B), Danger = C(0xEF5E5E), Info = C(0x5AA9F0),
                Input = C(0x14181D), InputBorder = C(0x3A434F), ScrollThumb = C(0x3E4754)
            };
            p.Finish(0.16f);
            return p;
        }

        void Finish(float soft)
        {
            AccentSoft = Theme.Blend(Surface, Accent, soft);
            SuccessSoft = Theme.Blend(Surface, Success, soft);
            WarningSoft = Theme.Blend(Surface, Warning, soft);
            DangerSoft = Theme.Blend(Surface, Danger, soft);
            InfoSoft = Theme.Blend(Surface, Info, soft);
            Selection = Theme.Blend(Surface, Accent, soft * 0.8f);
        }

        static Color C(int rgb) => Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255);
    }

    /// <summary>Theme state: follows the Windows light/dark app setting, including live changes.</summary>
    public static class Theme
    {
        /// <summary>TG3ML_THEME=dark or light forces a theme (declared first: static fields initialise in order).</summary>
        static readonly string Forced = (Environment.GetEnvironmentVariable("TG3ML_THEME") ?? "").Trim().ToLowerInvariant();

        public static Palette P { get; private set; } = Detect();
        public static event Action Changed;

        public static bool WindowsUsesDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch { return false; }
        }

        static Palette Detect()
        {
            bool dark = Forced == "dark" || (Forced != "light" && !SystemInformation.HighContrast && WindowsUsesDark());
            return dark ? Palette.DarkTheme() : Palette.Light();
        }

        public static void StartWatching(Control uiThread)
        {
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (uiThread.IsDisposed || !uiThread.IsHandleCreated) return;
                uiThread.BeginInvoke(new Action(() =>
                {
                    var p = Detect();
                    if (p.Dark == P.Dark) return;
                    P = p;
                    Changed?.Invoke();
                }));
            };
        }

        // ---- Fonts ----
        static readonly string UiFamily = FontExists("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
        static readonly string GlyphFamily = FontExists("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
        public static readonly Font Body = new Font(UiFamily, 10f);
        public static readonly Font BodyBold = new Font(UiFamily, 10f, FontStyle.Bold);
        public static readonly Font Small = new Font(UiFamily, 9f);
        public static readonly Font SmallBold = new Font(UiFamily, 9f, FontStyle.Bold);
        public static readonly Font Title = new Font(UiFamily, 17f, FontStyle.Bold);
        public static readonly Font Heading = new Font(UiFamily, 12.5f, FontStyle.Bold);
        public static readonly Font Button = new Font(UiFamily, 10f, FontStyle.Bold);
        public static readonly Font Mono = new Font(FontExists("Cascadia Mono") ? "Cascadia Mono" : "Consolas", 9.5f);
        public static Font Glyph(float size) => new Font(GlyphFamily, size);

        static bool FontExists(string name) => new InstalledFontCollection().Families.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        // Segoe MDL2 / Fluent icon code points
        public const string GCheck = "", GWarning = "", GInfo = "", GError = "", GPlay = "", GRefresh = "",
            GMore = "", GHelp = "", GUp = "", GDown = "", GFolder = "", GDocument = "",
            GCopy = "", GGlobe = "", GClear = "", GSelectAll = "", GSync = "", GTest = "", GLink = "";

        // ---- Drawing helpers ----
        public static Color Blend(Color a, Color b, float t) =>
            Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Max(1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color c, RectangleF r, float radius)
        {
            using (var path = Round(r, radius))
            using (var b = new SolidBrush(c)) g.FillPath(b, path);
        }

        public static void StrokeRound(Graphics g, Color c, RectangleF r, float radius, float width = 1f)
        {
            using (var path = Round(r, radius))
            using (var pen = new Pen(c, width)) g.DrawPath(pen, path);
        }

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        // ---- Native: dark title bar and dark native scrollbars ----
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string id);

        public static void ApplyTitleBar(Form f)
        {
            if (!f.IsHandleCreated) return;
            int v = P.Dark ? 1 : 0;
            try
            {
                if (DwmSetWindowAttribute(f.Handle, 20, ref v, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref v, 4);
                // Redraw the non-client area so the title bar switches immediately.
                f.Width += 1; f.Width -= 1;
            }
            catch { }
        }

        public static void ApplyNativeTheme(Control c)
        {
            if (!c.IsHandleCreated) return;
            try { SetWindowTheme(c.Handle, P.Dark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
        }
    }
}
