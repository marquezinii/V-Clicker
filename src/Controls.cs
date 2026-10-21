using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace VClicker
{
    /// <summary>Rounded dark panel that groups related settings.</summary>
    internal sealed class Card : Panel
    {
        public Card()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface;
            Padding = Theme.Pad(16, 14, 16, 16);
        }

        protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Theme.Bg);

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Smooth(e.Graphics);
            using (GraphicsPath path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 10 * Theme.Dpi(this)))
            using (var fill = new SolidBrush(Theme.Surface))
            using (var border = new Pen(Theme.Border))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            }
        }
    }

    internal enum ButtonKind { Primary, Danger, Secondary, Chip }

    /// <summary>
    /// Rounded push button drawn from scratch (a plain Control: the stock Button paint path leaves
    /// unpainted corners on rounded shapes).
    /// </summary>
    internal sealed class FlatButton : Control
    {
        private bool hot, down;
        private ButtonKind kind = ButtonKind.Secondary;

        public FlatButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Font = Theme.Ui(9.5f, true);
            AccessibleRole = AccessibleRole.PushButton;
        }

        protected override Size DefaultSize => Theme.Sz(100, 30);

        public ButtonKind Kind
        {
            get => kind;
            set { kind = value; Invalidate(); }
        }

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
            float k = Theme.Dpi(this);

            Color fill, text = Color.White, border = Color.Transparent;
            switch (kind)
            {
                case ButtonKind.Primary:
                    fill = down ? Theme.AccentPressed : hot ? Theme.AccentHover : Theme.Accent;
                    break;
                case ButtonKind.Danger:
                    fill = down ? Theme.Mix(Theme.Danger, Color.Black, 0.25f)
                         : hot ? Theme.Mix(Theme.Danger, Color.White, 0.15f) : Theme.Danger;
                    break;
                default:
                    fill = down ? Theme.Border : hot ? Theme.Mix(Theme.Field, Theme.Border, 0.6f) : Theme.Field;
                    text = Theme.Text;
                    border = Theme.Border;
                    break;
            }
            if (!Enabled)
            {
                fill = Theme.Mix(fill, Theme.Surface, 0.55f);
                text = Theme.Dim;
            }

            var bounds = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            float radius = (kind == ButtonKind.Chip ? 7 : 9) * k;
            using (GraphicsPath path = Theme.RoundRect(bounds, radius))
            using (var brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
                if (border != Color.Transparent)
                    using (var pen = new Pen(border)) g.DrawPath(pen, path);
                if (Focused && ShowFocusCues)
                {
                    var ring = RectangleF.Inflate(bounds, -2 * k, -2 * k);
                    using (GraphicsPath inner = Theme.RoundRect(ring, radius - 2 * k))
                    using (var pen = new Pen(Color.FromArgb(180, Color.White), Math.Max(1f, k)))
                        g.DrawPath(pen, inner);
                }
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, text, Theme.Centered);
        }
    }

    /// <summary>Row of mutually exclusive options drawn as one pill.</summary>
    internal sealed class Segmented : Control
    {
        private string[] items = new string[0];
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
            Font = Theme.Ui(9.25f);
        }

        protected override Size DefaultSize => Theme.Sz(200, 30);

        protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Theme.Backdrop(this));

        public string[] Items
        {
            get => items;
            set { items = value ?? new string[0]; selected = Math.Max(0, Math.Min(selected, items.Length - 1)); Invalidate(); }
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

        /// <summary>Segment boundaries; each segment is as wide as its text needs, the spare space is shared out.</summary>
        private float[] Edges()
        {
            float k = Theme.Dpi(this);
            float inset = 3 * k;
            var edges = new float[items.Length + 1];
            var weights = new float[items.Length];
            float sum = 0;
            for (int i = 0; i < items.Length; i++)
            {
                weights[i] = TextRenderer.MeasureText(items[i], Theme.Ui(9.25f, true), Size.Empty, TextFormatFlags.NoPadding).Width + 24 * k;
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

        protected override void OnMouseMove(MouseEventArgs e) { hot = HitTest(e.Location); Invalidate(); base.OnMouseMove(e); }
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
            float k = Theme.Dpi(this);

            var track = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using (GraphicsPath path = Theme.RoundRect(track, 8 * k))
            using (var fill = new SolidBrush(Theme.Field))
            using (var border = new Pen(Focused && ShowFocusCues ? Theme.Accent : Theme.Border))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }
            if (items.Length == 0) return;

            float inset = 3 * k;
            float[] edges = Edges();
            for (int i = 0; i < items.Length; i++)
            {
                var segment = new RectangleF(edges[i], inset, edges[i + 1] - edges[i], Height - 2 * inset);
                bool isSelected = i == selected;
                Color textColor = Theme.Dim;
                if (isSelected)
                {
                    using (GraphicsPath pill = Theme.RoundRect(segment, 6 * k))
                    using (var brush = new SolidBrush(Enabled ? Theme.Accent : Theme.Mix(Theme.Accent, Theme.Field, 0.6f)))
                        g.FillPath(brush, pill);
                    textColor = Color.White;
                }
                else if (i == hot && Enabled)
                {
                    using (GraphicsPath pill = Theme.RoundRect(segment, 6 * k))
                    using (var brush = new SolidBrush(Theme.Mix(Theme.Field, Theme.Border, 0.6f)))
                        g.FillPath(brush, pill);
                    textColor = Theme.Text;
                }
                if (!Enabled && !isSelected) textColor = Theme.Mix(Theme.Dim, Theme.Field, 0.5f);
                TextRenderer.DrawText(g, items[i], Theme.Ui(9.25f, isSelected), Rectangle.Round(segment), textColor, Theme.Centered);
            }
        }
    }

    /// <summary>Dark numeric input: typing, arrow keys and the mouse wheel; values are clamped on commit.</summary>
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
            Font = Theme.Ui(10f);
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

        protected override Size DefaultSize => Theme.Sz(90, 30);

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

        private void LayoutBox()
        {
            int pad = (int)(10 * Theme.Dpi(this));
            int unitWidth = unit.Length == 0 ? 0 : TextRenderer.MeasureText(unit, Theme.Ui(9f)).Width + pad / 2;
            box.Width = Math.Max(8, Width - 2 * pad - unitWidth);
            box.Location = new Point(pad, Math.Max(0, (Height - box.Height) / 2));
        }

        protected override void OnLayout(LayoutEventArgs e) { LayoutBox(); base.OnLayout(e); }
        protected override void OnResize(EventArgs e) { LayoutBox(); base.OnResize(e); }
        protected override void OnDpiChangedAfterParent(EventArgs e) { LayoutBox(); base.OnDpiChangedAfterParent(e); }
        protected override void OnMouseDown(MouseEventArgs e) { box.Focus(); base.OnMouseDown(e); }

        protected override void OnEnabledChanged(EventArgs e)
        {
            box.BackColor = Theme.Field;
            box.ForeColor = Enabled ? Theme.Text : Theme.Dim;
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            float k = Theme.Dpi(this);
            using (GraphicsPath path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 8 * k))
            using (var fill = new SolidBrush(Enabled ? Theme.Field : Theme.Mix(Theme.Field, Theme.Surface, 0.5f)))
            using (var border = new Pen(editing && Enabled ? Theme.Accent : Theme.Border))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }
            if (unit.Length > 0)
            {
                int width = TextRenderer.MeasureText(unit, Theme.Ui(9f)).Width;
                var area = new Rectangle(Width - width - (int)(10 * k), 0, width + (int)(4 * k), Height);
                TextRenderer.DrawText(g, unit, Theme.Ui(9f), area, Theme.Dim, Theme.Centered);
            }
        }
    }

    /// <summary>Click it, press a key combination, done. Esc cancels.</summary>
    internal sealed class HotkeyBox : Control
    {
        private Hotkey value;
        private bool capturing;

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
            Font = Theme.Ui(9.5f, true);
        }

        protected override Size DefaultSize => Theme.Sz(150, 30);

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
            float k = Theme.Dpi(this);
            bool active = capturing && Enabled;

            using (GraphicsPath path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 8 * k))
            using (var fill = new SolidBrush(Enabled ? Theme.Field : Theme.Mix(Theme.Field, Theme.Surface, 0.5f)))
            using (var border = new Pen(active || (Focused && ShowFocusCues) ? Theme.Accent : Theme.Border))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }

            if (active || value.IsEmpty)
            {
                TextRenderer.DrawText(g, PromptText, Theme.Ui(9.25f), new Rectangle(Point.Empty, Size),
                    Theme.Accent, Theme.Centered);
                return;
            }

            // Draw each part of "Ctrl+F6" as a little keycap, centered.
            string[] parts = value.ToString().Split('+');
            int gap = (int)(6 * k), padX = (int)(9 * k), capHeight = Height - (int)(10 * k);
            var widths = new int[parts.Length];
            int total = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                widths[i] = TextRenderer.MeasureText(parts[i], Font).Width + 2 * padX;
                total += widths[i] + (i > 0 ? gap : 0);
            }
            if (total > Width - 2 * gap)
            {
                // Too long for keycaps (e.g. Ctrl+Alt+OemQuestion): fall back to plain text.
                TextRenderer.DrawText(g, value.ToString(), Font, new Rectangle(Point.Empty, Size),
                    Enabled ? Theme.Text : Theme.Dim, Theme.Centered);
                return;
            }
            int x = (Width - total) / 2, y = (Height - capHeight) / 2;
            for (int i = 0; i < parts.Length; i++)
            {
                var cap = new RectangleF(x, y, widths[i], capHeight);
                using (GraphicsPath path = Theme.RoundRect(cap, 6 * k))
                using (var fill = new SolidBrush(Enabled ? Theme.Border : Theme.Mix(Theme.Border, Theme.Field, 0.5f)))
                    g.FillPath(fill, path);
                TextRenderer.DrawText(g, parts[i], Font, Rectangle.Round(cap),
                    Enabled ? Theme.Text : Theme.Dim, Theme.Centered);
                x += widths[i] + gap;
            }
        }
    }

    /// <summary>
    /// Check box drawn from scratch. (A plain Control instead of CheckBox: the stock ButtonBase paint path
    /// left stale pixels from neighbouring controls behind our custom drawing.)
    /// </summary>
    internal sealed class DarkCheck : Control
    {
        private bool isChecked;

        public event EventHandler CheckedChanged;

        public DarkCheck()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            TabStop = true;
            AutoSize = true;
            Cursor = Cursors.Hand;
            Font = Theme.Ui(9.5f);
            ForeColor = Theme.Text;
            AccessibleRole = AccessibleRole.CheckButton;
        }

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
            float k = Theme.Dpi(this);
            return new Size(text.Width + (int)(34 * k), Math.Max(text.Height, (int)(20 * k)) + (int)(6 * k));
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
        protected override void OnMouseEnter(EventArgs e) { Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Invalidate(); base.OnMouseLeave(e); }

        protected override AccessibleObject CreateAccessibilityInstance() => new CheckAccessibleObject(this);

        private sealed class CheckAccessibleObject : ControlAccessibleObject
        {
            private readonly DarkCheck owner;

            public CheckAccessibleObject(DarkCheck owner) : base(owner)
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
            float k = Theme.Dpi(this);
            float size = 18 * k;
            var box = new RectangleF(0.5f, (Height - size) / 2, size - 1, size - 1);
            bool hover = Enabled && ClientRectangle.Contains(PointToClient(Cursor.Position));
            bool focused = Focused && ShowFocusCues;

            using (GraphicsPath path = Theme.RoundRect(box, 5 * k))
            {
                using (var fill = new SolidBrush(isChecked ? Theme.Accent : Theme.Field))
                    g.FillPath(fill, path);
                Color edge = isChecked ? Theme.Accent : focused ? Theme.Accent : hover ? Theme.Dim : Theme.Border;
                using (var pen = new Pen(edge, focused ? 2 * k / 1.5f : 1))
                    g.DrawPath(pen, path);
            }
            if (isChecked)
            {
                using (var pen = new Pen(Color.White, 2f * k) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    g.DrawLines(pen, new[]
                    {
                        new PointF(box.Left + size * 0.26f, box.Top + size * 0.52f),
                        new PointF(box.Left + size * 0.43f, box.Top + size * 0.69f),
                        new PointF(box.Left + size * 0.75f, box.Top + size * 0.33f)
                    });
                }
            }
            var textArea = new Rectangle((int)(size + 10 * k), 0, Math.Max(1, Width - (int)(size + 10 * k)), Height);
            TextRenderer.DrawText(g, Text, Font, textArea, Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine |
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>Small pill with a colored dot and a short status text.</summary>
    internal sealed class Badge : Control
    {
        private Color dot = Theme.Dim;

        public Badge()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Theme.Ui(9.25f, true);
            AutoSize = true;
        }

        public Color Dot
        {
            get => dot;
            set { dot = value; Invalidate(); }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            float k = Theme.Dpi(this);
            return new Size(TextRenderer.MeasureText(Text, Font).Width + (int)(40 * k), (int)(28 * k));
        }

        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaintBackground(PaintEventArgs e) =>
            e.Graphics.Clear(Theme.Backdrop(this));

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            float k = Theme.Dpi(this);
            using (GraphicsPath path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Height / 2f))
            using (var fill = new SolidBrush(Theme.Field))
                g.FillPath(fill, path);
            float d = 8 * k;
            using (var brush = new SolidBrush(dot))
                g.FillEllipse(brush, 12 * k, (Height - d) / 2, d, d);
            var area = new Rectangle((int)(26 * k), 0, Width - (int)(34 * k), Height);
            TextRenderer.DrawText(g, Text, Font, area, Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix |
                TextFormatFlags.EndEllipsis);
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
