using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace VClicker
{
    // The Macro Studio's own dialogs: what to watch after F7, how to find a click target, and the pause optimizer.
    internal sealed partial class MacroStudio
    {

        /// <summary>
        /// After F7 for a condition: what was found at the point, which kind to watch (the likeliest is preselected),
        /// and the time limit.
        /// </summary>
        internal sealed class ConditionDialog : Form
        {
            public MacroStep Result;

            public ConditionDialog(ConditionProbe probe, int timeoutMs)
            {
                Text = Loc.T("cond.title");
                Dialogs.Style(this);
                var kinds = new List<ConditionKind>();
                if (probe.ElementName.Length > 0 && probe.ElementCount == 1) kinds.Add(ConditionKind.Element);
                if (probe.Rgb.HasValue) kinds.Add(ConditionKind.Color);
                if (probe.Dialog != null) kinds.Add(ConditionKind.Window);
                ConditionKind preferred = kinds.Contains(ConditionKind.Element) ? ConditionKind.Element
                                        : kinds.Contains(ConditionKind.Window) ? ConditionKind.Window : ConditionKind.Color;

                var body = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, BackColor = Theme.Bg
                };
                Label Line(string text, Color color, int bottom = 8) => new Label
                {
                    Text = text, AutoSize = true, ForeColor = color, BackColor = Theme.Bg, UseMnemonic = false,
                    MaximumSize = new Size(Theme.Dp(440), 0), Margin = Theme.Pad(0, 0, 0, bottom)
                };
                var ok = new KeyButton { Text = Loc.T("cond.confirm"), Kind = ButtonKind.Primary, Size = Theme.Sz(120, 36) };
                var cancel = new KeyButton { Text = Loc.T("ask.cancel"), Kind = ButtonKind.Secondary, Size = Theme.Sz(110, 36), Margin = Theme.Pad(0, 0, 8, 0) };
                cancel.Click += delegate { DialogResult = DialogResult.Cancel; };

                if (kinds.Count == 0)
                {
                    body.Controls.Add(Line(Loc.T("cond.nothing"), Theme.Warning, 16));
                    ok.Enabled = false;
                }
                else
                {
                    body.Controls.Add(Line(Loc.T("cond.intro"), Theme.Dim));
                    var choice = new Segmented
                    {
                        Items = kinds.Select(k => Loc.T("cond.kind." + k)).ToArray(), Height = Theme.Dp(36), Dock = DockStyle.Fill, Margin = Theme.Pad(0, 0, 0, 10)
                    };
                    choice.SelectedIndex = kinds.IndexOf(preferred);
                    choice.Visible = kinds.Count > 1; // a single kind is already named by the line below
                    body.Controls.Add(choice);
                    var found = Line(string.Empty, Theme.Text, 4);
                    found.Font = Theme.Ui(10f, true);
                    var swatch = new Panel { Size = Theme.Sz(60, 20), Margin = Theme.Pad(0, 0, 0, 6), BorderStyle = BorderStyle.FixedSingle };
                    var note = Line(string.Empty, Theme.Dim, 12);
                    body.Controls.Add(found);
                    body.Controls.Add(swatch);
                    body.Controls.Add(note);
                    void Show()
                    {
                        ConditionKind kind = kinds[Math.Max(0, choice.SelectedIndex)];
                        swatch.Visible = kind == ConditionKind.Color;
                        switch (kind)
                        {
                            case ConditionKind.Element:
                                found.Text = Loc.F("cond.foundElement", TypeLabel(probe.ControlType), probe.ElementName);
                                note.Text = Loc.T("cond.noteElement");
                                break;
                            case ConditionKind.Color:
                                int rgb = probe.Rgb.Value;
                                found.Text = Loc.F("cond.foundColor", rgb.ToString("X6", CultureInfo.InvariantCulture), probe.At.X, probe.At.Y);
                                swatch.BackColor = Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
                                note.Text = Loc.T("studio.colorInfo");
                                break;
                            default:
                                found.Text = Loc.F("cond.foundWindow", probe.Dialog.Title);
                                note.Text = Loc.T("cond.noteWindow");
                                break;
                        }
                    }
                    choice.SelectedChanged += delegate { Show(); };
                    Show();
                    foreach (ConditionKind missing in new[] { ConditionKind.Element, ConditionKind.Color, ConditionKind.Window })
                        if (!kinds.Contains(missing)) body.Controls.Add(Line(MissingText(missing, probe), Theme.Dim, 4));

                    var limitRow = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Bg, Margin = Theme.Pad(0, 10, 0, 0), WrapContents = false };
                    limitRow.Controls.Add(new Label
                    {
                        Text = Loc.T("studio.timeout"), AutoSize = true, ForeColor = Theme.Dim, BackColor = Theme.Bg, Margin = Theme.Pad(0, 10, 10, 0)
                    });
                    var limit = new NumberField
                    {
                        Minimum = MacroStep.MinTimeoutMs / 1000, Maximum = MacroStep.MaxTimeoutMs / 1000, Size = Theme.Sz(90, 36), Unit = "s"
                    };
                    limit.Number = timeoutMs / 1000;
                    limitRow.Controls.Add(limit);
                    body.Controls.Add(limitRow);

                    ok.Click += delegate
                    {
                        limit.Commit();
                        ConditionKind kind = kinds[Math.Max(0, choice.SelectedIndex)];
                        Result = new MacroStep
                        {
                            Kind = StepKind.WaitFor, Condition = kind, DurationMs = limit.Value * 1000,
                            At = kind == ConditionKind.Window ? Point.Empty : probe.At,
                            Window = kind == ConditionKind.Window ? probe.Dialog?.Clone() : probe.Window?.Clone(),
                            ControlType = kind == ConditionKind.Element ? probe.ControlType : string.Empty,
                            Text = kind == ConditionKind.Element ? probe.ElementName : string.Empty,
                            AutomationId = kind == ConditionKind.Element ? probe.AutomationId : string.Empty,
                            Rgb = kind == ConditionKind.Color ? probe.Rgb.Value : 0
                        };
                        DialogResult = DialogResult.OK;
                    };
                }
                var buttons = new FlowLayoutPanel
                {
                    FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, BackColor = Theme.Bg, Margin = Theme.Pad(0, 16, 0, 0)
                };
                buttons.Controls.Add(ok);
                buttons.Controls.Add(cancel);
                body.Controls.Add(buttons);
                Controls.Add(body);
                KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) DialogResult = DialogResult.Cancel; };
            }

            /// <summary>"Botão" for "Button": the control types a person recognizes, in their language.</summary>
            internal static string TypeLabel(string type) => Loc.ControlTypeLabel(type);

            /// <summary>Why a kind of condition is not offered at this point.</summary>
            private static string MissingText(ConditionKind kind, ConditionProbe probe)
            {
                if (kind == ConditionKind.Element && probe.ElementName.Length > 0)
                    return probe.ElementCount > 1
                        ? Loc.F("cond.missing.ElementMany", probe.ElementCount, MacroStep.Lower(TypeLabel(probe.ControlType)), probe.ElementName)
                        : Loc.T("cond.missing.ElementUnconfirmed");
                return Loc.T("cond.missing." + kind);
            }
        }

        /// <summary>
        /// "Smart target": how the V-Clicker will recognize what to click, in plain words, with a preview. By name
        /// (UI Automation) when the point has a named element that is unique in the window; else by appearance (the
        /// smallest image region that is unique). The other way stays one click away; Confirm only for a unique one.
        /// </summary>
        internal sealed class TargetDialog : Form
        {
            public SmartTarget Result;

            public TargetDialog(TargetProbe probe, int timeoutMs)
            {
                Text = Loc.T("target.title");
                Dialogs.Style(this);
                var body = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, BackColor = Theme.Bg
                };
                Label Line(string text, Color color, int bottom = 8) => new Label
                {
                    Text = text, AutoSize = true, ForeColor = color, BackColor = Theme.Bg, UseMnemonic = false,
                    MaximumSize = new Size(Theme.Dp(440), 0), Margin = Theme.Pad(0, 0, 0, bottom)
                };
                var ok = new KeyButton { Text = Loc.T("cond.confirm"), Kind = ButtonKind.Primary, Size = Theme.Sz(120, 36) };
                var cancel = new KeyButton { Text = Loc.T("ask.cancel"), Kind = ButtonKind.Secondary, Size = Theme.Sz(110, 36), Margin = Theme.Pad(0, 0, 8, 0) };
                cancel.Click += delegate { DialogResult = DialogResult.Cancel; };

                bool byName = probe.ElementUsable;
                List<ImageOption> sizes = probe.Images.Where(o => o.Crop != null).ToList();
                ImageOption image = probe.FirstImage;
                bool byLook = image != null;
                var missing = new List<string>();
                if (!byName) missing.Add(NameMissing(probe));
                if (!byLook) missing.Add(Loc.T("target.missing.image"));

                if (!byName && !byLook)
                {
                    body.Controls.Add(Line(Loc.T("target.nothing"), Theme.Warning, 12));
                    foreach (string why in missing) body.Controls.Add(Line(why, Theme.Dim, 4));
                    ok.Enabled = false;
                }
                else
                {
                    var choice = new Segmented { Items = new[] { Loc.T("target.byName"), Loc.T("target.byLook") }, Height = Theme.Dp(36), Dock = DockStyle.Fill, Margin = Theme.Pad(0, 0, 0, 10) };
                    choice.SelectedIndex = byName ? 0 : 1;
                    var found = Line(string.Empty, Theme.Text, 6);
                    found.Font = Theme.Ui(10f, true);
                    var view = new PixelView { Height = Theme.Dp(110), Dock = DockStyle.Fill, Margin = Theme.Pad(0, 0, 0, 8), BackColor = Theme.Field };
                    var verdict = Line(string.Empty, Theme.Success, 6);
                    // Label, then the size keys out to the dialog's right edge (the same edge as the preview and the keys).
                    var sizeRow = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill, BackColor = Theme.Bg, Margin = Theme.Pad(0, 0, 0, 6) };
                    sizeRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                    sizeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                    sizeRow.Controls.Add(new Label
                    {
                        Text = Loc.T("target.size"), AutoSize = true, ForeColor = Theme.Dim, BackColor = Theme.Bg, Anchor = AnchorStyles.Left, Margin = Theme.Pad(0, 0, 10, 0)
                    });
                    var size = new Segmented
                    {
                        Items = sizes.Select(o => Loc.T("target.side" + o.Side)).ToArray(), Height = Theme.Dp(36), Dock = DockStyle.Fill, Margin = Padding.Empty
                    };
                    if (image != null) size.SelectedIndex = sizes.IndexOf(image);
                    sizeRow.Controls.Add(size);
                    var note = Line(string.Empty, Theme.Dim, 8);
                    body.Controls.Add(Line(Loc.T("target.intro"), Theme.Dim));
                    body.Controls.Add(found);
                    body.Controls.Add(view);
                    body.Controls.Add(verdict);
                    body.Controls.Add(sizeRow);
                    body.Controls.Add(note);
                    if (byName && byLook)
                    {
                        body.Controls.Add(Line(Loc.T("target.how"), Theme.Dim, 4));
                        body.Controls.Add(choice);
                    }

                    void Show()
                    {
                        bool element = byName && choice.SelectedIndex == 0;
                        sizeRow.Visible = !element;
                        if (element)
                        {
                            found.Text = Loc.F("target.foundElement", ConditionDialog.TypeLabel(probe.Element.ControlType), probe.Element.Name);
                            view.Image = probe.ElementPreview;
                            verdict.Text = Loc.T("target.unique");
                            verdict.ForeColor = Theme.Success;
                            note.Text = Loc.T("target.elementNote");
                            ok.Enabled = true;
                            return;
                        }
                        found.Text = Loc.T("target.foundImage");
                        view.Image = image?.Crop;
                        verdict.Text = image == null ? string.Empty
                            : image.Problem == ImageProblem.None ? Loc.T("target.unique") : Loc.F("target.image." + image.Problem, image.Count);
                        verdict.ForeColor = image?.Problem == ImageProblem.None ? Theme.Success : Theme.Warning;
                        note.Text = Loc.T("target.imageNote");
                        ok.Enabled = image?.Problem == ImageProblem.None;
                    }
                    choice.SelectedChanged += delegate { Show(); };
                    size.SelectedChanged += delegate
                    {
                        if (size.SelectedIndex >= 0 && size.SelectedIndex < sizes.Count) image = sizes[size.SelectedIndex];
                        Show();
                    };
                    Show();
                    foreach (string why in missing) body.Controls.Add(Line(why, Theme.Dim, 4));

                    var limitRow = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Bg, Margin = Theme.Pad(0, 10, 0, 0), WrapContents = false };
                    limitRow.Controls.Add(new Label
                    {
                        Text = Loc.T("studio.findTimeout"), AutoSize = true, ForeColor = Theme.Dim, BackColor = Theme.Bg, Margin = Theme.Pad(0, 10, 10, 0)
                    });
                    var limit = new NumberField
                    {
                        Minimum = MacroStep.MinTimeoutMs / 1000, Maximum = MacroStep.MaxTimeoutMs / 1000, Size = Theme.Sz(90, 36), Unit = "s"
                    };
                    limit.Number = timeoutMs / 1000;
                    limitRow.Controls.Add(limit);
                    body.Controls.Add(limitRow);

                    ok.Click += delegate
                    {
                        limit.Commit();
                        if (byName && choice.SelectedIndex == 0)
                            Result = new SmartTarget
                            {
                                Kind = TargetKind.Element, ControlType = probe.Element.ControlType, Name = probe.Element.Name,
                                AutomationId = probe.Element.AutomationId, TimeoutMs = limit.Value * 1000
                            };
                        else if (image != null && image.Problem == ImageProblem.None)
                            Result = new SmartTarget { Kind = TargetKind.Image, Png = SmartTarget.EncodeImage(image.Crop), TimeoutMs = limit.Value * 1000 };
                        else return;
                        DialogResult = DialogResult.OK;
                    };
                }
                var buttons = new FlowLayoutPanel
                {
                    FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, BackColor = Theme.Bg, Margin = Theme.Pad(0, 16, 0, 0)
                };
                buttons.Controls.Add(ok);
                buttons.Controls.Add(cancel);
                body.Controls.Add(buttons);
                Controls.Add(body);
                KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) DialogResult = DialogResult.Cancel; };
            }

            /// <summary>Why the element at the point cannot be the target.</summary>
            private static string NameMissing(TargetProbe probe)
            {
                ElementProbe element = probe.Element;
                if (element == null || element.Name.Length == 0) return Loc.T("target.missing.none");
                return element.Count > 1
                    ? Loc.F("target.missing.many", element.Count, MacroStep.Lower(ConditionDialog.TypeLabel(element.ControlType)), element.Name)
                    : Loc.T("target.missing.unconfirmed");
            }
        }

        /// <summary>A bitmap scaled to fit, pixels kept sharp when enlarged (a captured region is small). Does not own the bitmap.</summary>
        internal sealed class PixelView : Control
        {
            private Bitmap image;

            public PixelView()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }

            public Bitmap Image
            {
                get => image;
                set
                {
                    image = value;
                    Invalidate();
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (image == null) return;
                float scale = Math.Min((float)Width / image.Width, (float)Height / image.Height);
                if (scale > 1) scale = (float)Math.Floor(scale); // whole multiples keep every pixel square
                var size = new SizeF(image.Width * scale, image.Height * scale);
                e.Graphics.InterpolationMode = scale >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
                e.Graphics.DrawImage(image, new RectangleF((Width - size.Width) / 2, (Height - size.Height) / 2, size.Width, size.Height));
            }
        }

        /// <summary>The pause optimization: long waits, each with a tick (all off) and a new value (1 s).</summary>
        private sealed class PauseOptimizer : Form
        {
            public readonly Dictionary<int, int> Result = new Dictionary<int, int>();

            public PauseOptimizer(Macro macro, List<int> waits)
            {
                Text = Loc.T("opt.title");
                Dialogs.Style(this);
                var body = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2,
                    BackColor = Theme.Bg
                };
                body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                var intro = new Label
                {
                    Text = Loc.T("opt.intro"), AutoSize = true, ForeColor = Theme.Dim, BackColor = Theme.Bg, UseMnemonic = false,
                    MaximumSize = new Size(Theme.Dp(440), 0), Margin = Theme.Pad(0, 0, 0, 12)
                };
                body.Controls.Add(intro);
                body.SetColumnSpan(intro, 2);
                var rows = new List<Tuple<int, LedCheck, NumberField>>();
                foreach (int index in waits)
                {
                    var check = new LedCheck
                    {
                        Text = Loc.F("opt.row", index + 1, MacroStep.Seconds(macro.Steps[index].DurationMs)),
                        BackColor = Theme.Bg, Margin = Theme.Pad(0, 4, 16, 6)
                    };
                    var value = new NumberField { Minimum = 0, Maximum = MacroStep.MaxWaitMs / 1000, Decimals = 1, Size = Theme.Sz(90, 36), Unit = "s" };
                    value.Number = 1;
                    value.Enabled = false;
                    check.CheckedChanged += delegate { value.Enabled = check.Checked; };
                    body.Controls.Add(check);
                    body.Controls.Add(value);
                    rows.Add(Tuple.Create(index, check, value));
                }
                var buttons = new FlowLayoutPanel
                {
                    FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, BackColor = Theme.Bg,
                    Margin = Theme.Pad(0, 14, 0, 0)
                };
                var apply = new KeyButton { Text = Loc.T("opt.apply"), Kind = ButtonKind.Primary, Size = Theme.Sz(110, 36) };
                var cancel = new KeyButton { Text = Loc.T("ask.cancel"), Kind = ButtonKind.Secondary, Size = Theme.Sz(110, 36), Margin = Theme.Pad(0, 0, 8, 0) };
                apply.Click += delegate
                {
                    foreach (Tuple<int, LedCheck, NumberField> row in rows)
                        if (row.Item2.Checked) Result[row.Item1] = (int)Math.Round(row.Item3.Number * 1000);
                    DialogResult = DialogResult.OK;
                };
                cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
                buttons.Controls.Add(apply);
                buttons.Controls.Add(cancel);
                body.Controls.Add(buttons);
                body.SetColumnSpan(buttons, 2);
                Controls.Add(body);
            }
        }
    }
}
