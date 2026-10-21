using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VClicker
{
    /// <summary>
    /// The on-screen indicator: a small pill in a screen corner, over the game. It is an ordinary separate window
    /// (nothing is injected into the game): always on top, clicks pass through it, and it never takes the focus,
    /// so it can neither swallow a click nor make Window Lock think the game lost the focus.
    /// Windows does not draw ordinary windows over exclusive full-screen games; there the beeps and the tray icon remain.
    /// </summary>
    internal sealed class Hud : Form
    {
        private const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_NOACTIVATE = 0x8000000;
        private const int WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST = 0x8;
        private const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
        private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

        private Color dot = Theme.Dim;

        public Hud()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Surface;
            ForeColor = Theme.Text;
            Font = Theme.Ui(10f, true);
            Opacity = 0.92;
            DoubleBuffered = true;
            Text = "V-Clicker HUD";
        }

        internal const int ExStyles = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= ExStyles;
                return parameters;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)MA_NOACTIVATE;
                return;
            }
            base.WndProc(ref m);
        }

        /// <summary>Shows (or updates) the pill; does nothing when nothing changed.</summary>
        public void Display(Color color, string text, Rectangle screen, HudCorner corner)
        {
            Size size = new Size(Math.Min(TextRenderer.MeasureText(text, Font).Width + Theme.Dp(44), Theme.Dp(360)), Theme.Dp(32));
            int margin = Theme.Dp(16);
            bool right = corner == HudCorner.TopRight || corner == HudCorner.BottomRight;
            bool bottom = corner == HudCorner.BottomLeft || corner == HudCorner.BottomRight;
            var bounds = new Rectangle(
                right ? screen.Right - margin - size.Width : screen.Left + margin,
                bottom ? screen.Bottom - margin - size.Height : screen.Top + margin,
                size.Width, size.Height);

            if (Visible && color == dot && text == Text && bounds == Bounds) return;
            dot = color;
            Text = text;
            if (bounds.Size != Size)
            {
                Size = bounds.Size;
                using (GraphicsPath path = Theme.RoundRect(new RectangleF(0, 0, Width, Height), Height / 2f))
                {
                    Region old = Region;
                    Region = new Region(path);
                    old?.Dispose();
                }
            }
            Location = bounds.Location;
            if (!Visible) Show();
            KeepOnTop();
            Invalidate();
        }

        /// <summary>Borderless full-screen games can be topmost too; re-assert our place without activating.</summary>
        public void KeepOnTop()
        {
            if (Visible) SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            g.Clear(Theme.Surface);
            float d = Theme.Dp(9);
            using (var brush = new SolidBrush(dot))
                g.FillEllipse(brush, Theme.Dp(14), (Height - d) / 2, d, d);
            var area = new Rectangle(Theme.Dp(30), 0, Width - Theme.Dp(40), Height);
            TextRenderer.DrawText(g, Text, Font, area, Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix |
                TextFormatFlags.EndEllipsis);
        }
    }
}
