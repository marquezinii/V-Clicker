using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace VClicker
{
    /// <summary>Silkscreen lettering on the plate: a spaced capital label followed by a hairline. Groups settings without boxes.</summary>
    internal sealed class Section : Control
    {
        public Section()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
            Font = Theme.Legend(8f, bold: true);
            AccessibleRole = AccessibleRole.StaticText;
        }

        protected override Size DefaultSize => Theme.Sz(200, 28);

        public override Size GetPreferredSize(Size proposedSize) => new Size(Math.Max(Theme.Dp(40), proposedSize.Width), Theme.Dp(28));

        protected override void OnTextChanged(EventArgs e) { AccessibleName = Text; Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Theme.Backdrop(this));

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float k = Theme.Scale;
            // More room above the label than below it: the label belongs to what follows.
            var row = new Rectangle(0, Height - (int)(18 * k), Width, (int)(14 * k));
            int width = Theme.DrawTracked(g, (Text ?? string.Empty).ToUpper(CultureInfo.CurrentCulture), Font, 0, row, Theme.Dim, 1.4f * k);
            int x = width + (int)(10 * k);
            if (x >= Width) return;
            using (var pen = new Pen(Theme.Border, Math.Max(1f, k)))
                g.DrawLine(pen, x, row.Top + row.Height / 2, Width, row.Top + row.Height / 2);
        }
    }

    /// <summary>What a <see cref="WellPanel"/> looks like.</summary>
    internal enum WellLook
    {
        /// <summary>A recess in the plate (a list). Children use <see cref="Theme.Field"/>.</summary>
        Recess,
        /// <summary>A display module set into the plate (status and messages). Children use <see cref="Theme.Glass"/>.</summary>
        Glass,
        /// <summary>A hairline outline on the plate (a pane that scrolls). Children use <see cref="Theme.Bg"/>.</summary>
        Frame
    }

    /// <summary>A bounded region of the plate: a recess, a display or an outlined pane (see <see cref="WellLook"/>).</summary>
    internal sealed class WellPanel : Panel
    {
        private WellLook look;

        public WellPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Field;
            Padding = Theme.Pad(12);
        }

        public WellLook Look
        {
            get => look;
            set
            {
                look = value;
                BackColor = value == WellLook.Glass ? Theme.Glass : value == WellLook.Frame ? Theme.Bg : Theme.Field;
                Invalidate();
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Theme.Backdrop(this));

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Smooth(e.Graphics);
            var bounds = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            if (look == WellLook.Glass) Theme.DrawGlass(e.Graphics, bounds);
            else if (look == WellLook.Recess) Theme.DrawWell(e.Graphics, bounds, 8 * Theme.Scale);
            else
                using (GraphicsPath path = Theme.RoundRect(bounds, 8 * Theme.Scale))
                using (var pen = new Pen(Theme.Border, 1f))
                    e.Graphics.DrawPath(pen, path);
        }
    }

    internal enum ButtonKind { Primary, Danger, Secondary, Chip }

    /// <summary>
    /// A keycap push button. Primary is the amber key, Danger the coral key (stop, delete), Secondary a grey modifier key
    /// (an action), Chip a graphite alpha key (a small or repeated action). Optional: a secondary legend (the hotkey), an LED before the legend,
    /// and a latch that keeps the key down (a key that stays pressed while its action runs).
    /// </summary>
    internal sealed class KeyButton : Control
    {
        private bool hot, down, latched, ledLit;
        private ButtonKind kind = ButtonKind.Secondary;
        private string subLegend = string.Empty;
        private Color? led;

        public KeyButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Font = Theme.Legend(9.5f, bold: true);
            AccessibleRole = AccessibleRole.PushButton;
        }

        protected override Size DefaultSize => Theme.Sz(100, 34);

        public ButtonKind Kind
        {
            get => kind;
            set { kind = value; Invalidate(); }
        }

        /// <summary>Small legend in the lower right of the face, e.g. the hotkey.</summary>
        public string SubLegend
        {
            get => subLegend;
            set { subLegend = value ?? string.Empty; Invalidate(); }
        }

        /// <summary>Keeps the key drawn down (its action is running).</summary>
        public bool Latched
        {
            get => latched;
            set { if (latched == value) return; latched = value; Invalidate(); }
        }

        /// <summary>An LED before the legend (null = none), lit or dark.</summary>
        public Color? Led
        {
            get => led;
            set { led = value; Invalidate(); }
        }

        public bool LedLit
        {
            get => ledLit;
            set { if (ledLit == value) return; ledLit = value; Invalidate(); }
        }

        private KeyTone Tone => kind == ButtonKind.Primary ? KeyTone.Accent : kind == ButtonKind.Danger ? KeyTone.Stop
                              : kind == ButtonKind.Secondary ? KeyTone.Mod : KeyTone.Alpha;

        public void PerformClick()
        {
            if (Enabled) OnClick(EventArgs.Empty);
        }

        protected override void OnTextChanged(EventArgs e) { AccessibleName = Text; Invalidate(); base.OnTextChanged(e); }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Focus(); Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { down = false; Invalidate(); base.OnLostFocus(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { down = true; Invalidate(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (down && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
            {
                down = false;
                Invalidate();
                PerformClick();
                e.Handled = true;
            }
            base.OnKeyUp(e);
        }

        protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Theme.Backdrop(this));

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            float k = Theme.Scale;
            KeyTone tone = Tone;
            RectangleF face = Theme.DrawKey(g, new RectangleF(0, 0, Width, Height), tone, down || latched, hot, Enabled);
            if (Focused && ShowFocusCues) Theme.DrawFocus(g, new RectangleF(0.75f, 0.75f, Width - 1.5f, Height - 1.5f), 6 * k, tone);

            Color legend = Enabled ? Theme.LegendOn(tone) : Theme.LegendOn(tone) == Theme.Text ? Theme.Mix(Theme.Dim, Theme.Bg, 0.35f) : Theme.Mix(Theme.Ink, Theme.Bg, 0.2f);
            Font font = Font;

            // The secondary legend: under the main one on a tall key, at the right on a short one.
            RectangleF main = face;
            if (subLegend.Length > 0)
            {
                Font small = Theme.Legend(kind == ButtonKind.Primary || kind == ButtonKind.Danger ? 8.5f : 7.5f, bold: true);
                Size size = TextRenderer.MeasureText(g, subLegend, small, Size.Empty, TextFormatFlags.NoPadding);
                Color subColor = Enabled ? Theme.Mix(legend, Theme.FaceColor(tone), 0.35f) : legend;
                RectangleF sub;
                if (face.Height >= 30 * k)
                {
                    sub = new RectangleF(face.Left, face.Bottom - size.Height - 4 * k, face.Width, size.Height);
                    main = RectangleF.FromLTRB(face.Left, face.Top + 2 * k, face.Right, sub.Top);
                }
                else
                {
                    sub = new RectangleF(face.Right - size.Width - 8 * k, face.Top, size.Width + 2, face.Height);
                    main = RectangleF.FromLTRB(face.Left, face.Top, sub.Left - 2 * k, face.Bottom);
                }
                TextRenderer.DrawText(g, subLegend, small, Rectangle.Round(sub), subColor, Theme.Centered);
            }

            Size text = TextRenderer.MeasureText(g, Text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            float ledSize = 7 * k, ledGap = led.HasValue ? ledSize + 7 * k : 0;
            float width = Math.Min(main.Width - 8 * k, text.Width + ledGap);
            float left = main.Left + (main.Width - width) / 2;
            if (led.HasValue)
                Theme.DrawLed(g, new PointF(left + ledSize / 2, main.Top + main.Height / 2), ledSize, led.Value, ledLit && Enabled);
            var area = Rectangle.Round(new RectangleF(left + ledGap, main.Top, width - ledGap + 2, main.Height));
            TextRenderer.DrawText(g, Text, font, area, legend, Theme.Centered);
        }
    }

    /// <summary>
    /// Mutually exclusive options as a cluster of keys in one recess; the chosen key is the light one, held down.
    /// With <see cref="Glyphs"/> the keys show icons and <see cref="Items"/> only name them (for screen readers).
    /// </summary>
    internal sealed class Segmented : Control
    {
        private string[] items = new string[0];
        private Glyph[] glyphs;
        private int selected;
        private int hot = -1;

        public event EventHandler SelectedChanged;

        public Segmented()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Font = Theme.Legend(9.25f);
        }

        protected override Size DefaultSize => Theme.Sz(200, 34);

        protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Theme.Backdrop(this));

        public string[] Items
        {
            get => items;
            set { items = value ?? new string[0]; selected = Math.Max(0, Math.Min(selected, items.Length - 1)); Invalidate(); }
        }

        public Glyph[] Glyphs
        {
            get => glyphs;
            set { glyphs = value; Invalidate(); }
        }

        public int SelectedIndex
        {
            get => selected;
            set
            {
                int clamped = Math.Max(0, Math.Min(value, items.Length - 1));
                if (clamped == selected) return;
                selected = clamped;
                Invalidate();
                AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
                SelectedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Screen readers hear which segment is selected (its text is the value).</summary>
        protected override AccessibleObject CreateAccessibilityInstance() => new SegmentedAccessibleObject(this);

        private sealed class SegmentedAccessibleObject : ControlAccessibleObject
        {
            private readonly Segmented owner;

            public SegmentedAccessibleObject(Segmented owner) : base(owner)
            {
                this.owner = owner;
            }

            public override AccessibleRole Role => AccessibleRole.ComboBox;

            public override string Value
            {
                get => owner.items.Length == 0 ? string.Empty : owner.items[owner.selected];
                set { }
            }
        }

        /// <summary>Sets the index without raising SelectedChanged (used when loading settings).</summary>
        public void SetSelectedSilently(int value)
        {
            selected = Math.Max(0, Math.Min(value, items.Length - 1));
            Invalidate();
        }

        /// <summary>Key boundaries; each key is as wide as its legend needs, the spare space is shared out.</summary>
        private float[] Edges()
        {
            float k = Theme.Scale;
            float inset = 3 * k;
            var edges = new float[items.Length + 1];
            var weights = new float[items.Length];
            float sum = 0;
            for (int i = 0; i < items.Length; i++)
            {
                weights[i] = glyphs != null ? 1 : TextRenderer.MeasureText(items[i], Theme.Legend(9.25f, bold: true), Size.Empty, TextFormatFlags.NoPadding).Width + 24 * k;
                sum += weights[i];
            }
            float scale = sum <= 0 ? 0 : (Width - 2 * inset) / sum;
            edges[0] = inset;
            for (int i = 0; i < items.Length; i++) edges[i + 1] = edges[i] + weights[i] * scale;
            return edges;
        }

        private int HitTest(Point point)
        {
            if (items.Length == 0) return -1;
            float[] edges = Edges();
            for (int i = 0; i < items.Length; i++)
                if (point.X < edges[i + 1]) return i;
            return items.Length - 1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int at = HitTest(e.Location);
            if (at != hot) { hot = at; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hot = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button == MouseButtons.Left) SelectedIndex = HitTest(e.Location);
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up || keyData == Keys.Down ||
            base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Up) SelectedIndex = selected - 1;
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Down) SelectedIndex = selected + 1;
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            float k = Theme.Scale;

            var well = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Theme.DrawWell(g, well, 7 * k, Focused && ShowFocusCues ? Theme.Accent : (Color?)null);
            if (items.Length == 0) return;

            float inset = 3 * k;
            float[] edges = Edges();
            for (int i = 0; i < items.Length; i++)
            {
                bool isSelected = i == selected;
                KeyTone tone = isSelected ? KeyTone.Light : KeyTone.Alpha;
                var bounds = new RectangleF(edges[i] + k, inset, edges[i + 1] - edges[i] - 2 * k, Height - 2 * inset + k);
                RectangleF face = Theme.DrawKey(g, bounds, tone, isSelected, i == hot && !isSelected, Enabled);
                Color legend = !Enabled ? Theme.Mix(isSelected ? Theme.Ink : Theme.Dim, Theme.Bg, 0.3f)
                             : isSelected ? Theme.Ink : i == hot ? Theme.Text : Theme.Dim;
                if (glyphs != null && i < glyphs.Length)
                {
                    float side = Math.Min(face.Height, 15 * k);
                    Icons.Draw(g, glyphs[i], new RectangleF(face.Left + (face.Width - side) / 2, face.Top + (face.Height - side) / 2, side, side), legend);
                }
                else
                {
                    TextRenderer.DrawText(g, items[i], Theme.Legend(9.25f, bold: isSelected), Rectangle.Round(face), legend, Theme.Centered);
                }
            }
        }
    }

    /// <summary>Numeric input in a recess: typing, arrow keys and the mouse wheel; values are clamped on commit.</summary>
    internal sealed class NumberField : Control
    {
        private readonly TextBox box = new TextBox();
        private double minimum, maximum = int.MaxValue, number;
        private int decimals;
        private string unit = string.Empty;
        private string shownText; // text standing in for the number (see ShowText) until the user edits it
        private bool editing;

        public event EventHandler ValueChanged;

        public NumberField()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Theme.Legend(10.5f);
            box.BorderStyle = BorderStyle.None;
            box.BackColor = Theme.Field;
            box.ForeColor = Theme.Text;
            box.Font = Font;
            box.TextAlign = HorizontalAlignment.Center;
            box.MaxLength = 14;
            box.Text = "0";
            box.Enter += delegate { editing = true; Invalidate(); BeginInvoke(new Action(box.SelectAll)); };
            box.Leave += delegate { Commit(); editing = false; Invalidate(); };
            box.KeyDown += OnBoxKeyDown;
            box.KeyPress += OnBoxKeyPress;
            box.MouseWheel += delegate (object s, MouseEventArgs e)
            {
                if (e is HandledMouseEventArgs handled) handled.Handled = true;
                Step(e.Delta > 0 ? 1 : -1, (ModifierKeys & Keys.Shift) != 0 ? 10 : 1);
            };
            Controls.Add(box);
        }

        protected override Size DefaultSize => Theme.Sz(90, 34);

        protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Theme.Backdrop(this));

        public double Minimum { get => minimum; set => minimum = value; }
        public double Maximum { get => maximum; set => maximum = value; }

        /// <summary>Digits allowed after the decimal separator (0 = whole numbers only).</summary>
        public int Decimals
        {
            get => decimals;
            set { decimals = Math.Max(0, Math.Min(6, value)); Number = number; }
        }

        public string Unit
        {
            get => unit;
            set { unit = value ?? string.Empty; LayoutBox(); Invalidate(); }
        }

        public string FieldName
        {
            get => box.AccessibleName;
            set { box.AccessibleName = value; AccessibleName = value; }
        }

        public int Value
        {
            get => (int)Math.Round(number);
            set => Number = value;
        }

        /// <summary>The value, rounded to <see cref="Decimals"/> and clamped to the range.</summary>
        public double Number
        {
            get => number;
            set
            {
                number = Clamp(value);
                shownText = null;
                box.Text = Format(number);
                Invalidate();
            }
        }

        /// <summary>Displays <paramref name="text"/> instead of the number (e.g. "&lt; 0.1") without changing the value.</summary>
        public void ShowText(string text)
        {
            shownText = text;
            box.Text = text;
            Invalidate();
        }

        /// <summary>Applies whatever is typed (clamped); reverts the text if it is not a number.</summary>
        public void Commit()
        {
            if (shownText != null && box.Text == shownText) return;
            if (shownText != null && !TryParse(box.Text, out _))
            {
                // The stored number is stale behind a stand-in text: never apply it, put the stand-in back.
                box.Text = shownText;
                return;
            }
            double clamped = TryParse(box.Text, out double parsed) ? Clamp(parsed) : number;
            // While a stand-in text was shown the stored number is stale: anything typed is a change.
            bool changed = clamped != number || shownText != null;
            number = clamped;
            shownText = null;
            box.Text = Format(number);
            if (changed) ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        private double Clamp(double value) =>
            double.IsNaN(value) ? minimum : Math.Round(Math.Max(minimum, Math.Min(maximum, value)), decimals);

        // Integers keep the invariant form they always had; fractions use the user's decimal separator.
        private string Format(double value) => decimals == 0
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0." + new string('#', decimals), CultureInfo.CurrentCulture);

        /// <summary>Accepts both "," and "." as the decimal separator.</summary>
        private bool TryParse(string text, out double value)
        {
            text = (text ?? string.Empty).Trim();
            if (decimals > 0) text = text.Replace(',', '.');
            NumberStyles style = NumberStyles.AllowLeadingSign | (decimals > 0 ? NumberStyles.AllowDecimalPoint : 0);
            return double.TryParse(text, style, CultureInfo.InvariantCulture, out value);
        }

        private void Step(int direction, int amount)
        {
            bool standIn = shownText != null;
            Commit();
            if (standIn) number = minimum; // stepping from a stand-in text starts at the bottom of the range
            double before = number;
            Number = number + (double)direction * amount;
            // From a stand-in the shown value always differs from what was applied, even if the number did not move.
            if (standIn || number != before) ValueChanged?.Invoke(this, EventArgs.Empty);
            box.SelectAll();
        }

        private void OnBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                Commit();
                box.SelectAll();
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                Step(e.KeyCode == Keys.Up ? 1 : -1, e.Shift ? 10 : 1);
                e.SuppressKeyPress = true;
            }
        }

        private void OnBoxKeyPress(object sender, KeyPressEventArgs e)
        {
            bool allowed = char.IsControl(e.KeyChar) || (e.KeyChar >= '0' && e.KeyChar <= '9') ||
                           (e.KeyChar == '-' && minimum < 0) || (decimals > 0 && (e.KeyChar == ',' || e.KeyChar == '.'));
            if (!allowed) e.Handled = true;
        }

        private int UnitWidth() => unit.Length == 0 ? 0 : TextRenderer.MeasureText(unit, Theme.Legend(8.5f, bold: true)).Width;

        private void LayoutBox()
        {
            int pad = (int)(10 * Theme.Scale);
            int unitWidth = unit.Length == 0 ? 0 : UnitWidth() + pad / 2;
            box.Width = Math.Max(8, Width - 2 * pad - unitWidth);
            box.Location = new Point(pad, Math.Max(0, (Height - box.Height) / 2));
        }

        protected override void OnLayout(LayoutEventArgs e) { LayoutBox(); base.OnLayout(e); }
        protected override void OnResize(EventArgs e) { LayoutBox(); base.OnResize(e); }
        protected override void OnDpiChangedAfterParent(EventArgs e) { LayoutBox(); base.OnDpiChangedAfterParent(e); }
        protected override void OnMouseDown(MouseEventArgs e) { box.Focus(); base.OnMouseDown(e); }

        protected override void OnEnabledChanged(EventArgs e)
        {
            box.BackColor = Enabled ? Theme.Field : Theme.Mix(Theme.Field, Theme.Bg, 0.5f);
            box.ForeColor = Enabled ? Theme.Text : Theme.Dim;
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            float k = Theme.Scale;
            Theme.DrawWell(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 7 * k,
                editing && Enabled ? Theme.Accent : (Color?)null, Enabled ? Theme.Field : Theme.Mix(Theme.Field, Theme.Bg, 0.5f));
            if (unit.Length > 0)
            {
                int width = UnitWidth();
                var area = new Rectangle(Width - width - (int)(10 * k), 0, width + (int)(4 * k), Height);
                TextRenderer.DrawText(g, unit, Theme.Legend(8.5f, bold: true), area, Theme.Dim, Theme.Centered);
            }
        }
    }

    /// <summary>Free text in a recess (the same well as <see cref="NumberField"/>); <see cref="Box"/> is the editor.</summary>
    internal sealed class TextField : Control
    {
        private bool editing;

        public TextField()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Box = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Field, ForeColor = Theme.Text, Font = Theme.Ui(10f) };
            Box.Enter += delegate { editing = true; Invalidate(); };
            Box.Leave += delegate { editing = false; Invalidate(); };
            Controls.Add(Box);
        }

        public TextBox Box { get; }

        protected override Size DefaultSize => Theme.Sz(240, 34);

        protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Theme.Backdrop(this));

        protected override void OnLayout(LayoutEventArgs e)
        {
            int pad = (int)(10 * Theme.Scale);
            Box.Width = Math.Max(8, Width - 2 * pad);
            Box.Location = new Point(pad, Math.Max(0, (Height - Box.Height) / 2));
            base.OnLayout(e);
        }

        protected override void OnMouseDown(MouseEventArgs e) { Box.Focus(); base.OnMouseDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Smooth(e.Graphics);
            Theme.DrawWell(e.Graphics, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 7 * Theme.Scale,
                editing && !Box.ReadOnly ? Theme.Accent : (Color?)null);
        }
    }

    /// <summary>The hotkey as keycaps in a recess. Click it, press a key combination, done. Esc cancels.</summary>
    internal sealed class HotkeyBox : Control
    {
        private Hotkey value;
        private bool capturing, hot;

        public event EventHandler ValueChanged;

        /// <summary>
        /// Raised when the box starts or stops listening for a key. The owner must release the global
        /// hotkeys while it listens: a registered hotkey is swallowed by Windows before the box sees it.
        /// </summary>
        public event EventHandler CapturingChanged;

        public bool Capturing => capturing;

        /// <summary>Called with the captured combination; return false to reject it.</summary>
        public Func<Hotkey, bool> Accept { get; set; }

        public string PromptText { get; set; } = "…";

        public HotkeyBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Font = Theme.Legend(10f, bold: true);
        }

        protected override Size DefaultSize => Theme.Sz(150, 40);

        protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Theme.Backdrop(this));

        public Hotkey Value
        {
            get => value;
            set { this.value = value; Invalidate(); }
        }

        private void SetCapturing(bool on)
        {
            if (capturing == on) return;
            capturing = on;
            Invalidate();
            CapturingChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            SetCapturing(true);
            base.OnMouseDown(e);
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { SetCapturing(false); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { if (!Enabled) SetCapturing(false); Invalidate(); base.OnEnabledChanged(e); }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!Focused) return base.ProcessCmdKey(ref msg, keyData);

            if (!capturing)
            {
                if (keyData == Keys.Enter || keyData == Keys.Space)
                {
                    SetCapturing(true);
                    return true;
                }
                return base.ProcessCmdKey(ref msg, keyData);
            }

            if (keyData == Keys.Escape)
            {
                SetCapturing(false);
                return true;
            }

            Hotkey captured = Hotkey.FromKeyData(keyData);
            if (Hotkey.IsModifierKey(captured.Key)) return true; // still building the combination

            // Accept() runs while still capturing, i.e. while the owner has the global hotkeys released.
            if (Accept == null || Accept(captured))
            {
                value = captured;
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
            SetCapturing(false);
            return true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            float k = Theme.Scale;
            bool active = capturing && Enabled;

            Theme.DrawWell(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 7 * k,
                active || (Focused && ShowFocusCues) ? Theme.Accent : (Color?)null,
                Enabled ? Theme.Field : Theme.Mix(Theme.Field, Theme.Bg, 0.5f));

            if (active || value.IsEmpty)
            {
                TextRenderer.DrawText(g, PromptText, Theme.Legend(9f), new Rectangle(Point.Empty, Size), Theme.Accent, Theme.Centered);
                return;
            }

            // Each part of "Ctrl+F6" is a keycap, centered in the recess.
            string[] parts = value.ToString().Split('+');
            int gap = (int)(5 * k), padX = (int)(11 * k), capHeight = Height - (int)(8 * k);
            var widths = new int[parts.Length];
            int total = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                widths[i] = Math.Max(capHeight, TextRenderer.MeasureText(parts[i], Font).Width + 2 * padX);
                total += widths[i] + (i > 0 ? gap : 0);
            }
            if (total > Width - 2 * gap)
            {
                // Too long for keycaps (e.g. Ctrl+Alt+OemQuestion): fall back to plain text.
                TextRenderer.DrawText(g, value.ToString(), Font, new Rectangle(Point.Empty, Size),
                    Enabled ? Theme.Text : Theme.Dim, Theme.Centered);
                return;
            }
            int x = (Width - total) / 2, y = (int)(3 * k);
            for (int i = 0; i < parts.Length; i++)
            {
                RectangleF face = Theme.DrawKey(g, new RectangleF(x, y, widths[i], capHeight), KeyTone.Alpha, false, hot, Enabled);
                TextRenderer.DrawText(g, parts[i], Font, Rectangle.Round(face), Enabled ? Theme.Text : Theme.Dim, Theme.Centered);
                x += widths[i] + gap;
            }
        }
    }

    /// <summary>
    /// An on/off option as a lock key: a small keycap whose LED lights when the option is on (like Caps Lock), and its label.
    /// (A plain Control instead of CheckBox: the stock ButtonBase paint path left stale pixels behind custom drawing.)
    /// </summary>
    internal sealed class LedCheck : Control
    {
        private bool isChecked, hot;

        public event EventHandler CheckedChanged;

        public LedCheck()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            TabStop = true;
            AutoSize = true;
            Cursor = Cursors.Hand;
            Font = Theme.Legend(9.75f);
            ForeColor = Theme.Text;
            AccessibleRole = AccessibleRole.CheckButton;
        }

        private static int CapWidth => Theme.Dp(26);
        private static int CapHeight => Theme.Dp(24);

        public bool Checked
        {
            get => isChecked;
            set
            {
                if (isChecked == value) return;
                isChecked = value;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding);
            return new Size(text.Width + CapWidth + Theme.Dp(12), Math.Max(text.Height, CapHeight) + Theme.Dp(4));
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            AccessibleName = Text;
            Parent?.PerformLayout(this, "Text");
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            Focus();
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                Checked = !Checked;
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Theme.Backdrop(this));
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override AccessibleObject CreateAccessibilityInstance() => new CheckAccessibleObject(this);

        private sealed class CheckAccessibleObject : ControlAccessibleObject
        {
            private readonly LedCheck owner;

            public CheckAccessibleObject(LedCheck owner) : base(owner)
            {
                this.owner = owner;
            }

            public override AccessibleStates State =>
                base.State | (owner.Checked ? AccessibleStates.Checked : AccessibleStates.None);

            public override string DefaultAction => owner.Checked ? "Uncheck" : "Check";

            public override void DoDefaultAction() => owner.Checked = !owner.Checked;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            float k = Theme.Scale;
            var cap = new RectangleF(0, (Height - CapHeight) / 2f, CapWidth, CapHeight);
            RectangleF face = Theme.DrawKey(g, cap, isChecked ? KeyTone.Mod : KeyTone.Alpha, isChecked, hot, Enabled);
            Theme.DrawLed(g, new PointF(face.Left + face.Width / 2, face.Top + face.Height / 2), 7 * k, Theme.Accent, isChecked && Enabled);
            if (Focused && ShowFocusCues) Theme.DrawFocus(g, RectangleF.Inflate(cap, -0.25f * k, -0.25f * k), 6 * k);

            var textArea = new Rectangle(CapWidth + Theme.Dp(10), 0, Math.Max(1, Width - CapWidth - Theme.Dp(10)), Height);
            TextRenderer.DrawText(g, Text, Font, textArea, Enabled ? Theme.Text : Theme.Dim, Theme.LeftMiddle);
        }
    }

    /// <summary>A strip of display glass with a status LED and a short status text.</summary>
    internal sealed class LedBadge : Control
    {
        private Color led = Theme.Dim;

        public LedBadge()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Theme.Legend(9.5f, bold: true);
            AutoSize = true;
            AccessibleRole = AccessibleRole.StaticText;
        }

        public Color Dot
        {
            get => led;
            set { if (led == value) return; led = value; Invalidate(); }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            float k = Theme.Scale;
            return new Size(TextRenderer.MeasureText(Text, Font).Width + (int)(42 * k), (int)(30 * k));
        }

        protected override void OnTextChanged(EventArgs e) { AccessibleName = Text; Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Theme.Backdrop(this));

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            float k = Theme.Scale;
            using (GraphicsPath path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 6 * k))
            using (var fill = new SolidBrush(Theme.Glass))
                g.FillPath(fill, path);
            Theme.DrawLed(g, new PointF(15 * k, Height / 2f), 8 * k, led, led != Theme.Dim);
            var area = new Rectangle((int)(27 * k), 0, Width - (int)(36 * k), Height);
            TextRenderer.DrawText(g, Text, Font, area, Theme.Text, Theme.LeftMiddle);
        }
    }

    /// <summary>
    /// The OLED display of the main window: status LED and text, the Window Lock lamp, the requested speed large, three
    /// readouts and a message line. Everything is painted; screen readers get the same text as the value.
    /// </summary>
    internal sealed class StatusDisplay : Control
    {
        private Color led = Theme.Dim;
        private bool ledLit, lockLit;
        private string status = string.Empty, rate = string.Empty, rateCaption = string.Empty, lockCaption = "LOCK";
        private readonly string[] captions = { string.Empty, string.Empty, string.Empty };
        private readonly string[] values = { string.Empty, string.Empty, string.Empty };
        private string message = string.Empty;
        private Color messageColor = Theme.Dim;

        public StatusDisplay()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
            AccessibleRole = AccessibleRole.StatusBar;
        }

        protected override Size DefaultSize => Theme.Sz(372, 166);

        public string Status => status;

        public string Message => message;

        private void Set<T>(ref T field, T value)
        {
            if (Equals(field, value)) return;
            field = value;
            Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        }

        public void SetStatus(Color color, bool lit, string text)
        {
            Set(ref led, color);
            Set(ref ledLit, lit);
            Set(ref status, text ?? string.Empty);
        }

        public void SetRate(string value, string caption)
        {
            Set(ref rate, value ?? string.Empty);
            Set(ref rateCaption, caption ?? string.Empty);
        }

        public void SetLock(bool lit, string caption)
        {
            Set(ref lockLit, lit);
            Set(ref lockCaption, caption ?? string.Empty);
        }

        public void SetReadout(int index, string caption, string value)
        {
            Set(ref captions[index], caption ?? string.Empty);
            Set(ref values[index], value ?? string.Empty);
        }

        public void SetMessage(string text, Color color)
        {
            Set(ref message, text ?? string.Empty);
            Set(ref messageColor, color);
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new DisplayAccessibleObject(this);

        private sealed class DisplayAccessibleObject : ControlAccessibleObject
        {
            private readonly StatusDisplay owner;

            public DisplayAccessibleObject(StatusDisplay owner) : base(owner)
            {
                this.owner = owner;
            }

            public override string Value
            {
                get => owner.status + ". " + owner.rate + " CPS " + owner.rateCaption + ". " +
                       owner.captions[0] + " " + owner.values[0] + ", " + owner.captions[1] + " " + owner.values[1] + ", " +
                       owner.captions[2] + " " + owner.values[2] + ". " + owner.message;
                set { }
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Theme.Backdrop(this));

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            float k = Theme.Scale;
            int pad = (int)(18 * k);
            Theme.DrawGlass(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1));

            // Top line: the status, and the Window Lock lamp on the right.
            int top = pad;
            int lineHeight = (int)(18 * k);
            Theme.DrawLed(g, new PointF(pad + 4 * k, top + lineHeight / 2f), 8 * k, led, ledLit);
            Font caption = Theme.Legend(7.5f, bold: true);
            int lampX = Width - pad - (int)(6 * k);
            Theme.DrawLed(g, new PointF(lampX + 3 * k, top + lineHeight / 2f), 6 * k, Theme.Accent, lockLit);
            int lampWidth = TrackedWidth(g, lockCaption, caption, 1.2f * k);
            Theme.DrawTracked(g, lockCaption, caption, lampX - (int)(8 * k) - lampWidth, new Rectangle(0, top, Width, lineHeight),
                lockLit ? Theme.Text : Theme.Dim, 1.2f * k);
            var statusArea = new Rectangle(pad + (int)(16 * k), top, lampX - lampWidth - pad - (int)(30 * k), lineHeight);
            TextRenderer.DrawText(g, status, Theme.Legend(10f, bold: true), statusArea, Theme.Text, Theme.LeftMiddle);

            // The requested speed, large, with its unit and caption stacked beside it.
            int blockTop = top + lineHeight + (int)(10 * k);
            int blockHeight = (int)(60 * k);
            int readoutsWidth = (int)(150 * k);
            int readoutsLeft = Width - pad - readoutsWidth;
            Font big = Theme.Legend(30f, light: true);
            Size bigSize = TextRenderer.MeasureText(g, rate, big, Size.Empty, TextFormatFlags.NoPadding);
            int bigTop = blockTop + (blockHeight - bigSize.Height) / 2;
            TextRenderer.DrawText(g, rate, big, new Rectangle(pad - (int)(2 * k), bigTop, bigSize.Width + 4, bigSize.Height), Theme.Text,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            int unitX = pad + bigSize.Width + (int)(8 * k);
            int unitRight = readoutsLeft - (int)(10 * k);
            if (unitX < unitRight)
            {
                int bottom = bigTop + bigSize.Height - (int)(6 * k);
                var unitArea = new Rectangle(unitX, bottom - (int)(16 * k), unitRight - unitX, (int)(16 * k));
                TextRenderer.DrawText(g, "CPS", Theme.Legend(11f, bold: true), unitArea, Theme.Dim, Theme.LeftMiddle);
                Theme.DrawTracked(g, rateCaption, Theme.Legend(7f, bold: true), unitX, new Rectangle(unitX, unitArea.Top - (int)(14 * k), unitRight - unitX, (int)(12 * k)),
                    Theme.Dim, 1.1f * k);
            }

            // Three readouts: caption on the left, value on the right.
            int rowHeight = blockHeight / 3;
            for (int i = 0; i < 3; i++)
            {
                var row = new Rectangle(readoutsLeft, blockTop + i * rowHeight, readoutsWidth, rowHeight);
                Theme.DrawTracked(g, captions[i], caption, row.Left, row, Theme.Dim, 1.2f * k);
                TextRenderer.DrawText(g, values[i], Theme.Legend(11.5f, bold: true), row, Theme.Text,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            }

            // A hairline, then the message line (up to two lines).
            int rule = blockTop + blockHeight + (int)(10 * k);
            using (var pen = new Pen(Theme.Mix(Theme.Glass, Theme.Border, 0.9f), 1f))
                g.DrawLine(pen, pad, rule, Width - pad, rule);
            var messageArea = new Rectangle(pad, rule + (int)(8 * k), Width - 2 * pad, Height - rule - (int)(8 * k) - (int)(10 * k));
            TextRenderer.DrawText(g, message, Theme.Ui(9f), messageArea, messageColor,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        private static int TrackedWidth(Graphics g, string text, Font font, float tracking)
        {
            int width = 0;
            foreach (char c in text)
                width += TextRenderer.MeasureText(g, c.ToString(), font, Size.Empty, TextFormatFlags.NoPadding).Width + (int)Math.Round(tracking);
            return Math.Max(0, width - (int)Math.Round(tracking));
        }
    }

    /// <summary>
    /// A drop-down choice drawn as a recess with a chevron; the list opens as a dark menu under it. The subset of the
    /// ComboBox surface the Studio uses (Items, SelectedIndex, SelectedItem, SelectedIndexChanged), fully drawn: the native
    /// combo paints its own light frame and arrow.
    /// </summary>
    internal sealed class ChoiceBox : Control
    {
        private readonly ContextMenuStrip menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = false };
        private int selected = -1;
        private bool hot;

        public event EventHandler SelectedIndexChanged;

        public ChoiceBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Font = Theme.Ui(10f);
            AccessibleRole = AccessibleRole.ComboBox;
            menu.Renderer = new KeyMenuRenderer();
            menu.Font = Theme.Ui(10f);
            menu.Closed += delegate { Invalidate(); };
        }

        protected override Size DefaultSize => Theme.Sz(300, 36);

        public List<object> Items { get; } = new List<object>();

        public int SelectedIndex
        {
            get => selected;
            set
            {
                int clamped = Items.Count == 0 ? -1 : Math.Max(-1, Math.Min(value, Items.Count - 1));
                if (clamped == selected) return;
                selected = clamped;
                Invalidate();
                AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public object SelectedItem
        {
            get => selected >= 0 && selected < Items.Count ? Items[selected] : null;
            set => SelectedIndex = value == null ? -1 : Items.IndexOf(value);
        }

        public void BeginUpdate() { }

        public void EndUpdate()
        {
            if (selected >= Items.Count) selected = Items.Count - 1;
            Invalidate();
        }

        protected override AccessibleObject CreateAccessibilityInstance() => new ChoiceAccessibleObject(this);

        private sealed class ChoiceAccessibleObject : ControlAccessibleObject
        {
            private readonly ChoiceBox owner;

            public ChoiceAccessibleObject(ChoiceBox owner) : base(owner)
            {
                this.owner = owner;
            }

            public override string Value
            {
                get => owner.SelectedItem?.ToString() ?? string.Empty;
                set { }
            }
        }

        private void Open()
        {
            if (!Enabled || Items.Count == 0) return;
            menu.Items.Clear();
            for (int i = 0; i < Items.Count; i++)
            {
                int index = i;
                var item = new ToolStripMenuItem(Items[i].ToString()) { Checked = i == selected, AutoSize = false };
                item.Size = new Size(Width - Theme.Dp(4), Theme.Dp(32));
                item.Click += delegate { SelectedIndex = index; };
                menu.Items.Add(item);
            }
            menu.Show(this, new Point(0, Height + Theme.Dp(2)));
            if (selected >= 0) menu.Items[selected].Select();
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button == MouseButtons.Left) Open();
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F4 || (e.Alt && e.KeyCode == Keys.Down) || e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)
            {
                Open();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Up && selected > 0) SelectedIndex = selected - 1;
            else if (e.KeyCode == Keys.Down && selected < Items.Count - 1) SelectedIndex = selected + 1;
            base.OnKeyDown(e);
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Theme.Backdrop(this));

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            float k = Theme.Scale;
            bool active = menu.Visible || (Focused && ShowFocusCues);
            Color fill = !Enabled ? Theme.Mix(Theme.Field, Theme.Bg, 0.5f) : hot ? Theme.Mix(Theme.Field, Theme.Key, 0.35f) : Theme.Field;
            Theme.DrawWell(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 7 * k, active ? Theme.Accent : (Color?)null, fill);

            int chevron = (int)(30 * k);
            var text = new Rectangle((int)(12 * k), 0, Width - chevron - (int)(16 * k), Height);
            TextRenderer.DrawText(g, SelectedItem?.ToString() ?? string.Empty, Font, text, Enabled ? Theme.Text : Theme.Dim, Theme.LeftMiddle);
            float cx = Width - chevron / 2f, cy = Height / 2f;
            using (var pen = new Pen(Enabled ? Theme.Dim : Theme.Border, Math.Max(1.3f, 1.5f * k)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[] { new PointF(cx - 4 * k, cy - 2 * k), new PointF(cx, cy + 2 * k), new PointF(cx + 4 * k, cy - 2 * k) });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) menu.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Menus in the plate's colors: a recessed list, the hovered item on a key-colored bar, the current one with a lit LED.</summary>
    internal sealed class KeyMenuRenderer : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var fill = new SolidBrush(Theme.Field)) e.Graphics.FillRectangle(fill, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var pen = new Pen(Theme.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            Theme.Smooth(e.Graphics);
            float k = Theme.Scale;
            var bar = new RectangleF(2 * k, k, e.Item.Width - 4 * k, e.Item.Height - 2 * k);
            using (GraphicsPath path = Theme.RoundRect(bar, 4 * k))
            using (var fill = new SolidBrush(Theme.Key))
                e.Graphics.FillPath(fill, path);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            float k = Theme.Scale;
            var item = e.Item as ToolStripMenuItem;
            bool current = item != null && item.Checked;
            var area = new Rectangle((int)(26 * k), 0, e.Item.Width - (int)(34 * k), e.Item.Height);
            if (current) Theme.DrawLed(e.Graphics, new PointF(13 * k, e.Item.Height / 2f), 6 * k, Theme.Accent, true);
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, area, e.Item.Enabled ? Theme.Text : Theme.Dim, Theme.LeftMiddle);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var pen = new Pen(Theme.Border)) e.Graphics.DrawLine(pen, Theme.Dp(8), y, e.Item.Width - Theme.Dp(8), y);
        }
    }

    /// <summary>Draws the app icon at the exact pixel size with high-quality scaling.</summary>
    internal sealed class LogoBox : Control
    {
        private readonly Image image;

        public LogoBox(Image image)
        {
            this.image = image;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            TabStop = false;
        }

        protected override void OnPaintBackground(PaintEventArgs e) =>
            e.Graphics.Clear(Theme.Backdrop(this));

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.DrawImage(image, new Rectangle(0, 0, Width, Height));
        }
    }
}
