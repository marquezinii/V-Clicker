using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VClicker
{
    /// <summary>Colors, fonts and drawing helpers shared by every control. Palette taken from the app icon.</summary>
    internal static class Theme
    {
        public static readonly Color Bg = Rgb(0x0B1220);
        public static readonly Color Surface = Rgb(0x121C30);
        public static readonly Color Field = Rgb(0x1A263E);
        public static readonly Color Border = Rgb(0x2A3A5E);
        public static readonly Color Text = Rgb(0xE8EEFA);
        public static readonly Color Dim = Rgb(0x8A9BBB);
        public static readonly Color Accent = Rgb(0x4F86F0);
        public static readonly Color AccentHover = Rgb(0x6A9AF5);
        public static readonly Color AccentPressed = Rgb(0x3A6BD1);
        public static readonly Color Danger = Rgb(0xE5546B);
        public static readonly Color Success = Rgb(0x3DD6A3);
        public static readonly Color Warning = Rgb(0xF2B04C);

        private static readonly Dictionary<string, Font> Fonts = new Dictionary<string, Font>();

        private static Color Rgb(int rgb) => Color.FromArgb(0xFF, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

        /// <summary>Cached UI font. Sizes are in points, so Windows scales them for the monitor DPI.</summary>
        public static Font Ui(float points, bool semibold = false)
        {
            string key = points.ToString("0.##") + (semibold ? "s" : "r");
            if (!Fonts.TryGetValue(key, out Font font))
            {
                font = new Font(semibold ? "Segoe UI Semibold" : "Segoe UI", points, FontStyle.Regular, GraphicsUnit.Point);
                Fonts[key] = font;
            }
            return font;
        }

        /// <summary>The first opaque background color found walking up the parents (for painting rounded corners).</summary>
        public static Color Backdrop(Control control)
        {
            for (Control c = control.Parent; c != null; c = c.Parent)
                if (c.BackColor.A == 255) return c.BackColor;
            return Bg;
        }

        /// <summary>
        /// Display scale (1.0 = 96 DPI). The app is system-DPI aware and every pixel metric goes through
        /// Dp()/Pad()/Sz(): Windows Forms' own auto-scaling does not scale table-layout styles reliably.
        /// </summary>
        public static readonly float Scale = ReadScale();

        private static float ReadScale()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return Math.Max(1f, g.DpiX / 96f);
        }

        public static int Dp(int pixels) => (int)Math.Round(pixels * Scale);
        public static Padding Pad(int all) => new Padding(Dp(all));
        public static Padding Pad(int left, int top, int right, int bottom) => new Padding(Dp(left), Dp(top), Dp(right), Dp(bottom));
        public static Size Sz(int width, int height) => new Size(Dp(width), Dp(height));

        public static float Dpi(Control control) => Scale;

        public static Color Mix(Color a, Color b, float amount) => Color.FromArgb(
            (int)(a.R + (b.R - a.R) * amount),
            (int)(a.G + (b.G - a.G) * amount),
            (int)(a.B + (b.B - a.B) * amount));

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
        }

        public static GraphicsPath RoundRect(RectangleF bounds, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            var path = new GraphicsPath();
            if (d <= 0.5f)
            {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public const TextFormatFlags Centered =
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
    }
}
