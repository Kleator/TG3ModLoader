using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TG3ModLoader.Core;
using TG3ModLoader.Merge;

namespace TG3ModLoader.Gui
{
    /// <summary>Base for owner-drawn, theme-aware controls.</summary>
    public class Painted : Control
    {
        public Painted()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        }

        /// <summary>The colour behind the control (the parent's surface).</summary>
        public Func<Color> Background = () => Theme.P.Window;
        protected Color Bg => Background();
        protected int S(float v) => (int)Math.Round(v * DeviceDpi / 96f);
        protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Bg);

        /// <summary>Single-line text width; NoPrefix so that "&amp;" counts as a character (as it is drawn).</summary>
        protected static int Measure(string s, Font f) => TextRenderer.MeasureText(s ?? "", f, Size.Empty, TextFormatFlags.NoPrefix).Width;

        protected static void PaintText(Graphics g, string s, Font f, Rectangle r, Color c, TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter) =>
            TextRenderer.DrawText(g, s ?? "", f, r, c, flags | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        protected static void GlyphText(Graphics g, string glyph, float size, Rectangle r, Color c)
        {
            using (var f = Theme.Glyph(size))
                TextRenderer.DrawText(g, glyph, f, r, c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>Wrapping text in a theme colour; grows to fit its text when <see cref="AutoHeight"/> is set.</summary>
    public sealed class TextBlock : Painted
    {
        public Font TextFont = Theme.Body;
        public Func<Color> Color = () => Theme.P.TextMuted;
        public bool AutoHeight = true;
        const TextFormatFlags Flags = TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix;

        protected override void OnPaint(PaintEventArgs e) => TextRenderer.DrawText(e.Graphics, base.Text, TextFont, ClientRectangle, Color(), Flags);

        void Fit()
        {
            if (!AutoHeight || Width <= 0) return;
            int h = TextRenderer.MeasureText(base.Text ?? "", TextFont, new Size(Width, int.MaxValue), Flags).Height + S(2);
            if (Height != h) Height = h;
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Fit(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Fit(); Invalidate(); }
    }

    public enum ButtonKind { Primary, Secondary, Ghost }

    /// <summary>Flat, rounded button with an optional icon glyph.</summary>
    public sealed class FlatButton : Painted
    {
        public ButtonKind Kind = ButtonKind.Secondary;
        public string Glyph;
        bool hover, pressed;

        public FlatButton()
        {
            SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new ButtonAccessible(this);

        /// <summary>Lets screen readers (and UI automation) see and press the button.</summary>
        sealed class ButtonAccessible : ControlAccessibleObject
        {
            readonly FlatButton button;
            public ButtonAccessible(FlatButton b) : base(b) { button = b; }
            public override AccessibleRole Role => AccessibleRole.PushButton;
            public override string Name => button.Text;
            public override string DefaultAction => "Press";
            public override void DoDefaultAction() { if (button.Enabled) button.OnClick(EventArgs.Empty); }
        }

        public int PreferredWidth()
        {
            var font = Kind == ButtonKind.Ghost ? Theme.Body : Theme.Button;
            int w = Measure(base.Text, font) + S(Kind == ButtonKind.Ghost ? 22 : 34);
            if (!string.IsNullOrEmpty(Glyph)) w += S(base.Text?.Length > 0 ? 26 : 16);
            return w;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Smooth(g);
            var p = Theme.P;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color back, fore, border;
            switch (Kind)
            {
                case ButtonKind.Primary:
                    back = !Enabled ? Theme.Blend(Bg, p.Accent, 0.35f) : pressed ? p.AccentPressed : hover ? p.AccentHover : p.Accent;
                    fore = Enabled ? p.AccentText : Theme.Blend(p.AccentText, back, 0.35f);
                    border = back;
                    break;
                case ButtonKind.Secondary:
                    back = !Enabled ? p.Surface : pressed ? Theme.Blend(p.Surface, p.Text, 0.10f) : hover ? p.SurfaceHover : p.Surface;
                    fore = Enabled ? p.Text : p.TextFaint;
                    border = hover && Enabled ? Theme.Blend(p.Border, p.Text, 0.25f) : p.Border;
                    break;
                default:
                    back = pressed ? Theme.Blend(Bg, p.Text, 0.12f) : hover ? Theme.Blend(Bg, p.Text, 0.07f) : Bg;
                    fore = !Enabled ? p.TextFaint : hover ? p.Text : p.TextMuted;
                    border = back;
                    break;
            }
            float radius = S(8);
            Theme.FillRound(g, back, r, radius);
            if (border != back) Theme.StrokeRound(g, border, r, radius);
            if (Focused && ShowFocusCues)
                Theme.StrokeRound(g, p.Accent, RectangleF.Inflate(r, -S(2), -S(2)), radius - 1, 1.5f);

            var font = Kind == ButtonKind.Ghost ? Theme.Body : Theme.Button;
            int textW = string.IsNullOrEmpty(base.Text) ? 0 : Measure(base.Text, font);
            int glyphW = string.IsNullOrEmpty(Glyph) ? 0 : S(18);
            int gap = glyphW > 0 && textW > 0 ? S(8) : 0;
            int x = (Width - (glyphW + gap + textW)) / 2;
            if (glyphW > 0) GlyphText(g, Glyph, 11f, new Rectangle(x, 0, glyphW, Height), fore);
            if (textW > 0) PaintText(g, base.Text, font, new Rectangle(x + glyphW + gap, 0, textW + S(4), Height), fore);
        }
    }

    /// <summary>Tab strip with an accent underline and optional count badges.</summary>
    public sealed class NavTabs : Painted
    {
        public readonly List<string> Items = new List<string>();
        public readonly Dictionary<int, int> Counts = new Dictionary<int, int>();
        public readonly HashSet<int> Attention = new HashSet<int>();
        int selected, hover = -1;
        public event EventHandler SelectedChanged;

        public NavTabs() { Cursor = Cursors.Hand; }

        protected override AccessibleObject CreateAccessibilityInstance() => new TabsAccessible(this);

        sealed class TabsAccessible : ControlAccessibleObject
        {
            readonly NavTabs tabs;
            public TabsAccessible(NavTabs t) : base(t) { tabs = t; }
            public override AccessibleRole Role => AccessibleRole.PageTabList;
            public override int GetChildCount() => tabs.Items.Count;
            public override AccessibleObject GetChild(int index) => index >= 0 && index < tabs.Items.Count ? new TabAccessible(tabs, this, index) : null;
        }

        sealed class TabAccessible : AccessibleObject
        {
            readonly NavTabs tabs;
            readonly AccessibleObject parent;
            readonly int index;
            public TabAccessible(NavTabs t, AccessibleObject p, int i) { tabs = t; parent = p; index = i; }
            public override string Name => tabs.Items[index] + (tabs.Counts.TryGetValue(index, out int n) && n > 0 ? " (" + n + ")" : "");
            public override AccessibleRole Role => AccessibleRole.PageTab;
            public override AccessibleObject Parent => parent;
            public override AccessibleStates State => AccessibleStates.Selectable | (tabs.Selected == index ? AccessibleStates.Selected : AccessibleStates.None);
            public override string DefaultAction => "Switch";
            public override void DoDefaultAction() => tabs.Selected = index;
            public override Rectangle Bounds
            {
                get { using (var g = tabs.CreateGraphics()) return tabs.RectangleToScreen(tabs.TabRects(g)[index]); }
            }
        }

        public int Selected
        {
            get => selected;
            set { if (selected == value) return; selected = value; Invalidate(); SelectedChanged?.Invoke(this, EventArgs.Empty); }
        }

        List<Rectangle> TabRects(Graphics g)
        {
            var res = new List<Rectangle>();
            int x = 0;
            for (int i = 0; i < Items.Count; i++)
            {
                int w = Measure(Items[i], Theme.BodyBold) + S(28);
                if (Counts.TryGetValue(i, out int n) && n > 0) w += Measure(n.ToString(), Theme.SmallBold) + S(18);
                res.Add(new Rectangle(x, 0, w, Height));
                x += w + S(4);
            }
            return res;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Smooth(g);
            var p = Theme.P;
            using (var pen = new Pen(p.Border)) g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            var rects = TabRects(g);
            for (int i = 0; i < Items.Count; i++)
            {
                var r = rects[i];
                bool sel = i == selected;
                if (i == hover && !sel) Theme.FillRound(g, Theme.Blend(Bg, p.Text, 0.05f), new RectangleF(r.X, r.Y + S(4), r.Width, r.Height - S(10)), S(8));
                var font = sel ? Theme.BodyBold : Theme.Body;
                var color = sel ? p.Text : i == hover ? p.Text : p.TextMuted;
                int tw = Measure(Items[i], Theme.BodyBold);
                int x = r.X + S(14);
                PaintText(g, Items[i], font, new Rectangle(x, 0, tw + S(4), Height - S(4)), color);
                if (Counts.TryGetValue(i, out int n) && n > 0)
                {
                    string t = n.ToString();
                    int bw = Measure(t, Theme.SmallBold) + S(12);
                    var br = new Rectangle(x + tw + S(6), (Height - S(4)) / 2 - S(10), bw, S(20));
                    bool warn = Attention.Contains(i);
                    Theme.FillRound(g, warn ? p.WarningSoft : Theme.Blend(Bg, p.Text, 0.08f), br, S(10));
                    PaintText(g, t, Theme.SmallBold, br, warn ? p.Warning : p.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                if (sel) Theme.FillRound(g, p.Accent, new RectangleF(r.X + S(10), Height - S(4), r.Width - S(20), S(3)), S(1.5f));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            using (var g = CreateGraphics())
            {
                var rects = TabRects(g);
                int h = rects.FindIndex(r => r.Contains(e.Location));
                if (h != hover) { hover = h; Invalidate(); }
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            using (var g = CreateGraphics())
            {
                int i = TabRects(g).FindIndex(r => r.Contains(e.Location));
                if (i >= 0) Selected = i;
            }
            base.OnMouseDown(e);
        }
    }

    public enum BannerKind { Neutral, Success, Warning, Danger, Busy }

    /// <summary>Large status message at the top of the window (with an optional action and a busy animation).</summary>
    public sealed class StatusBanner : Painted
    {
        BannerKind kind;
        string title = "", detail = "", action;
        readonly Timer timer = new Timer { Interval = 25 };
        float phase;
        Rectangle actionRect;
        bool actionHover;
        public event EventHandler ActionClicked;

        public StatusBanner()
        {
            AccessibleRole = AccessibleRole.StaticText;
            timer.Tick += (s, e) => { phase = (phase + 0.012f) % 1.4f; Invalidate(); };
        }

        public void Set(BannerKind k, string t, string d, string actionText = null)
        {
            kind = k; title = t ?? ""; detail = d ?? ""; action = actionText;
            AccessibleName = title + (detail.Length == 0 ? "" : (title.EndsWith(".") || title.EndsWith("!") || title.EndsWith("…") ? " " : ". ") + detail);
            timer.Enabled = k == BannerKind.Busy;
            Height = PreferredHeight(Width);
            Invalidate();
        }

        public void SetDetail(string d) { detail = d ?? ""; Invalidate(); }

        const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix;

        int TextWidth(int width) => Math.Max(S(120), width - S(84) - ActionWidth());
        int ActionWidth() => string.IsNullOrEmpty(action) ? 0 : Measure(action, Theme.Button) + S(44);

        public int PreferredHeight(int width)
        {
            if (width <= 0) return S(72);
            int tw = TextWidth(width);
            int h = TextRenderer.MeasureText(title, Theme.Heading, new Size(tw, int.MaxValue), Wrap).Height;
            if (detail.Length > 0) h += S(4) + TextRenderer.MeasureText(detail, Theme.Body, new Size(tw, int.MaxValue), Wrap).Height;
            return Math.Max(S(76), h + S(32));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int h = PreferredHeight(Width);
            if (h != Height) Height = h;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Smooth(g);
            var p = Theme.P;
            Color soft, strong;
            string glyph;
            switch (kind)
            {
                case BannerKind.Success: soft = p.SuccessSoft; strong = p.Success; glyph = Theme.GCheck; break;
                case BannerKind.Warning: soft = p.WarningSoft; strong = p.Warning; glyph = Theme.GWarning; break;
                case BannerKind.Danger: soft = p.DangerSoft; strong = p.Danger; glyph = Theme.GError; break;
                case BannerKind.Busy: soft = p.AccentSoft; strong = p.Accent; glyph = Theme.GSync; break;
                default: soft = p.Surface; strong = p.Info; glyph = Theme.GInfo; break;
            }
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRound(g, soft, r, S(14));
            Theme.StrokeRound(g, kind == BannerKind.Neutral ? p.Border : Theme.Blend(soft, strong, 0.25f), r, S(14));

            var circle = new Rectangle(S(18), S(18), S(40), S(40));
            using (var b = new SolidBrush(Theme.Blend(soft, strong, 0.18f))) g.FillEllipse(b, circle);
            GlyphText(g, glyph, 15f, circle, strong);

            int x = S(72), tw = TextWidth(Width);
            int th = TextRenderer.MeasureText(title, Theme.Heading, new Size(tw, int.MaxValue), Wrap).Height;
            TextRenderer.DrawText(g, title, Theme.Heading, new Rectangle(x, S(16), tw, th), p.Text, Wrap);
            if (detail.Length > 0)
                TextRenderer.DrawText(g, detail, Theme.Body, new Rectangle(x, S(16) + th + S(4), tw, Height - th - S(24)), p.TextMuted, Wrap);

            actionRect = Rectangle.Empty;
            if (!string.IsNullOrEmpty(action))
            {
                int aw = ActionWidth() - S(20);
                actionRect = new Rectangle(Width - aw - S(18), (Height - S(38)) / 2, aw, S(38));
                Theme.FillRound(g, actionHover ? p.AccentHover : p.Accent, actionRect, S(8));
                PaintText(g, action, Theme.Button, actionRect, p.AccentText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            if (kind == BannerKind.Busy)
            {
                var track = new RectangleF(S(72), Height - S(12), Width - S(72) - S(24), S(4));
                Theme.FillRound(g, Theme.Blend(soft, strong, 0.18f), track, S(2));
                float seg = track.Width * 0.3f;
                float pos = track.X - seg + (track.Width + seg) * Math.Min(1f, phase);
                var bar = RectangleF.Intersect(new RectangleF(pos, track.Y, seg, track.Height), track);
                if (bar.Width > 0) Theme.FillRound(g, strong, bar, S(2));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool h = actionRect.Contains(e.Location);
            if (h != actionHover) { actionHover = h; Cursor = h ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { actionHover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (actionRect.Contains(e.Location)) ActionClicked?.Invoke(this, EventArgs.Empty);
            base.OnMouseDown(e);
        }

        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }

    /// <summary>Vertical scrolling for owner-drawn lists, with a slim theme-coloured scrollbar.</summary>
    public abstract class ScrollList : Painted
    {
        protected int ScrollY;
        protected int ContentHeight;
        bool dragThumb, thumbHover;
        int dragStartY, dragStartScroll;

        protected ScrollList() { SetStyle(ControlStyles.Selectable, true); TabStop = true; }

        protected int BarWidth => S(10);
        protected bool ShowBar => ContentHeight > Height;
        protected int MaxScroll => Math.Max(0, ContentHeight - Height);
        protected int ViewWidth => Width - (ShowBar ? BarWidth + S(6) : 0);

        protected void ClampScroll() => ScrollY = Math.Max(0, Math.Min(MaxScroll, ScrollY));

        Rectangle Thumb
        {
            get
            {
                if (!ShowBar) return Rectangle.Empty;
                int track = Height - S(8);
                int th = Math.Max(S(40), (int)((long)track * Height / ContentHeight));
                int y = S(4) + (MaxScroll == 0 ? 0 : (int)((long)(track - th) * ScrollY / MaxScroll));
                return new Rectangle(Width - BarWidth + S(2), y, BarWidth - S(4), th);
            }
        }

        protected void PaintScrollbar(Graphics g)
        {
            if (!ShowBar) return;
            var t = Thumb;
            var c = dragThumb || thumbHover ? Theme.Blend(Theme.P.ScrollThumb, Theme.P.Text, 0.25f) : Theme.P.ScrollThumb;
            Theme.FillRound(g, c, t, t.Width / 2f);
        }

        public void ScrollTo(int y) { ScrollY = y; ClampScroll(); Invalidate(); }

        public void EnsureVisible(int top, int bottom)
        {
            if (top < ScrollY) ScrollY = top;
            else if (bottom > ScrollY + Height) ScrollY = bottom - Height;
            ClampScroll();
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollY -= e.Delta * S(56) / 120;
            ClampScroll();
            Invalidate();
            base.OnMouseWheel(e);
        }

        /// <summary>Handles scrollbar clicks; returns true if the event was consumed.</summary>
        protected bool ScrollbarMouseDown(MouseEventArgs e)
        {
            if (!ShowBar || e.X < Width - BarWidth) return false;
            var t = Thumb;
            if (t.Contains(e.Location)) { dragThumb = true; dragStartY = e.Y; dragStartScroll = ScrollY; Capture = true; }
            else { ScrollY += e.Y < t.Y ? -Height : Height; ClampScroll(); Invalidate(); }
            return true;
        }

        protected bool ScrollbarMouseMove(MouseEventArgs e)
        {
            bool over = ShowBar && Thumb.Contains(e.Location);
            if (over != thumbHover) { thumbHover = over; Invalidate(); }
            if (!dragThumb) return false;
            int track = Height - S(8), th = Thumb.Height;
            if (track > th) ScrollY = dragStartScroll + (int)((long)(e.Y - dragStartY) * MaxScroll / (track - th));
            ClampScroll();
            Invalidate();
            return true;
        }

        protected bool ScrollbarMouseUp()
        {
            if (!dragThumb) return false;
            dragThumb = false;
            Capture = false;
            Invalidate();
            return true;
        }

        protected override void OnMouseLeave(EventArgs e) { thumbHover = false; Invalidate(); base.OnMouseLeave(e); }
    }

    /// <summary>One mod in the list.</summary>
    public sealed class ModRow
    {
        public ModSelection Sel;
        public ModInfo Info;
        public ModStats Stats;
        public bool CanCheck => Info.Mergeable && !Info.Missing;
        public bool Checked => Sel.Enabled && CanCheck;
    }

    /// <summary>The mod list: cards with a checkbox, drag &amp; drop and up/down buttons.</summary>
    public sealed class ModListView : ScrollList
    {
        public List<ModRow> Rows = new List<ModRow>();
        public Func<ModRow, string> Subtitle = r => "";
        public event Action<ModRow> Toggled;
        public event Action<ModRow, int> Moved;
        public event Action<ModRow, Point> MenuRequested;
        public string EmptyText = "No mods found.";
        int selectedIndex = -1, hoverIndex = -1, hoverPart;
        int pressIndex = -1, dropIndex = -1;
        Point pressPoint;
        bool dragging;
        readonly Timer autoScroll = new Timer { Interval = 30 };
        int autoScrollDir;

        protected override AccessibleObject CreateAccessibilityInstance() => new ListAccessible(this);

        sealed class ListAccessible : ControlAccessibleObject
        {
            readonly ModListView list;
            public ListAccessible(ModListView l) : base(l) { list = l; }
            public override AccessibleRole Role => AccessibleRole.List;
            public override int GetChildCount() => list.Rows.Count;
            public override AccessibleObject GetChild(int index) => index >= 0 && index < list.Rows.Count ? new RowAccessible(list, this, index) : null;
        }

        sealed class RowAccessible : AccessibleObject
        {
            readonly ModListView list;
            readonly AccessibleObject parent;
            readonly int index;
            public RowAccessible(ModListView l, AccessibleObject p, int i) { list = l; parent = p; index = i; }
            ModRow Row => list.Rows[index];
            public override string Name => Row.Sel.Name;
            public override string Description => list.Subtitle(Row);
            public override AccessibleRole Role => AccessibleRole.CheckButton;
            public override AccessibleObject Parent => parent;
            public override AccessibleStates State =>
                (Row.Checked ? AccessibleStates.Checked : AccessibleStates.None) | (Row.CanCheck ? AccessibleStates.None : AccessibleStates.Unavailable) |
                (list.selectedIndex == index ? AccessibleStates.Selected : AccessibleStates.None) | AccessibleStates.Selectable;
            public override string DefaultAction => Row.CanCheck ? (Row.Checked ? "Untick" : "Tick") : null;
            public override void DoDefaultAction() { if (Row.CanCheck && list.Enabled) list.Toggled?.Invoke(Row); }
            public override Rectangle Bounds => list.RectangleToScreen(list.RowRect(index));
        }

        public ModListView()
        {
            autoScroll.Tick += (s, e) =>
            {
                if (!dragging || autoScrollDir == 0) return;
                ScrollY += autoScrollDir * S(12);
                ClampScroll();
                UpdateDrop(PointToClient(Cursor.Position));
                Invalidate();
            };
        }

        int RowH => S(68);
        int Gap => S(8);
        Rectangle RowRect(int i) => new Rectangle(0, i * (RowH + Gap) - ScrollY, ViewWidth, RowH);

        public void SetRows(List<ModRow> rows, ModSelection keepSelected = null)
        {
            Rows = rows;
            selectedIndex = keepSelected == null ? Math.Min(selectedIndex, rows.Count - 1) : rows.FindIndex(r => r.Sel == keepSelected);
            ContentHeight = rows.Count * (RowH + Gap);
            ClampScroll();
            Invalidate();
        }

        public ModRow SelectedRow => selectedIndex >= 0 && selectedIndex < Rows.Count ? Rows[selectedIndex] : null;

        protected override void OnResize(EventArgs e) { ContentHeight = Rows.Count * (RowH + Gap); ClampScroll(); base.OnResize(e); }

        // Parts: 1 = checkbox, 2 = up, 3 = down
        Rectangle CheckRect(Rectangle r) => new Rectangle(r.X + S(34), r.Y + (r.Height - S(22)) / 2, S(22), S(22));
        static string BadgeText(ModRow row) => row.Info.Missing ? "Missing" : Friendly.Kind(row.Info.Kind);

        // One layout for painting and hit testing: the arrows sit left of the badge, whose width depends on its text.
        Rectangle BadgeRect(Rectangle r, ModRow row)
        {
            int bw = Measure(BadgeText(row), Theme.SmallBold) + S(22);
            return new Rectangle(r.Right - S(16) - bw, r.Y + (r.Height - S(26)) / 2, bw, S(26));
        }

        Rectangle UpRect(Rectangle r, ModRow row) => new Rectangle(BadgeRect(r, row).Left - S(76), r.Y + (r.Height - S(30)) / 2, S(30), S(30));
        Rectangle DownRect(Rectangle r, ModRow row) { var u = UpRect(r, row); return new Rectangle(u.Right + S(4), u.Y, u.Width, u.Height); }

        int HitPart(int i, Point pt)
        {
            var r = RowRect(i);
            if (CheckRect(r).Contains(pt) || (pt.X < CheckRect(r).Right + S(4) && pt.X > r.X + S(26))) return 1;
            if (i == hoverIndex || i == selectedIndex)
            {
                if (UpRect(r, Rows[i]).Contains(pt)) return 2;
                if (DownRect(r, Rows[i]).Contains(pt)) return 3;
            }
            return 0;
        }

        int IndexAt(Point pt)
        {
            for (int i = 0; i < Rows.Count; i++) if (RowRect(i).Contains(pt)) return i;
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Smooth(g);
            var p = Theme.P;
            if (Rows.Count == 0)
            {
                PaintText(g, EmptyText, Theme.Body, new Rectangle(S(20), S(20), Width - S(40), S(60)), p.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
                return;
            }
            int rank = 0;
            var ranks = new int[Rows.Count];
            for (int i = 0; i < Rows.Count; i++) if (Rows[i].Checked) ranks[i] = ++rank;

            for (int i = 0; i < Rows.Count; i++)
            {
                var r = RowRect(i);
                if (r.Bottom < 0 || r.Top > Height) continue;
                var row = Rows[i];
                bool sel = i == selectedIndex, hov = i == hoverIndex, dim = !row.CanCheck;
                var rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1.5f, r.Height - 1.5f);
                Theme.FillRound(g, sel ? p.Selection : hov ? p.SurfaceHover : p.Surface, rf, S(12));
                Theme.StrokeRound(g, sel ? Theme.Blend(p.Selection, p.Accent, 0.55f) : p.Border, rf, S(12), sel ? 1.5f : 1f);

                // Drag grip
                using (var b = new SolidBrush(p.TextFaint))
                    for (int gx = 0; gx < 2; gx++)
                        for (int gy = 0; gy < 3; gy++)
                            g.FillEllipse(b, r.X + S(14) + gx * S(6), r.Y + r.Height / 2 - S(8) + gy * S(6), S(3), S(3));

                // Checkbox
                var cb = CheckRect(r);
                if (row.Checked)
                {
                    Theme.FillRound(g, p.Accent, cb, S(6));
                    GlyphText(g, Theme.GCheck, 10f, cb, p.AccentText);
                }
                else
                {
                    Theme.FillRound(g, dim ? Theme.Blend(p.Surface, p.Text, 0.05f) : p.Input, cb, S(6));
                    Theme.StrokeRound(g, dim ? p.Border : p.InputBorder, cb, S(6), 1.5f);
                }

                // Priority number of ticked mods
                int x = cb.Right + S(14);
                if (ranks[i] > 0)
                {
                    var rc = new Rectangle(x, r.Y + (r.Height - S(28)) / 2, S(28), S(28));
                    using (var b = new SolidBrush(p.AccentSoft)) g.FillEllipse(b, rc);
                    PaintText(g, ranks[i].ToString(), Theme.SmallBold, rc, p.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                x += S(40);

                // Badge
                string badge = BadgeText(row);
                Color bfg = row.Info.Missing ? p.Danger : row.Info.Kind == ModKind.Map ? p.Success : row.Info.Kind == ModKind.Mixed ? p.Accent : p.Info;
                Color bbg = row.Info.Missing ? p.DangerSoft : row.Info.Kind == ModKind.Map ? p.SuccessSoft : row.Info.Kind == ModKind.Mixed ? p.AccentSoft : p.InfoSoft;
                var br = BadgeRect(r, row);
                Theme.FillRound(g, bbg, br, S(13));
                PaintText(g, badge, Theme.SmallBold, br, bfg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                // Up / down buttons (on hover or selection)
                int textRight = br.Left - S(12);
                if ((hov || sel) && !dragging)
                {
                    var ur = UpRect(r, row);
                    var dr = DownRect(r, row);
                    foreach (var (rect, glyph, part) in new[] { (ur, Theme.GUp, 2), (dr, Theme.GDown, 3) })
                    {
                        bool h = hov && hoverPart == part;
                        Theme.FillRound(g, h ? Theme.Blend(p.Surface, p.Text, 0.10f) : Theme.Blend(sel ? p.Selection : hov ? p.SurfaceHover : p.Surface, p.Text, 0.04f), rect, S(8));
                        GlyphText(g, glyph, 9f, rect, h ? p.Text : p.TextMuted);
                    }
                    textRight = ur.Left - S(10);
                }

                // Name and subtitle
                var nameR = new Rectangle(x, r.Y + S(13), Math.Max(S(40), textRight - x), S(22));
                PaintText(g, row.Sel.Name, Theme.BodyBold, nameR, dim ? p.TextMuted : p.Text);
                var subR = new Rectangle(x, r.Y + S(36), Math.Max(S(40), textRight - x), S(20));
                PaintText(g, Subtitle(row), Theme.Small, subR, row.Info.Missing ? p.Danger : p.TextMuted);
            }

            if (dragging && dropIndex >= 0)
            {
                int y = dropIndex * (RowH + Gap) - ScrollY - Gap / 2;
                Theme.FillRound(g, p.Accent, new RectangleF(S(6), y - S(2), ViewWidth - S(12), S(4)), S(2));
            }
            PaintScrollbar(g);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (ScrollbarMouseDown(e)) return;
            int i = IndexAt(e.Location);
            if (i < 0) return;
            if (e.Button == MouseButtons.Right)
            {
                selectedIndex = i; Invalidate();
                MenuRequested?.Invoke(Rows[i], e.Location);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            int part = HitPart(i, e.Location);
            selectedIndex = i;
            if (part == 1) { if (Rows[i].CanCheck) Toggled?.Invoke(Rows[i]); }
            else if (part == 2) { if (i > 0) { Moved?.Invoke(Rows[i], i - 1); selectedIndex = i - 1; } }
            else if (part == 3) { if (i < Rows.Count - 1) { Moved?.Invoke(Rows[i], i + 1); selectedIndex = i + 1; } }
            else { pressIndex = i; pressPoint = e.Location; Capture = true; }
            Invalidate();
            base.OnMouseDown(e);
        }

        void UpdateDrop(Point pt)
        {
            int y = pt.Y + ScrollY;
            dropIndex = Math.Max(0, Math.Min(Rows.Count, (y + (RowH + Gap) / 2) / (RowH + Gap)));
            autoScrollDir = pt.Y < S(30) ? -1 : pt.Y > Height - S(30) ? 1 : 0;
            autoScroll.Enabled = autoScrollDir != 0;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (ScrollbarMouseMove(e)) return;
            if (pressIndex >= 0 && e.Button == MouseButtons.Left)
            {
                if (!dragging && (Math.Abs(e.Y - pressPoint.Y) > S(6) || Math.Abs(e.X - pressPoint.X) > S(6)))
                {
                    dragging = true;
                    Cursor = Cursors.SizeNS;
                }
                if (dragging) { UpdateDrop(e.Location); Invalidate(); return; }
            }
            int i = IndexAt(e.Location);
            int part = i >= 0 ? HitPart(i, e.Location) : 0;
            if (i != hoverIndex || part != hoverPart)
            {
                hoverIndex = i; hoverPart = part;
                Cursor = part > 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (ScrollbarMouseUp()) return;
            if (dragging)
            {
                int target = dropIndex;
                if (target > pressIndex) target--;
                if (target >= 0 && target != pressIndex) { Moved?.Invoke(Rows[pressIndex], target); selectedIndex = target; }
            }
            dragging = false; dropIndex = -1; pressIndex = -1; autoScroll.Enabled = false;
            Capture = false;
            Cursor = Cursors.Default;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e) { if (!dragging) { hoverIndex = -1; hoverPart = 0; } base.OnMouseLeave(e); }

        protected override bool IsInputKey(Keys k) => k == Keys.Up || k == Keys.Down || k == (Keys.Up | Keys.Alt) || k == (Keys.Down | Keys.Alt) || base.IsInputKey(k);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (Rows.Count == 0) return;
            int dir = e.KeyCode == Keys.Up ? -1 : e.KeyCode == Keys.Down ? 1 : 0;
            if (dir != 0)
            {
                int cur = Math.Max(0, selectedIndex);
                int next = Math.Max(0, Math.Min(Rows.Count - 1, cur + dir));
                if (e.Alt && selectedIndex >= 0 && next != cur) Moved?.Invoke(Rows[cur], next);
                selectedIndex = next;
                var r = RowRect(next);
                EnsureVisible(r.Top + ScrollY, r.Bottom + ScrollY);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Space && SelectedRow != null && SelectedRow.CanCheck) { Toggled?.Invoke(SelectedRow); e.Handled = true; }
            Invalidate();
            base.OnKeyDown(e);
        }

        protected override void Dispose(bool disposing) { if (disposing) autoScroll.Dispose(); base.Dispose(disposing); }
    }

    public sealed class ConflictOption
    {
        public string Label, Value, ModId, RawValue;
        public bool IsVanilla, IsCustom, Chosen;
    }

    public sealed class ConflictItem
    {
        public Conflict C;
        public string Title, Context, Area, Reason;
        public bool Pending;
        public List<ConflictOption> Options = new List<ConflictOption>();
    }

    /// <summary>Conflicts as cards, grouped by game area; each value is a clickable option.</summary>
    public sealed class ConflictListView : ScrollList
    {
        List<ConflictItem> items = new List<ConflictItem>();
        public event Action<ConflictItem, ConflictOption> OptionClicked;
        public string EmptyTitle = "No conflicts", EmptyText = "";
        readonly List<(int Y, int H, string Header, ConflictItem Item)> layout = new List<(int, int, string, ConflictItem)>();
        ConflictItem hoverItem;
        int hoverOption = -1;

        public void SetItems(List<ConflictItem> list) { items = list; Relayout(); Invalidate(); }

        protected override AccessibleObject CreateAccessibilityInstance() => new ListAccessible(this);

        sealed class ListAccessible : ControlAccessibleObject
        {
            readonly ConflictListView view;
            public ListAccessible(ConflictListView v) : base(v) { view = v; }
            public override AccessibleRole Role => AccessibleRole.List;
            List<(ConflictItem It, ConflictOption O)> All => view.items.SelectMany(i => i.Options.Select(o => (i, o))).ToList();
            public override int GetChildCount() => All.Count;
            public override AccessibleObject GetChild(int index) { var a = All; return index >= 0 && index < a.Count ? new OptionAccessible(view, this, a[index].It, a[index].O) : null; }
        }

        sealed class OptionAccessible : AccessibleObject
        {
            readonly ConflictListView view;
            readonly AccessibleObject parent;
            readonly ConflictItem item;
            readonly ConflictOption option;
            public OptionAccessible(ConflictListView v, AccessibleObject p, ConflictItem it, ConflictOption o) { view = v; parent = p; item = it; option = o; }
            public override string Name => item.Title + ": " + option.Label + (string.IsNullOrEmpty(option.Value) ? "" : " " + option.Value);
            public override string Description => item.Context;
            public override AccessibleRole Role => AccessibleRole.RadioButton;
            public override AccessibleObject Parent => parent;
            public override AccessibleStates State => option.Chosen ? AccessibleStates.Checked : AccessibleStates.None;
            public override string DefaultAction => "Use";
            public override void DoDefaultAction() { if (view.Enabled) view.OptionClicked?.Invoke(item, option); }
        }

        int OptH => S(36);
        int CardH(ConflictItem it) => S(18) + S(24) + (string.IsNullOrEmpty(it.Context) ? 0 : S(20)) + (it.Pending ? S(20) : 0) + S(10) + it.Options.Count * (OptH + S(4)) + S(26) + S(12);

        void Relayout()
        {
            layout.Clear();
            int y = 0;
            string area = null;
            foreach (var it in items)
            {
                if (it.Area != area)
                {
                    area = it.Area;
                    int n = items.Count(x => x.Area == area);
                    layout.Add((y, S(38), area.ToUpperInvariant() + "  ·  " + n, null));
                    y += S(38);
                }
                int h = CardH(it);
                layout.Add((y, h, null, it));
                y += h + S(10);
            }
            ContentHeight = y;
            ClampScroll();
        }

        protected override void OnResize(EventArgs e) { Relayout(); base.OnResize(e); }

        Rectangle OptionRect(Rectangle card, ConflictItem it, int index)
        {
            int top = card.Y + S(18) + S(24) + (string.IsNullOrEmpty(it.Context) ? 0 : S(20)) + (it.Pending ? S(20) : 0) + S(10);
            return new Rectangle(card.X + S(16), top + index * (OptH + S(4)), card.Width - S(32), OptH);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Smooth(g);
            var p = Theme.P;
            if (items.Count == 0)
            {
                var c = new Rectangle(Width / 2 - S(32), S(40), S(64), S(64));
                using (var b = new SolidBrush(p.SuccessSoft)) g.FillEllipse(b, c);
                GlyphText(g, Theme.GCheck, 22f, c, p.Success);
                PaintText(g, EmptyTitle, Theme.Heading, new Rectangle(0, c.Bottom + S(16), Width, S(30)), p.Text, TextFormatFlags.HorizontalCenter);
                PaintText(g, EmptyText, Theme.Body, new Rectangle(S(40), c.Bottom + S(50), Width - S(80), S(60)), p.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
                return;
            }
            foreach (var (y0, h, header, it) in layout)
            {
                int y = y0 - ScrollY;
                if (y + h < 0 || y > Height) continue;
                if (header != null)
                {
                    PaintText(g, header, Theme.SmallBold, new Rectangle(S(4), y + S(10), ViewWidth, S(24)), p.TextFaint);
                    continue;
                }
                var card = new Rectangle(0, y, ViewWidth, h);
                var cf = new RectangleF(card.X + 0.5f, card.Y + 0.5f, card.Width - 1.5f, card.Height - 1.5f);
                Theme.FillRound(g, p.Surface, cf, S(12));
                Theme.StrokeRound(g, it.Pending ? Theme.Blend(p.Border, p.Warning, 0.6f) : p.Border, cf, S(12));

                int ty = card.Y + S(16);
                PaintText(g, it.Title, Theme.BodyBold, new Rectangle(card.X + S(18), ty, card.Width - S(36), S(24)), p.Text);
                ty += S(24);
                if (!string.IsNullOrEmpty(it.Context))
                {
                    PaintText(g, it.Context, Theme.Small, new Rectangle(card.X + S(18), ty, card.Width - S(36), S(20)), p.TextMuted);
                    ty += S(20);
                }
                if (it.Pending)
                {
                    PaintText(g, "Changed by you – combine again to apply", Theme.SmallBold, new Rectangle(card.X + S(18), ty, card.Width - S(36), S(20)), p.Warning);
                }

                for (int k = 0; k < it.Options.Count; k++)
                {
                    var o = it.Options[k];
                    var orc = OptionRect(card, it, k);
                    bool hov = it == hoverItem && k == hoverOption;
                    Color bg = o.Chosen ? p.SuccessSoft : hov ? p.SurfaceHover : Theme.Blend(p.Surface, p.Window, p.Dark ? 0.6f : 0.75f);
                    Theme.FillRound(g, bg, orc, S(8));
                    if (hov && !o.Chosen) Theme.StrokeRound(g, p.Border, orc, S(8));
                    var radio = new Rectangle(orc.X + S(12), orc.Y + (orc.Height - S(16)) / 2, S(16), S(16));
                    if (o.Chosen)
                    {
                        using (var b = new SolidBrush(p.Success)) g.FillEllipse(b, radio);
                        var inner = Rectangle.Inflate(radio, -S(5), -S(5));
                        using (var b = new SolidBrush(p.Surface)) g.FillEllipse(b, inner);
                    }
                    else
                    {
                        using (var pen = new Pen(p.InputBorder, 1.5f)) g.DrawEllipse(pen, radio);
                    }
                    int lx = radio.Right + S(12);
                    int tagW = o.Chosen ? Measure("✓ used", Theme.SmallBold) + S(16) : 0;
                    int valW = Math.Min(orc.Width / 2, Measure(o.Value, Theme.Mono) + S(8));
                    var labelR = new Rectangle(lx, orc.Y, orc.Right - lx - valW - tagW - S(16), orc.Height);
                    PaintText(g, o.Label, o.Chosen ? Theme.BodyBold : Theme.Body, labelR, o.IsCustom && !o.Chosen ? p.Accent : p.Text);
                    var valR = new Rectangle(orc.Right - tagW - valW - S(10), orc.Y, valW, orc.Height);
                    PaintText(g, o.Value, Theme.Mono, valR, o.Chosen ? p.Text : p.TextMuted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                    if (o.Chosen) PaintText(g, "✓ used", Theme.SmallBold, new Rectangle(orc.Right - tagW - S(6), orc.Y, tagW, orc.Height), p.Success, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                }
                var last = OptionRect(card, it, it.Options.Count);
                PaintText(g, it.Reason, Theme.Small, new Rectangle(card.X + S(18), last.Y, card.Width - S(36), S(22)), p.TextFaint);
            }
            PaintScrollbar(g);
        }

        (ConflictItem, int) Hit(Point pt)
        {
            foreach (var (y0, h, header, it) in layout)
            {
                if (it == null) continue;
                var card = new Rectangle(0, y0 - ScrollY, ViewWidth, h);
                if (!card.Contains(pt)) continue;
                for (int k = 0; k < it.Options.Count; k++) if (OptionRect(card, it, k).Contains(pt)) return (it, k);
                return (it, -1);
            }
            return (null, -1);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (ScrollbarMouseMove(e)) return;
            var (it, k) = Hit(e.Location);
            if (it != hoverItem || k != hoverOption)
            {
                hoverItem = it; hoverOption = k;
                Cursor = k >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hoverItem = null; hoverOption = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (ScrollbarMouseDown(e)) return;
            var (it, k) = Hit(e.Location);
            if (it != null && k >= 0 && e.Button == MouseButtons.Left) OptionClicked?.Invoke(it, it.Options[k]);
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e) { ScrollbarMouseUp(); base.OnMouseUp(e); }
    }

    /// <summary>Theme-aware replacement for MessageBox (and a simple input box).</summary>
    public sealed class ThemedDialog : Form
    {
        public struct Line
        {
            public string Glyph; public Color Color; public string Text;
            public Line(string glyph, Color color, string text) { Glyph = glyph; Color = color; Text = text; }
        }

        public struct Choice
        {
            public string Text; public DialogResult Result; public ButtonKind Kind;
            public Choice(string text, DialogResult result, ButtonKind kind = ButtonKind.Secondary) { Text = text; Result = result; Kind = kind; }
        }

        readonly BannerKind kind;
        readonly string title, message;
        readonly List<Line> lines;
        TextBox input;
        int S(float v) => (int)Math.Round(v * DeviceDpi / 96f);
        const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix;

        ThemedDialog(BannerKind kind, string title, string message, IEnumerable<Line> lines, Choice[] choices, string inputValue)
        {
            this.kind = kind; this.title = title; this.message = message ?? ""; this.lines = (lines ?? new Line[0]).ToList();
            Text = "TG3 ModLoader";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = MaximizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.P.Surface;
            Font = Theme.Body;
            DoubleBuffered = true;

            int width = S(540);
            int textW = width - S(110);
            int y = S(28);
            y += TextRenderer.MeasureText(title, Theme.Heading, new Size(textW, int.MaxValue), Wrap).Height + S(8);
            if (this.message.Length > 0) y += TextRenderer.MeasureText(this.message, Theme.Body, new Size(textW, int.MaxValue), Wrap).Height + S(10);
            foreach (var l in this.lines) y += Math.Max(S(24), TextRenderer.MeasureText(l.Text, Theme.Body, new Size(textW - S(30), int.MaxValue), Wrap).Height) + S(8);
            if (inputValue != null)
            {
                // Borderless text box in a rounded frame that lights up in the accent colour while typing.
                var frame = new Panel { BackColor = Theme.P.Surface };
                frame.SetBounds(S(84), y + S(2), textW, S(40));
                input = new TextBox { Text = inputValue, Font = Theme.Body, BorderStyle = BorderStyle.None, BackColor = Theme.P.Input, ForeColor = Theme.P.Text };
                input.SetBounds(S(12), (frame.Height - input.PreferredHeight) / 2, textW - S(24), input.PreferredHeight);
                frame.Controls.Add(input);
                frame.Paint += (s, e) =>
                {
                    Theme.Smooth(e.Graphics);
                    var r = new RectangleF(0.5f, 0.5f, frame.Width - 1.5f, frame.Height - 1.5f);
                    Theme.FillRound(e.Graphics, Theme.P.Input, r, S(8));
                    Theme.StrokeRound(e.Graphics, input.Focused ? Theme.P.Accent : Theme.P.InputBorder, r, S(8), input.Focused ? 1.5f : 1f);
                };
                input.GotFocus += (s, e) => frame.Invalidate();
                input.LostFocus += (s, e) => frame.Invalidate();
                frame.MouseDown += (s, e) => input.Focus();
                Controls.Add(frame);
                y += S(52);
            }
            y += S(12);
            int bx = width - S(24);
            for (int i = choices.Length - 1; i >= 0; i--)
            {
                var c = choices[i];
                var b = new FlatButton { Text = c.Text, Kind = c.Kind, Background = () => Theme.Blend(Theme.P.Surface, Theme.P.Window, 0.5f) };
                int w = Math.Max(S(96), b.PreferredWidth());
                b.SetBounds(bx - w, y + S(14), w, S(38));
                var result = c.Result;
                b.Click += (s, e) => { DialogResult = result; Close(); };
                Controls.Add(b);
                bx -= w + S(10);
                if (c.Kind == ButtonKind.Primary) AcceptButtonControl = b;
                if (result == DialogResult.Cancel || result == DialogResult.No && choices.All(x => x.Result != DialogResult.Cancel)) CancelResult = result;
            }
            footerTop = y;
            ClientSize = new Size(width, y + S(66));
        }

        Control AcceptButtonControl;
        DialogResult CancelResult = DialogResult.Cancel;
        readonly int footerTop;

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.ApplyTitleBar(this); }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (input != null) { input.Focus(); input.SelectAll(); }
            else AcceptButtonControl?.Focus();
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape) { DialogResult = CancelResult; Close(); return true; }
            if (keyData == Keys.Enter && input != null && input.Focused) { DialogResult = DialogResult.OK; Close(); return true; }
            return base.ProcessDialogKey(keyData);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Smooth(g);
            var p = Theme.P;
            Color soft, strong;
            string glyph;
            switch (kind)
            {
                case BannerKind.Success: soft = p.SuccessSoft; strong = p.Success; glyph = Theme.GCheck; break;
                case BannerKind.Warning: soft = p.WarningSoft; strong = p.Warning; glyph = Theme.GWarning; break;
                case BannerKind.Danger: soft = p.DangerSoft; strong = p.Danger; glyph = Theme.GError; break;
                default: soft = p.InfoSoft; strong = p.Info; glyph = Theme.GHelp; break;
            }
            var circle = new Rectangle(S(24), S(26), S(44), S(44));
            using (var b = new SolidBrush(soft)) g.FillEllipse(b, circle);
            using (var f = Theme.Glyph(16f)) TextRenderer.DrawText(g, glyph, f, circle, strong, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            int x = S(84), textW = ClientSize.Width - S(110), y = S(28);
            int th = TextRenderer.MeasureText(title, Theme.Heading, new Size(textW, int.MaxValue), Wrap).Height;
            TextRenderer.DrawText(g, title, Theme.Heading, new Rectangle(x, y, textW, th), p.Text, Wrap);
            y += th + S(8);
            if (message.Length > 0)
            {
                int mh = TextRenderer.MeasureText(message, Theme.Body, new Size(textW, int.MaxValue), Wrap).Height;
                TextRenderer.DrawText(g, message, Theme.Body, new Rectangle(x, y, textW, mh), p.TextMuted, Wrap);
                y += mh + S(10);
            }
            foreach (var l in lines)
            {
                int lh = Math.Max(S(24), TextRenderer.MeasureText(l.Text, Theme.Body, new Size(textW - S(30), int.MaxValue), Wrap).Height);
                using (var f = Theme.Glyph(10.5f)) TextRenderer.DrawText(g, l.Glyph, f, new Rectangle(x, y, S(22), S(22)), l.Color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, l.Text, Theme.Body, new Rectangle(x + S(30), y + S(1), textW - S(30), lh), p.Text, Wrap);
                y += lh + S(8);
            }
            using (var b = new SolidBrush(Theme.Blend(p.Surface, p.Window, 0.5f))) g.FillRectangle(b, 0, footerTop, ClientSize.Width, ClientSize.Height - footerTop);
            using (var pen = new Pen(p.Border)) g.DrawLine(pen, 0, footerTop, ClientSize.Width, footerTop);
        }

        public static DialogResult Show(IWin32Window owner, BannerKind kind, string title, string message, IEnumerable<Line> lines, params Choice[] choices)
        {
            if (choices == null || choices.Length == 0) choices = new[] { new Choice("OK", DialogResult.OK, ButtonKind.Primary) };
            using (var d = new ThemedDialog(kind, title, message, lines, choices, null)) return d.ShowDialog(owner);
        }

        public static DialogResult Show(IWin32Window owner, BannerKind kind, string title, string message, params Choice[] choices) =>
            Show(owner, kind, title, message, null, choices);

        /// <summary>Asks for a text; returns null when cancelled.</summary>
        public static string Ask(IWin32Window owner, string title, string message, string initial)
        {
            using (var d = new ThemedDialog(BannerKind.Neutral, title, message, null,
                new[] { new Choice("Cancel", DialogResult.Cancel), new Choice("Use this value", DialogResult.OK, ButtonKind.Primary) }, initial ?? ""))
                return d.ShowDialog(owner) == DialogResult.OK ? d.input.Text.Trim() : null;
        }
    }

    /// <summary>Context menu colours for both themes.</summary>
    public sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new Colors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.P.Text : Theme.P.TextFaint;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Theme.P.TextMuted; base.OnRenderArrow(e); }

        sealed class Colors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Theme.P.Surface;
            public override Color ImageMarginGradientBegin => Theme.P.Surface;
            public override Color ImageMarginGradientMiddle => Theme.P.Surface;
            public override Color ImageMarginGradientEnd => Theme.P.Surface;
            public override Color MenuBorder => Theme.P.Border;
            public override Color MenuItemBorder => Theme.P.SurfaceHover;
            public override Color MenuItemSelected => Theme.P.SurfaceHover;
            public override Color MenuItemSelectedGradientBegin => Theme.P.SurfaceHover;
            public override Color MenuItemSelectedGradientEnd => Theme.P.SurfaceHover;
            public override Color MenuItemPressedGradientBegin => Theme.P.SurfaceHover;
            public override Color MenuItemPressedGradientEnd => Theme.P.SurfaceHover;
            public override Color SeparatorDark => Theme.P.Border;
            public override Color SeparatorLight => Theme.P.Surface;
        }

        /// <summary>Renders an icon glyph as a small image for menu items.</summary>
        public static Bitmap GlyphImage(string glyph, Color color, int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            using (var f = Theme.Glyph(size * 0.55f))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                using (var b = new SolidBrush(color)) g.DrawString(glyph, f, b, new RectangleF(0, 0, size, size), sf);
            }
            return bmp;
        }
    }
}
