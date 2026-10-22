using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VClicker
{
    /// <summary>"Revisão de segurança": shown before the first run of a macro version with impactful actions.</summary>
    internal sealed class SafetyReview : Form
    {
        public ReviewChoice Answer = ReviewChoice.Cancel;

        public static ReviewChoice Ask(IWin32Window owner, List<ImpactfulAction> actions)
        {
            using (var form = new SafetyReview(actions))
            {
                form.ShowDialog(owner);
                return form.Answer;
            }
        }

        public SafetyReview(List<ImpactfulAction> actions)
        {
            Text = Loc.T("review.title");
            Dialogs.Style(this);
            var body = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill, BackColor = Theme.Bg };
            Label Line(string text, Color color, int bottom, bool semibold = false) => new Label
            {
                Text = text, AutoSize = true, ForeColor = color, BackColor = Theme.Bg, UseMnemonic = false, Font = Theme.Ui(9.5f, semibold),
                MaximumSize = new Size(Theme.Dp(460), 0), Margin = Theme.Pad(0, 0, 0, bottom)
            };
            body.Controls.Add(Line(Loc.T("review.intro"), Theme.Text, 14));
            foreach (ImpactfulAction action in actions)
            {
                string icon = action.Deletes ? "⚠ " : "ⓘ ";
                body.Controls.Add(Line(icon + Loc.F("review.step", action.Step + 1, MacroKeys.Display(action.Key)), action.Deletes ? Theme.Warning : Theme.Text, 2, true));
                body.Controls.Add(Line(action.Explain(), Theme.Dim, 10));
            }
            body.Controls.Add(Line(Loc.T("review.note"), Theme.Dim, 16));
            var row = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, BackColor = Theme.Bg };
            void Add(string key, ButtonKind kind, ReviewChoice choice)
            {
                string text = Loc.T(key);
                var button = new KeyButton
                {
                    Text = text, Kind = kind,
                    Size = new Size(Math.Max(Theme.Dp(100), TextRenderer.MeasureText(text, Theme.Ui(9.5f, true)).Width + Theme.Dp(28)), Theme.Dp(36)),
                    Margin = Theme.Pad(8, 0, 0, 0)
                };
                button.Click += delegate { Answer = choice; Close(); };
                row.Controls.Add(button);
            }
            Add("review.run", ButtonKind.Primary, ReviewChoice.Run);
            Add("ask.cancel", ButtonKind.Secondary, ReviewChoice.Cancel);
            Add("review.simulate", ButtonKind.Secondary, ReviewChoice.Simulate);
            body.Controls.Add(row);
            Controls.Add(body);
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        }
    }

    /// <summary>Close markers on the capture share one label ("2, 5").</summary>
    internal sealed class MarkerGroup
    {
        public PointF At;
        public readonly List<int> Steps = new List<int>();
        public string Label => string.Join(", ", Steps.OrderBy(s => s).Select(s => (s + 1).ToString(CultureInfo.CurrentCulture)));
    }

    /// <summary>Where a capture and its markers go inside the view.</summary>
    internal static class MarkerLayout
    {
        /// <summary>The capture scaled to fit the view (never enlarged), centred.</summary>
        public static RectangleF Fit(Size image, Rectangle view)
        {
            float scale = Math.Min(1f, Math.Min((float)view.Width / image.Width, (float)view.Height / image.Height));
            float w = image.Width * scale, h = image.Height * scale;
            return new RectangleF(view.X + (view.Width - w) / 2, view.Y + (view.Height - h) / 2, w, h);
        }

        public static PointF ToView(Point shot, Size image, Rectangle view)
        {
            RectangleF fit = Fit(image, view);
            float scale = fit.Width / image.Width;
            return new PointF(fit.X + shot.X * scale, fit.Y + shot.Y * scale);
        }

        /// <param name="points">Step index and its place in the view.</param>
        public static List<MarkerGroup> Merge(IEnumerable<KeyValuePair<int, PointF>> points, float px)
        {
            var groups = new List<MarkerGroup>();
            foreach (KeyValuePair<int, PointF> point in points)
            {
                MarkerGroup near = groups.FirstOrDefault(g => Math.Abs(g.At.X - point.Value.X) <= px && Math.Abs(g.At.Y - point.Value.Y) <= px);
                if (near == null) groups.Add(near = new MarkerGroup { At = point.Value });
                near.Steps.Add(point.Key);
            }
            return groups;
        }
    }

    /// <summary>
    /// SafeClick · Simulação (docs/superpowers/specs/2026-10-21-safeclick-design.md): a still of the app with each
    /// step's place marked, one step at a time. Nothing here sends input: it only shows what
    /// <see cref="MacroPreview.Evaluate"/> read.
    /// </summary>
    internal sealed class SimulationDialog : Form
    {
        private readonly Macro macro;
        private readonly Func<CancellationToken, MacroPreviewResult> evaluate;
        /// <summary>Cancelled when the dialog closes: a look still running must not hold the screen reads a run needs.</summary>
        private readonly CancellationTokenSource closing = new CancellationTokenSource();
        private readonly CaptureView view = new CaptureView();
        private readonly Label summary, taken, header, sentence;
        private readonly LedBadge badge = new LedBadge { Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        private readonly KeyButton previous, next, recapture;
        private MacroPreviewResult result;
        private IntPtr shown; // the window whose still is on view
        private int step;
        private bool closed;

        internal bool Loading { get; private set; }

        public SimulationDialog(Macro macro, Func<CancellationToken, MacroPreviewResult> evaluate, int startStep)
        {
            this.macro = macro;
            this.evaluate = evaluate;
            step = Math.Max(0, Math.Min(macro.Steps.Count - 1, startStep));
            Text = Loc.T("sim.title");
            Dialogs.Style(this);

            var body = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill, BackColor = Theme.Bg };
            Label Line(Color color, int bottom, float size = 9.5f, bool semibold = false) => new Label
            {
                AutoSize = true, ForeColor = color, BackColor = Theme.Bg, UseMnemonic = false, Font = Theme.Ui(size, semibold),
                MaximumSize = new Size(Theme.Dp(560), 0), Margin = Theme.Pad(0, 0, 0, bottom)
            };
            summary = Line(Theme.Text, 10);
            taken = Line(Theme.Dim, 4, 9f);
            view.Size = Theme.Sz(560, 300);
            view.Margin = Theme.Pad(0, 0, 0, 10);
            view.AccessibleName = Loc.T("sim.view");

            var headerRow = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill, BackColor = Theme.Bg, Margin = Theme.Pad(0, 0, 0, 6) };
            headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header = Line(Theme.Text, 0, 11f, true);
            header.Anchor = AnchorStyles.Left;
            headerRow.Controls.Add(header, 0, 0);
            headerRow.Controls.Add(badge, 1, 0);
            sentence = Line(Theme.Text, 4);
            sentence.MinimumSize = new Size(0, Theme.Dp(40));
            var note = Line(Theme.Dim, 12, 9f);
            note.Text = Loc.T("sim.note");

            KeyButton Key(string key, ButtonKind kind, int width, EventHandler click)
            {
                var button = new KeyButton { Text = Loc.T(key), Kind = kind, Size = Theme.Sz(width, 36), Margin = Theme.Pad(0, 0, 8, 0) };
                button.Click += click;
                return button;
            }
            previous = Key("sim.prev", ButtonKind.Secondary, 110, delegate { Previous(); });
            next = Key("sim.next", ButtonKind.Primary, 110, delegate { Next(); });
            recapture = Key("sim.recapture", ButtonKind.Secondary, 150, delegate { Recapture(); });
            KeyButton close = Key("sim.close", ButtonKind.Secondary, 100, delegate { Close(); });
            close.Margin = Padding.Empty;
            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Bg, Margin = Padding.Empty };
            buttons.Controls.AddRange(new Control[] { previous, next, recapture, close });

            body.Controls.Add(summary);
            body.Controls.Add(taken);
            body.Controls.Add(view);
            body.Controls.Add(headerRow);
            body.Controls.Add(sentence);
            body.Controls.Add(note);
            body.Controls.Add(buttons);
            Controls.Add(body);

            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) Close();
            };
            Shown += delegate { Recapture(); };
            FormClosed += delegate
            {
                closed = true;
                closing.Cancel();
                if (!Loading) result?.Dispose(); // else the capture in progress disposes it when it comes back
            };
            Render();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Left || keyData == Keys.Right)
            {
                if (keyData == Keys.Left) Previous();
                else Next();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        internal int Step
        {
            get => step;
            set
            {
                step = Math.Max(0, Math.Min(macro.Steps.Count - 1, value));
                Render();
            }
        }

        internal void Next() => Step = step + 1;

        internal void Previous() => Step = step - 1;

        /// <summary>A new still of the app, every step looked at again.</summary>
        internal async void Recapture()
        {
            if (Loading) return;
            Loading = true;
            Render();
            MacroPreviewResult fresh;
            try
            {
                CancellationToken cancel = closing.Token;
                fresh = await Task.Run(() => evaluate(cancel));
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                fresh = null; // shown as "could not check"; the run checks everything again anyway
            }
            Loading = false;
            if (closed || IsDisposed)
            {
                fresh?.Dispose();
                result?.Dispose();
                return;
            }
            result?.Dispose();
            result = fresh;
            shown = IntPtr.Zero;
            Render();
        }

        private void Render()
        {
            previous.Enabled = !Loading && step > 0;
            next.Enabled = !Loading && step < macro.Steps.Count - 1;
            recapture.Enabled = !Loading;
            header.Text = macro.Steps.Count == 0 ? string.Empty : Loc.F("sim.step", step + 1, macro.Steps.Count);
            if (Loading || result == null || step >= result.Steps.Count)
            {
                summary.Text = string.Empty;
                taken.Text = Loading ? Loc.T("sim.loading") : Loc.T("sim.failed");
                badge.Visible = false;
                sentence.Text = macro.Steps.Count == 0 ? string.Empty : macro.Steps[step].Describe();
                view.Display(null, null, null, Theme.Dim);
                return;
            }
            StepPreview current = result.Steps[step];
            summary.Text = Summary();
            if (current.Window != IntPtr.Zero) shown = current.Window;
            else if (shown == IntPtr.Zero) shown = result.Shots.Keys.FirstOrDefault();
            WindowShot shot = result.Shots.TryGetValue(shown, out WindowShot s) ? s : null;
            taken.Text = shot == null ? string.Empty
                       : shot.Usable ? Loc.F("sim.taken", shot.TakenAt.ToString("HH:mm:ss", CultureInfo.CurrentCulture))
                       : Loc.T("sim.shot." + shot.Problem);
            badge.Visible = true;
            badge.Text = Loc.T("sim.badge." + current.State);
            badge.Dot = StateColor(current.State);
            badge.Size = badge.GetPreferredSize(Size.Empty); // the badge paints itself: it does not resize with its text
            sentence.Text = Sentence(macro.Steps[step], current) + Impact();
            view.AccessibleDescription = sentence.Text;
            view.Display(shot?.Usable == true ? shot : null, current.Window == shown ? current : null,
                      result.Steps.Where(p => p != current && p.Window == shown && p.At.HasValue).ToList(), StateColor(current.State));
        }

        private static Color StateColor(PreviewState state)
        {
            switch (state)
            {
                case PreviewState.Verified:
                case PreviewState.InCapture: return Theme.Success;
                case PreviewState.Ambiguous:
                case PreviewState.Outside:
                case PreviewState.Unavailable:
                case PreviewState.NotFound: return Theme.Warning;
                default: return Theme.Dim;
            }
        }

        private string Summary()
        {
            var lines = new List<string>
            {
                result.WindowFound ? Loc.T("sim.sum.window") : Loc.T("sim.sum.noWindow"),
                Loc.F("sim.sum.located", result.Located, result.Positional)
            };
            if (result.Impactful.Count > 0)
                lines.Add(Loc.F("sim.sum.impact", string.Join(", ", result.Impactful.Select(a => Loc.F("sim.stepKey", a.Step + 1, MacroKeys.Display(a.Key))))));
            if (result.Unverifiable > 0) lines.Add(Loc.T("sim.sum.later"));
            lines.Add(macro.Repeat == 1 ? Loc.T("sim.sum.once") : macro.Repeat == 0 ? Loc.T("sim.sum.forever") : Loc.F("sim.sum.repeat", macro.Repeat));
            return string.Join("\n", lines);
        }

        private string Impact()
        {
            ImpactfulAction action = result.Impactful.FirstOrDefault(a => a.Step == step);
            return action == null ? string.Empty : "\n⚠ " + action.Explain();
        }

        /// <summary>What the step would do, in the words of the state its look found.</summary>
        internal static string Sentence(MacroStep step, StepPreview p)
        {
            switch (p.State)
            {
                case PreviewState.Unavailable: return Loc.T("sim.unavailable");
                case PreviewState.NotFound: return Loc.T("sim.notFound");
                case PreviewState.Ambiguous: return Loc.F("sim.ambiguous", p.Count);
                case PreviewState.Outside: return Loc.T("sim.outside");
                case PreviewState.Inconclusive: return Loc.T("sim.inconclusive");
                case PreviewState.NoApp: return Loc.T("sim.noApp");
                case PreviewState.NoPosition:
                    switch (step.Kind)
                    {
                        case StepKind.Type: return Loc.F("sim.type", step.Text);
                        case StepKind.Key:
                            return step.Count > 1 ? Loc.F("sim.keys", MacroKeys.Display(step.Key), step.Count) : Loc.F("sim.key", MacroKeys.Display(step.Key));
                        case StepKind.Shortcut: return Loc.F("sim.key", MacroKeys.Display(step.Key));
                        case StepKind.Wait: return Loc.F("sim.wait", MacroStep.Seconds(step.DurationMs));
                        case StepKind.Password: return Loc.T("sim.password");
                        default: return step.Describe();
                    }
            }
            if (step.Kind == StepKind.WaitFor)
            {
                if (step.Condition == ConditionKind.Window) return Loc.F("sim.windowOpen", step.Window?.Title ?? string.Empty);
                string seconds = MacroStep.Seconds(step.DurationMs);
                if (p.Met == true) return Loc.T(p.State == PreviewState.Verified ? "sim.metLive" : "sim.met");
                return Loc.F(p.State == PreviewState.Verified ? "sim.notMetLive" : "sim.notMet", seconds);
            }
            string verb = Loc.T("sim.do." + step.Kind);
            Point at = p.At ?? step.At;
            if (step.Target != null && (step.Kind == StepKind.Click || step.Kind == StepKind.DoubleClick))
                return step.Target.Kind == TargetKind.Element
                    ? Loc.F("sim.element", verb, step.Target.Name, MacroStep.Lower(Loc.ControlTypeLabel(step.Target.ControlType)), at.X, at.Y)
                    : Loc.F("sim.image", verb, at.X, at.Y);
            if (step.Kind == StepKind.Drag && p.To.HasValue) return Loc.F("sim.drag", at.X, at.Y, p.To.Value.X, p.To.Value.Y);
            return Loc.F("sim.point", verb, at.X, at.Y);
        }

        /// <summary>The still, the current step's marker, and the other steps of the same window as small dimmed numbers.</summary>
        private sealed class CaptureView : Control
        {
            private WindowShot shot;
            private StepPreview current;
            private List<StepPreview> others = new List<StepPreview>();
            private Color color = Theme.Accent;

            public CaptureView()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                AccessibleRole = AccessibleRole.Graphic;
                BackColor = Theme.Field;
            }

            public void Display(WindowShot still, StepPreview step, List<StepPreview> rest, Color state)
            {
                shot = still;
                current = step;
                others = rest ?? new List<StepPreview>();
                color = state == Theme.Success ? Theme.Accent : state;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(Theme.Field);
                if (shot == null) return;
                Size size = shot.Image.Size;
                RectangleF fit = MarkerLayout.Fit(size, ClientRectangle);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(shot.Image, fit);
                Theme.Smooth(g);
                float k = Theme.Scale;
                PointF View(Point p) => MarkerLayout.ToView(p, size, ClientRectangle);

                // The current step goes first, so a step at the same place shares its tag ("4, 5") instead of hiding under it.
                var points = others.Select(o => new KeyValuePair<int, PointF>(o.Index, View(o.At.Value))).ToList();
                if (current?.At != null) points.Insert(0, new KeyValuePair<int, PointF>(current.Index, View(current.At.Value)));
                List<MarkerGroup> groups = MarkerLayout.Merge(points, 8 * k);
                MarkerGroup mine = groups.FirstOrDefault(gr => current != null && gr.Steps.Contains(current.Index));
                foreach (MarkerGroup group in groups.Where(gr => gr != mine))
                    Pin(g, group.At, group.Label, Theme.Ui(7.5f, true), Color.FromArgb(170, Theme.Ink), Color.FromArgb(220, Theme.Text), 4 * k, 8 * k);

                if (current == null) return;
                if (current.Bounds.HasValue)
                {
                    Rectangle b = current.Bounds.Value;
                    PointF a = View(b.Location), z = View(new Point(b.Right, b.Bottom));
                    using (var pen = new Pen(color, 2 * k))
                        g.DrawRectangle(pen, a.X - 2 * k, a.Y - 2 * k, z.X - a.X + 4 * k, z.Y - a.Y + 4 * k);
                }
                if (!current.At.HasValue) return;
                PointF at = View(current.At.Value);
                if (current.To.HasValue)
                    using (var pen = new Pen(color, 2.5f * k) { CustomEndCap = new AdjustableArrowCap(4, 4) })
                        g.DrawLine(pen, at, View(current.To.Value));
                string label = mine?.Label ?? (current.Index + 1).ToString(CultureInfo.CurrentCulture);
                if (current.Kind == StepKind.DoubleClick) label += " ×2";
                Pin(g, at, label, Theme.Ui(9f, true), color, Theme.Ink, 9 * k, 11 * k);
            }

            /// <summary>
            /// A ring on the point (what is under it stays visible) and the step number on a tag above and to the right,
            /// joined by a short leader; near the right or top edge the tag flips to the other side.
            /// </summary>
            private void Pin(Graphics g, PointF at, string label, Font font, Color fill, Color text, float ring, float radius)
            {
                float k = Theme.Scale;
                using (var halo = new Pen(Theme.Ink, 4.5f * k))
                using (var pen = new Pen(fill, 2.5f * k))
                {
                    g.DrawEllipse(halo, at.X - ring, at.Y - ring, 2 * ring, 2 * ring);
                    g.DrawEllipse(pen, at.X - ring, at.Y - ring, 2 * ring, 2 * ring);
                }
                Size measured = TextRenderer.MeasureText(label, font);
                float w = Math.Max(2 * radius, measured.Width + radius), h = 2 * radius, gap = ring + 6 * k;
                float x = at.X + gap + w <= Width ? at.X + gap : at.X - gap - w;
                float y = at.Y - gap - h >= 0 ? at.Y - gap - h : at.Y + gap;
                var box = new RectangleF(x, y, w, h);
                using (var leader = new Pen(fill, 1.5f * k))
                    g.DrawLine(leader, at, new PointF(box.X + (x > at.X ? 0 : w), box.Y + (y < at.Y ? h : 0)));
                using (GraphicsPath path = Theme.RoundRect(box, radius))
                using (var brush = new SolidBrush(fill))
                using (var edge = new Pen(Theme.Ink, 1.5f * k))
                {
                    g.FillPath(brush, path);
                    g.DrawPath(edge, path);
                }
                TextRenderer.DrawText(g, label, font, Rectangle.Round(box), text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }
}
