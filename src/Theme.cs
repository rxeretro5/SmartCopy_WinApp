using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace QuickCopy
{
    internal static class Theme
    {
        public static readonly Color Header = Color.FromArgb(30, 90, 168);
        public static readonly Color HeaderHover = Color.FromArgb(52, 112, 190);
        public static readonly Color HeaderText = Color.White;
        public static readonly Color Back = Color.FromArgb(242, 245, 249);
        public static readonly Color Border = Color.FromArgb(160, 176, 198);
        public static readonly Color Card = Color.White;
        public static readonly Color CardBorder = Color.FromArgb(218, 225, 234);
        public static readonly Color CardHover = Color.FromArgb(230, 240, 253);
        public static readonly Color CardHoverBorder = Color.FromArgb(120, 165, 225);
        public static readonly Color Accent = Color.FromArgb(30, 90, 168);
        public static readonly Color Success = Color.FromArgb(220, 245, 226);
        public static readonly Color SuccessBorder = Color.FromArgb(76, 175, 96);
        public static readonly Color SuccessText = Color.FromArgb(30, 120, 50);
        public static readonly Color Text = Color.FromArgb(33, 37, 41);
        public static readonly Color SubText = Color.FromArgb(105, 115, 128);
        public static readonly Color VarText = Color.FromArgb(170, 90, 0);

        public static float Scale = 1f;

        private static string _uiFontName;
        private static string _iconFontName;

        public static int S(int px) { return (int)Math.Round(px * Scale); }

        /// <summary>ฟอนต์ที่อ่านภาษาไทยได้ชัด: Leelawadee UI (Win8+) → Tahoma</summary>
        public static string UiFontName
        {
            get
            {
                if (_uiFontName == null)
                    _uiFontName = FirstInstalled(new[] { "Leelawadee UI", "Leelawadee", "Tahoma" }) ?? SystemFonts.MessageBoxFont.Name;
                return _uiFontName;
            }
        }

        /// <summary>ฟอนต์ไอคอนของ Windows 10/11 (ถ้าไม่มีจะใช้ตัวอักษรธรรมดาแทน)</summary>
        public static string IconFontName
        {
            get
            {
                if (_iconFontName == null)
                    _iconFontName = FirstInstalled(new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" }) ?? "";
                return _iconFontName;
            }
        }

        public static bool HasIconFont { get { return IconFontName.Length > 0; } }

        private static string FirstInstalled(string[] names)
        {
            using (var fonts = new InstalledFontCollection())
            {
                var installed = fonts.Families.Select(f => f.Name).ToList();
                foreach (var n in names)
                    if (installed.Any(i => string.Equals(i, n, StringComparison.OrdinalIgnoreCase)))
                        return n;
            }
            return null;
        }

        public static Font UiFont(float size, FontStyle style = FontStyle.Regular)
        {
            return new Font(UiFontName, size, style);
        }

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = Math.Max(1, radius * 2);
            if (d > r.Height) d = r.Height;
            if (d > r.Width) d = r.Width;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Icon LoadAppIcon(Size size)
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("QuickCopy.app.ico"))
                {
                    if (s != null) return new Icon(s, size);
                }
            }
            catch { }
            return DrawFallbackIcon(size.Width);
        }

        private static Icon DrawFallbackIcon(int size)
        {
            using (var bmp = new Bitmap(size, size))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(Header))
                using (var p = RoundRect(new Rectangle(0, 0, size - 1, size - 1), size / 5))
                    g.FillPath(b, p);
                int m = size / 4;
                g.FillRectangle(Brushes.White, m, m, size - 2 * m, size - 2 * m);
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }

    /// <summary>ปุ่มไอคอนแบบแบน ใช้บนแถบหัวและแถบเครื่องมือ</summary>
    internal class IconButton : Control
    {
        private bool _hover, _down, _checked;
        public string Glyph;      // อักขระในฟอนต์ไอคอน Segoe
        public string Fallback;   // ใช้เมื่อเครื่องไม่มีฟอนต์ไอคอน
        public Color HoverColor = Theme.HeaderHover;
        public Color CheckedColor = Color.FromArgb(255, 196, 61);
        public float GlyphSize = 10f;

        public IconButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
            ForeColor = Theme.HeaderText;
            BackColor = Color.Transparent;
        }

        public bool Checked
        {
            get { return _checked; }
            set { _checked = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(1, 1, Width - 3, Height - 3);
            if (_hover || _down)
            {
                using (var b = new SolidBrush(_down ? ControlPaint.Dark(HoverColor, 0.05f) : HoverColor))
                using (var p = Theme.RoundRect(r, Theme.S(4)))
                    g.FillPath(b, p);
            }
            Color fg = _checked ? CheckedColor : (Enabled ? ForeColor : Color.Gray);
            bool icon = Theme.HasIconFont && !string.IsNullOrEmpty(Glyph);
            string text = icon ? Glyph : (Fallback ?? Text);
            using (var f = icon ? new Font(Theme.IconFontName, GlyphSize) : Theme.UiFont(GlyphSize, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, text, f, ClientRectangle, fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
        }
    }
}
