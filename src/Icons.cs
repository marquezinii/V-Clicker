using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace VClicker
{
    internal enum Glyph
    {
        None,
        CornerTopLeft, CornerTopRight, CornerBottomLeft, CornerBottomRight,
        Click, DoubleClick, Target, Drag, Hover, Scroll, Type, Key, Wait, WaitFor, Lock, Unknown
    }

    /// <summary>
    /// Line icons drawn on a 16-unit grid with one stroke weight (no font glyphs or emoji: those render differently on every
    /// Windows build and never match the legends).
    /// </summary>
    internal static class Icons
    {
        public static Glyph ForCorner(HudCorner corner) => Glyph.CornerTopLeft + (int)corner;

        public static void Draw(Graphics g, Glyph glyph, RectangleF box, Color color)
        {
            if (glyph == Glyph.None) return;
            float s = Math.Min(box.Width, box.Height) / 16f;
            float x0 = box.X + (box.Width - 16 * s) / 2, y0 = box.Y + (box.Height - 16 * s) / 2;
            PointF P(float x, float y) => new PointF(x0 + x * s, y0 + y * s);
            RectangleF R(float x, float y, float w, float h) => new RectangleF(x0 + x * s, y0 + y * s, w * s, h * s);

            SmoothingMode mode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(color, Math.Max(1.2f, 1.35f * s)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var brush = new SolidBrush(color))
            {
                switch (glyph)
                {
                    case Glyph.CornerTopLeft:
                    case Glyph.CornerTopRight:
                    case Glyph.CornerBottomLeft:
                    case Glyph.CornerBottomRight:
                        // A screen with the indicator in one of its corners.
                        using (GraphicsPath screen = Theme.RoundRect(R(1.5f, 3f, 13f, 10f), 1.6f * s)) g.DrawPath(pen, screen);
                        bool right = glyph == Glyph.CornerTopRight || glyph == Glyph.CornerBottomRight;
                        bool bottom = glyph == Glyph.CornerBottomLeft || glyph == Glyph.CornerBottomRight;
                        using (GraphicsPath pill = Theme.RoundRect(R(right ? 8.5f : 3.5f, bottom ? 9f : 5f, 4f, 2f), 1f * s)) g.FillPath(brush, pill);
                        break;

                    case Glyph.Click:
                    case Glyph.DoubleClick:
                        Mouse(g, pen, brush, R(4f, 2f, 8f, 12.5f), s);
                        if (glyph == Glyph.DoubleClick)
                        {
                            g.DrawLine(pen, P(13.2f, 3.2f), P(14.8f, 1.6f));
                            g.DrawLine(pen, P(13.6f, 6f), P(15.4f, 6f));
                        }
                        break;

                    case Glyph.Target:
                        g.DrawEllipse(pen, R(3f, 3f, 10f, 10f));
                        g.FillEllipse(brush, R(6.6f, 6.6f, 2.8f, 2.8f));
                        g.DrawLine(pen, P(8f, 0.8f), P(8f, 2.6f));
                        g.DrawLine(pen, P(8f, 13.4f), P(8f, 15.2f));
                        g.DrawLine(pen, P(0.8f, 8f), P(2.6f, 8f));
                        g.DrawLine(pen, P(13.4f, 8f), P(15.2f, 8f));
                        break;

                    case Glyph.Drag:
                        g.DrawLine(pen, P(8f, 1.5f), P(8f, 14.5f));
                        g.DrawLine(pen, P(1.5f, 8f), P(14.5f, 8f));
                        g.DrawLines(pen, new[] { P(6f, 3.5f), P(8f, 1.5f), P(10f, 3.5f) });
                        g.DrawLines(pen, new[] { P(6f, 12.5f), P(8f, 14.5f), P(10f, 12.5f) });
                        g.DrawLines(pen, new[] { P(3.5f, 6f), P(1.5f, 8f), P(3.5f, 10f) });
                        g.DrawLines(pen, new[] { P(12.5f, 6f), P(14.5f, 8f), P(12.5f, 10f) });
                        break;

                    case Glyph.Hover:
                        // The mouse pointer: a move without a click.
                        g.DrawPolygon(pen, new[]
                        {
                            P(4f, 1.8f), P(4f, 12.8f), P(6.9f, 10.3f), P(9f, 14.4f), P(10.9f, 13.5f), P(8.8f, 9.5f), P(12.4f, 9.3f)
                        });
                        break;

                    case Glyph.Scroll:
                        Mouse(g, pen, null, R(4.5f, 2f, 7f, 12.5f), s);
                        g.DrawLine(pen, P(8f, 4.6f), P(8f, 7.4f));
                        g.DrawLines(pen, new[] { P(6.8f, 5.6f), P(8f, 4.4f), P(9.2f, 5.6f) });
                        g.DrawLines(pen, new[] { P(6.8f, 6.4f), P(8f, 7.6f), P(9.2f, 6.4f) });
                        break;

                    case Glyph.Type:
                        // A text caret (I-beam) after a line of text.
                        g.DrawLine(pen, P(10f, 2.5f), P(10f, 13.5f));
                        g.DrawLine(pen, P(8.2f, 2.5f), P(11.8f, 2.5f));
                        g.DrawLine(pen, P(8.2f, 13.5f), P(11.8f, 13.5f));
                        g.DrawLine(pen, P(1.5f, 8f), P(6.5f, 8f));
                        g.DrawLine(pen, P(1.5f, 11f), P(5f, 11f));
                        break;

                    case Glyph.Key:
                        using (GraphicsPath cap = Theme.RoundRect(R(1.5f, 2.5f, 13f, 11.5f), 2.2f * s)) g.DrawPath(pen, cap);
                        using (GraphicsPath face = Theme.RoundRect(R(4f, 4.3f, 8f, 6f), 1.4f * s)) g.DrawPath(pen, face);
                        break;

                    case Glyph.Wait:
                        g.DrawEllipse(pen, R(2.5f, 3.5f, 11f, 11f));
                        g.DrawLine(pen, P(8f, 9f), P(8f, 6f));
                        g.DrawLine(pen, P(8f, 9f), P(10.4f, 10.4f));
                        g.DrawLine(pen, P(6.5f, 1.3f), P(9.5f, 1.3f));
                        break;

                    case Glyph.WaitFor:
                        // An hourglass: wait until something happens.
                        g.DrawLine(pen, P(3.5f, 1.8f), P(12.5f, 1.8f));
                        g.DrawLine(pen, P(3.5f, 14.2f), P(12.5f, 14.2f));
                        g.DrawLines(pen, new[] { P(4.8f, 1.8f), P(4.8f, 4f), P(8f, 8f), P(4.8f, 12f), P(4.8f, 14.2f) });
                        g.DrawLines(pen, new[] { P(11.2f, 1.8f), P(11.2f, 4f), P(8f, 8f), P(11.2f, 12f), P(11.2f, 14.2f) });
                        g.FillPolygon(brush, new[] { P(6.2f, 13f), P(9.8f, 13f), P(8f, 10.8f) });
                        break;

                    case Glyph.Lock:
                        using (GraphicsPath body = Theme.RoundRect(R(3f, 7f, 10f, 7.5f), 1.6f * s)) g.DrawPath(pen, body);
                        g.DrawArc(pen, R(5.2f, 2f, 5.6f, 7f), 180, 180);
                        g.DrawLine(pen, P(5.2f, 5.5f), P(5.2f, 7f));
                        g.DrawLine(pen, P(10.8f, 5.5f), P(10.8f, 7f));
                        g.FillEllipse(brush, R(7.1f, 9.6f, 1.8f, 1.8f));
                        break;

                    case Glyph.Unknown:
                        g.DrawEllipse(pen, R(1.8f, 1.8f, 12.4f, 12.4f));
                        g.DrawArc(pen, R(5.6f, 4.2f, 4.8f, 4.4f), 180, 230);
                        g.DrawLine(pen, P(8f, 8.6f), P(8f, 9.6f));
                        g.FillEllipse(brush, R(7.2f, 11f, 1.6f, 1.6f));
                        break;
                }
            }
            g.SmoothingMode = mode;
        }

        /// <summary>A mouse seen from above, the left button filled when <paramref name="fill"/> is given.</summary>
        private static void Mouse(Graphics g, Pen pen, Brush fill, RectangleF body, float s)
        {
            float split = body.Top + body.Height * 0.38f;
            using (GraphicsPath shell = Theme.RoundRect(body, body.Width / 2))
            {
                if (fill != null)
                {
                    GraphicsState state = g.Save();
                    g.SetClip(shell, CombineMode.Intersect);
                    g.FillRectangle(fill, body.Left, body.Top, body.Width / 2, split - body.Top);
                    g.Restore(state);
                }
                g.DrawPath(pen, shell);
            }
            float middle = body.Left + body.Width / 2;
            g.DrawLine(pen, middle, body.Top + 0.6f * s, middle, split);
            g.DrawLine(pen, body.Left + 0.6f * s, split, body.Right - 0.6f * s, split);
        }
    }
}
