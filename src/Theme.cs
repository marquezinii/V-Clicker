/*
 * Visual world: "custom keyboard".
 * THESIS: every action is a key. F6/F7/F8 are real keycaps and Start is the wide amber key; no dashboard of rounded
 *   cards with a blue accent, which is what the category ships.
 * OWN-WORLD: graphite anodized plate, PBT keycaps in two flat layers (face + skirt), one amber accent key, off-white
 *   legends in Bahnschrift (DIN), LEDs for on/off, black OLED glass for live numbers, silkscreen labels with a hairline
 *   instead of cards, recessed wells for anything typed.
 * STORY: the player reads the OLED (clicking or not, how fast), presses the amber key or F6, tunes speed on the number
 *   row; everything else sits in labelled key clusters.
 * FIRST VIEWPORT: OLED top-left (requested CPS large, three readouts, message line), the amber Start key under it, the
 *   speed cluster and the hotkey caps below; click, position, Window Lock and options clusters on the right.
 * FORM: grounded direction 4 of 7 (custom keyboard), seed 18f56269.
 * FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, and DESIGN.md
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VClicker
{
    /// <summary>
    /// How a keycap is colored: graphite alpha (options, the number row), grey modifier (actions), light (a chosen option),
    /// amber accent (the one primary action), coral stop (stop, delete).
    /// </summary>
    internal enum KeyTone { Alpha, Mod, Light, Accent, Stop }

    /// <summary>Colors, fonts and drawing helpers shared by every control (see the world description above).</summary>
    internal static class Theme
    {
        // The plate, the recessed wells and the display glass.
        public static readonly Color Bg = Rgb(0x1A1B1F);
        public static readonly Color Field = Rgb(0x111215);
        public static readonly Color Glass = Rgb(0x0B0C0E);
        public static readonly Color Border = Rgb(0x2D2F35);

        // Keycaps: face and skirt.
        public static readonly Color Key = Rgb(0x33353C);
        public static readonly Color KeySide = Rgb(0x16171A);
        public static readonly Color Mod = Rgb(0x4B4E57);
        public static readonly Color ModSide = Rgb(0x26282D);
        public static readonly Color Cap = Rgb(0xE7E2D6);
        public static readonly Color CapSide = Rgb(0xB3AC9C);
        public static readonly Color Accent = Rgb(0xF5C542);
        public static readonly Color AccentSide = Rgb(0xBF952A);
        public static readonly Color Stop = Rgb(0xF0645A);
        public static readonly Color StopSide = Rgb(0xB9483F);

        // Legends: off-white on graphite, ink on light and colored keys.
        public static readonly Color Text = Rgb(0xECE8DF);
        public static readonly Color Dim = Rgb(0xA3A19A); // 4.5:1 even on a graphite key face
        public static readonly Color Ink = Rgb(0x17181B);

        // LEDs and messages.
        public static readonly Color Success = Rgb(0x5FE08C);
        public static readonly Color Warning = Rgb(0xFFA53D);
        public static readonly Color Danger = Rgb(0xFF6A5C);

        private static readonly Dictionary<string, Font> Fonts = new Dictionary<string, Font>();
        private static readonly HashSet<string> Families = InstalledFamilies();

        private static Color Rgb(int rgb) => Color.FromArgb(0xFF, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

        private static HashSet<string> InstalledFamilies()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FontFamily family in FontFamily.Families) names.Add(family.Name);
            return names;
        }

        private static Font Cached(string family, float points)
        {
            string key = family + "|" + points.ToString("0.##");
            if (!Fonts.TryGetValue(key, out Font font))
            {
                font = new Font(family, points, FontStyle.Regular, GraphicsUnit.Point);
                Fonts[key] = font;
            }
            return font;
        }

        /// <summary>Sentences and help text (Segoe UI). Sizes are in points, so Windows scales them for the monitor DPI.</summary>
        public static Font Ui(float points, bool semibold = false) => Cached(semibold ? "Segoe UI Semibold" : "Segoe UI", points);

        /// <summary>
        /// Key legends, labels and numbers: Bahnschrift, the DIN face that ships with Windows 10 1709+ (the app needs 1903).
        /// Falls back to Segoe UI if it was removed.
        /// </summary>
        public static Font Legend(float points, bool bold = false, bool light = false)
        {
            if (!Families.Contains("Bahnschrift")) return Ui(points, bold);
            return Cached(bold ? "Bahnschrift SemiBold" : light ? "Bahnschrift Light" : "Bahnschrift", points);
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

        public const TextFormatFlags LeftMiddle =
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;

        // ------------------------------------------------------------------ the materials

        /// <summary>The legend color that reads on a key of this tone.</summary>
        public static Color LegendOn(KeyTone tone) => tone == KeyTone.Alpha || tone == KeyTone.Mod ? Text : Ink;

        public static Color FaceColor(KeyTone tone) =>
            tone == KeyTone.Light ? Cap : tone == KeyTone.Accent ? Accent : tone == KeyTone.Stop ? Stop : tone == KeyTone.Mod ? Mod : Key;

        private static Color SideColor(KeyTone tone) =>
            tone == KeyTone.Light ? CapSide : tone == KeyTone.Accent ? AccentSide : tone == KeyTone.Stop ? StopSide : tone == KeyTone.Mod ? ModSide : KeySide;

        /// <summary>
        /// A keycap seen from the front: a skirt with a smaller face on it, a soft shadow on the plate. Pressed, the cap sits
        /// lower and the front of the skirt shrinks. Returns the face, where the legend goes.
        /// </summary>
        public static RectangleF DrawKey(Graphics g, RectangleF bounds, KeyTone tone, bool pressed, bool hot, bool enabled)
        {
            float k = Scale;
            Color face = FaceColor(tone);
            Color side = SideColor(tone);
            if (hot && enabled) face = Mix(face, Color.White, tone == KeyTone.Alpha || tone == KeyTone.Mod ? 0.08f : 0.14f);
            if (!enabled)
            {
                face = Mix(face, Bg, 0.55f);
                side = Mix(side, Bg, 0.55f);
            }

            float travel = pressed ? 1.5f * k : 0;
            RectangleF skirt = RectangleF.FromLTRB(bounds.Left + k, bounds.Top + travel, bounds.Right - k, bounds.Bottom - 2 * k);
            float radius = Math.Min(5 * k, skirt.Height / 3);
            if (enabled && !pressed)
            {
                // A short soft shadow: offset down, two widening passes.
                for (int pass = 0; pass < 2; pass++)
                {
                    RectangleF shade = RectangleF.Inflate(skirt, pass * k, pass * k);
                    shade.Offset(0, (1.5f + pass) * k);
                    using (GraphicsPath path = RoundRect(shade, radius + pass * k))
                    using (var brush = new SolidBrush(Color.FromArgb(pass == 0 ? 90 : 35, 0, 0, 0)))
                        g.FillPath(brush, path);
                }
            }
            using (GraphicsPath path = RoundRect(skirt, radius))
            using (var brush = new SolidBrush(side))
                g.FillPath(brush, path);

            // The face: a little narrower than the skirt, the front of the skirt showing below it, and a slight dish
            // (lighter at the top edge, catching the light).
            RectangleF top = RectangleF.FromLTRB(skirt.Left + 2.5f * k, skirt.Top + k, skirt.Right - 2.5f * k,
                skirt.Bottom - (pressed ? 2.5f : 5.5f) * k);
            using (GraphicsPath path = RoundRect(top, Math.Max(1, radius - k)))
            using (var brush = new LinearGradientBrush(RectangleF.Inflate(top, 0, 1), Mix(face, Color.White, enabled ? 0.07f : 0.03f), face, LinearGradientMode.Vertical))
                g.FillPath(brush, path);
            return top;
        }

        /// <summary>A focus ring around a key or well: the amber accent, or ink on the amber key itself.</summary>
        public static void DrawFocus(Graphics g, RectangleF bounds, float radius, KeyTone tone = KeyTone.Alpha)
        {
            using (GraphicsPath path = RoundRect(bounds, radius))
            using (var pen = new Pen(tone == KeyTone.Accent ? Ink : Accent, Math.Max(1.5f, 1.5f * Scale)))
                g.DrawPath(pen, path);
        }

        /// <summary>A recess in the plate: dark fill, shaded edge (dark above, catching light below), or a colored edge.</summary>
        public static void DrawWell(Graphics g, RectangleF bounds, float radius, Color? edge = null, Color? fill = null)
        {
            using (GraphicsPath path = RoundRect(bounds, radius))
            {
                using (var brush = new SolidBrush(fill ?? Field))
                    g.FillPath(brush, path);
                if (edge.HasValue)
                {
                    using (var pen = new Pen(edge.Value, Math.Max(1.5f, 1.5f * Scale))) g.DrawPath(pen, path);
                }
                else if (bounds.Height > 1)
                {
                    using (var shade = new LinearGradientBrush(bounds, Rgb(0x070809), Mix(Border, Text, 0.08f), LinearGradientMode.Vertical))
                    using (var pen = new Pen(shade, 1f))
                        g.DrawPath(pen, path);
                }
            }
        }

        /// <summary>
        /// A display module set into the plate: a dark bezel (shaded like a recess) around the black glass. Content goes
        /// inside the bezel, at least 4 dp in.
        /// </summary>
        public static void DrawGlass(Graphics g, RectangleF bounds)
        {
            float k = Scale;
            DrawWell(g, bounds, 11 * k, null, Mix(Bg, Color.Black, 0.45f));
            RectangleF glass = RectangleF.Inflate(bounds, -3.5f * k, -3.5f * k);
            using (GraphicsPath path = RoundRect(glass, 8 * k))
            using (var fill = new SolidBrush(Glass))
            using (var edge = new Pen(Mix(Glass, Border, 0.45f), 1f))
            {
                g.FillPath(fill, path);
                g.DrawPath(edge, path);
            }
        }

        /// <summary>A round LED: lit is its color with a small highlight; unlit is the same color, dark.</summary>
        public static void DrawLed(Graphics g, PointF center, float diameter, Color color, bool lit)
        {
            var dot = new RectangleF(center.X - diameter / 2, center.Y - diameter / 2, diameter, diameter);
            using (var brush = new SolidBrush(lit ? color : Mix(color, Color.Black, 0.74f)))
                g.FillEllipse(brush, dot);
            if (lit)
            {
                float h = diameter * 0.36f;
                using (var shine = new SolidBrush(Color.FromArgb(120, Color.White)))
                    g.FillEllipse(shine, dot.Left + diameter * 0.2f, dot.Top + diameter * 0.16f, h, h);
            }
            else
            {
                using (var pen = new Pen(Color.FromArgb(70, color), 1f)) g.DrawEllipse(pen, dot);
            }
        }

        /// <summary>
        /// Silkscreen lettering: capitals with open letter spacing, one character at a time (GDI has no tracking).
        /// Returns the width drawn.
        /// </summary>
        public static int DrawTracked(Graphics g, string text, Font font, int x, Rectangle row, Color color, float tracking)
        {
            int start = x;
            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine |
                                          TextFormatFlags.VerticalCenter | TextFormatFlags.Left;
            foreach (char c in text)
            {
                string glyph = c.ToString();
                int width = c == ' ' ? (int)(font.SizeInPoints * Scale * 0.4f)
                          : TextRenderer.MeasureText(g, glyph, font, Size.Empty, flags).Width;
                if (x + width > row.Right) break;
                TextRenderer.DrawText(g, glyph, font, new Rectangle(x, row.Top, width + 2, row.Height), color, flags);
                x += width + (int)Math.Round(tracking);
            }
            return x - start;
        }
    }
}
