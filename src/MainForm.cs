using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Windows.Forms;

namespace VClicker
{
    internal sealed class MainForm : Form
    {
        private const int IdToggle = 1, IdStop = 2, IdCapture = 3;
        private const string RepoUrl = "https://github.com/marquezinii/V-Clicker";
        private const string LabelColumn = "150";
        private static readonly Padding RowGap = Theme.Pad(0, 0, 0, 8);

        private readonly string settingsPath;
        private readonly ClickSettings settings;
        private readonly ClickEngine engine;
        private readonly List<Action> localizers = new List<Action>();
        private readonly List<Control> lockable = new List<Control>();
        private readonly List<NumberField> numberFields = new List<NumberField>();
        private readonly Timer uiTimer = new Timer { Interval = 100 };
        private readonly Timer captureTimer = new Timer { Interval = 1000 };
        private readonly ToolTip tips = new ToolTip { InitialDelay = 400, ReshowDelay = 200 };

        private HotkeyRegistry hotkeys;
        private NotifyIcon tray;
        private ToolStripMenuItem trayShow, trayToggle, trayExit;

        private Badge badge;
        private Label clicksValue, cpsValue, timeValue, notice, rateLabel;
        private FlatButton startButton, captureButton;
        private NumberField fHours, fMinutes, fSeconds, fMillis, fJitter, fRepeat, fX, fY, fDelay;
        private Segmented segLanguage, segButton, segType, segRepeat, segPosition, segMode;
        private HotkeyBox hkToggle, hkStop, hkCapture;
        private DarkCheck chkTop, chkTray, chkSounds;
        private LinkLabel adminLink;

        private bool loading, saveWarned;
        private StopReason? lastStop;
        private double cps;
        private long lastSampleStamp, lastSampleClicks, noticeExpires;
        private int captureSecondsLeft;

        public MainForm(string settingsPath) : this(settingsPath, new SendInputSink())
        {
        }

        internal MainForm(string settingsPath, IClickSink sink)
        {
            this.settingsPath = settingsPath;
            engine = new ClickEngine(sink);
            settings = ClickSettings.Load(settingsPath);
            Loc.SetLanguage(settings.Language);

            Text = "V-Clicker";
            Icon = LoadIcon();
            AutoScaleMode = AutoScaleMode.None; // pixel metrics are scaled by Theme.Dp() instead
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = Theme.Pad(20);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui(9.5f);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;

            BuildLayout();
            BuildTray();

            uiTimer.Tick += delegate { RefreshStats(); };
            captureTimer.Tick += OnCaptureTick;
            engine.Stopped += OnEngineStopped;

            ApplySettingsToUi();
            ApplyLanguage();
            ApplyWindowOptions();
            uiTimer.Start();
        }

        internal ClickEngine Engine => engine;

        /// <summary>Called when a second instance is launched: bring this window back.</summary>
        public void ShowFromTray()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        // ------------------------------------------------------------------ layout

        private static Grid Table(params string[] columns)
        {
            var table = new Grid
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                ColumnCount = columns.Length,
                Margin = Padding.Empty,
                BackColor = Theme.Surface // inside cards; the few tables on the window background override this
            };
            foreach (string column in columns)
            {
                if (column == "auto") table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                else if (column.EndsWith("%", StringComparison.Ordinal))
                    table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,
                        float.Parse(column.TrimEnd('%'), CultureInfo.InvariantCulture)));
                else table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,
                    float.Parse(column, CultureInfo.InvariantCulture) * Theme.Scale));
            }
            return table;
        }

        private static void Row(TableLayoutPanel table, params Control[] cells)
        {
            int row = table.RowCount;
            table.RowCount = row + 1;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            for (int i = 0; i < cells.Length; i++)
                if (cells[i] != null) table.Controls.Add(cells[i], i, row);
        }

        private static void FullRow(TableLayoutPanel table, Control cell)
        {
            Row(table, cell);
            table.SetColumnSpan(cell, table.ColumnCount);
        }

        private static Card NewCard(Grid body)
        {
            var card = new Card
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Margin = Theme.Pad(0, 0, 0, 10),
                Padding = Theme.Pad(16, 12, 16, 4)
            };
            body.BackColor = Theme.Surface;
            card.Controls.Add(body);
            return card;
        }

        private void Localize(Action apply)
        {
            localizers.Add(apply);
            apply();
        }

        private Label Lbl(string key, float size = 9.5f, bool dim = false, bool semibold = false)
        {
            var label = new Label
            {
                AutoSize = true,
                Font = Theme.Ui(size, semibold),
                ForeColor = dim ? Theme.Dim : Theme.Text,
                BackColor = Theme.Surface,
                Anchor = AnchorStyles.Left,
                Margin = Theme.Pad(0, 0, 8, 8),
                UseMnemonic = false
            };
            Localize(() => label.Text = Loc.T(key));
            return label;
        }

        /// <summary>Label for the left column of a settings row: fills its cell, never wraps.</summary>
        private Label RowLabel(string key)
        {
            Label label = Lbl(key);
            label.AutoSize = false;
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.AutoEllipsis = true;
            return label;
        }

        private Label Title(string key)
        {
            Label label = Lbl(key, 8.5f, dim: true, semibold: true);
            label.Margin = Theme.Pad(0, 0, 0, 8);
            return label;
        }

        private Segmented Seg(params string[] keys)
        {
            var segmented = new Segmented { Dock = DockStyle.Fill, Margin = RowGap };
            Localize(() =>
            {
                var items = new string[keys.Length];
                for (int i = 0; i < keys.Length; i++) items[i] = Loc.T(keys[i]);
                segmented.Items = items;
            });
            return segmented;
        }

        private NumberField Num(int min, int max, string accessibleName, string unit = "")
        {
            var field = new NumberField
            {
                Minimum = min,
                Maximum = max,
                Unit = unit,
                FieldName = accessibleName,
                Dock = DockStyle.Fill,
                Margin = RowGap
            };
            field.Value = min;
            numberFields.Add(field);
            lockable.Add(field);
            return field;
        }

        private HotkeyBox Hk(int id)
        {
            var box = new HotkeyBox { Dock = DockStyle.Fill, Margin = RowGap };
            box.Accept = hotkey => AcceptHotkey(box, id, hotkey);
            box.CapturingChanged += delegate
            {
                if (hotkeys == null) return;
                // While listening, F6/F7/F8 must reach the box instead of firing their actions.
                if (box.Capturing) hotkeys.UnregisterAll();
                else RegisterAllHotkeys();
            };
            Localize(() => box.PromptText = Loc.T("hk.press"));
            lockable.Add(box);
            return box;
        }

        private void BuildLayout()
        {
            var root = new Grid
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 2,
                Margin = Padding.Empty,
                BackColor = Theme.Bg
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.Dp(400)));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.Dp(356)));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Control header = BuildHeader();
            root.Controls.Add(header, 0, 0);
            root.SetColumnSpan(header, 2);
            root.Controls.Add(BuildLeftColumn(), 0, 1);
            Control right = BuildRightColumn();
            right.Margin = Theme.Pad(16, 0, 0, 0);
            root.Controls.Add(right, 1, 1);

            Controls.Add(root);
        }

        private Control BuildHeader()
        {
            Image logo;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("icon-256.png"))
            using (var decoded = new Bitmap(stream))
                logo = new Bitmap(decoded); // GDI+ needs the source stream open for the life of a stream-backed Bitmap
            Disposed += delegate { logo.Dispose(); };

            var header = Table("auto", "100%", "auto");
            header.BackColor = Theme.Bg;
            header.Margin = Theme.Pad(0, 0, 0, 14);

            var mark = new LogoBox(logo) { Size = Theme.Sz(44, 44), Margin = Theme.Pad(0, 0, 12, 0) };

            var titles = Table("100%");
            titles.BackColor = Theme.Bg;
            titles.Anchor = AnchorStyles.Left;
            titles.Dock = DockStyle.None;
            var title = new Label
            {
                Text = "V-Clicker", AutoSize = true, Font = Theme.Ui(17f, true), ForeColor = Theme.Text,
                BackColor = Theme.Bg, Margin = Padding.Empty, UseMnemonic = false
            };
            var subtitle = new Label
            {
                AutoSize = true, Font = Theme.Ui(9.25f), ForeColor = Theme.Dim, BackColor = Theme.Bg,
                Margin = Padding.Empty, UseMnemonic = false
            };
            Localize(() => subtitle.Text = Loc.T("subtitle"));
            Row(titles, title);
            Row(titles, subtitle);

            segLanguage = new Segmented
            {
                Items = new[] { "PT", "EN" }, Size = Theme.Sz(92, 28), Anchor = AnchorStyles.Right,
                Margin = Padding.Empty
            };
            segLanguage.AccessibleName = "Language";

            Row(header, mark, titles, segLanguage);
            return header;
        }

        private Control BuildLeftColumn()
        {
            Grid column = Table("100%");
            column.BackColor = Theme.Bg;
            Row(column, BuildIntervalCard());
            Row(column, BuildClickCard());
            Row(column, BuildPositionCard());
            AddFiller(column);
            return column;
        }

        /// <summary>Empty last row that soaks up spare height so the cards above keep their natural size.</summary>
        private static void AddFiller(TableLayoutPanel column)
        {
            column.RowCount++;
            column.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        }

        private Control BuildIntervalCard()
        {
            Grid body = Table("100%");
            FullRow(body, Title("card.interval"));

            Grid fields = Table("25%", "25%", "25%", "25%");
            fHours = Num(0, 99999, "Hours");
            fMinutes = Num(0, 99999, "Minutes");
            fSeconds = Num(0, 99999, "Seconds");
            fMillis = Num(0, 99999999, "Milliseconds");
            foreach (NumberField field in new[] { fHours, fMinutes, fSeconds, fMillis })
            {
                field.Margin = Theme.Pad(0, 0, 8, 2);
                field.ValueChanged += OnIntervalFieldChanged;
            }
            fMillis.Margin = Theme.Pad(0, 0, 0, 2);
            Row(fields, fHours, fMinutes, fSeconds, fMillis);
            Row(fields, UnitLabel("unit.h"), UnitLabel("unit.min"), UnitLabel("unit.s"), UnitLabel("unit.ms"));
            FullRow(body, fields);

            var chips = new FlowLayoutPanel
            {
                AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = Theme.Pad(0, 2, 0, 6),
                BackColor = Theme.Surface
            };
            foreach (int perSecond in new[] { 1, 10, 50, 100, 500 })
            {
                int ms = 1000 / perSecond;
                var chip = new FlatButton
                {
                    Kind = ButtonKind.Chip, Text = perSecond + "/s", Size = Theme.Sz(56, 26),
                    Margin = Theme.Pad(0, 0, 6, 0), Font = Theme.Ui(9f, true)
                };
                chip.Click += delegate { SetInterval(ms); };
                chips.Controls.Add(chip);
                lockable.Add(chip);
            }
            FullRow(body, chips);

            rateLabel = new Label
            {
                AutoSize = true, Font = Theme.Ui(9f), ForeColor = Theme.Dim, BackColor = Theme.Surface,
                Margin = Theme.Pad(0, 0, 0, 12), UseMnemonic = false
            };
            FullRow(body, rateLabel);

            Grid jitterRow = Table(LabelColumn, "100%");
            fJitter = Num(0, 50, "Random variation percent", "%");
            fJitter.Anchor = AnchorStyles.Left;
            fJitter.Dock = DockStyle.None;
            fJitter.Size = Theme.Sz(110, 30);
            fJitter.ValueChanged += OnConfigChanged;
            Label jitterLabel = RowLabel("jitter");
            Row(jitterRow, jitterLabel, fJitter);
            Localize(() => tips.SetToolTip(fJitter, Loc.T("jitter.tip")));
            Localize(() => tips.SetToolTip(jitterLabel, Loc.T("jitter.tip")));
            FullRow(body, jitterRow);

            return NewCard(body);
        }

        private Label UnitLabel(string key)
        {
            Label label = Lbl(key, 8.5f, dim: true);
            label.Anchor = AnchorStyles.None;
            label.Margin = Theme.Pad(0, 0, 8, 0);
            return label;
        }

        private Control BuildClickCard()
        {
            Grid body = Table(LabelColumn, "100%");
            FullRow(body, Title("card.click"));

            segButton = Seg("button.left", "button.middle", "button.right");
            segType = Seg("type.single", "type.double");
            segRepeat = Seg("repeat.infinite", "repeat.count");
            fRepeat = Num(1, 1000000000, "Number of clicks");
            segButton.AccessibleName = "Mouse button";
            segType.AccessibleName = "Click type";
            segRepeat.AccessibleName = "Repeat mode";
            lockable.AddRange(new Control[] { segButton, segType, segRepeat });
            segButton.SelectedChanged += OnConfigChanged;
            segType.SelectedChanged += OnConfigChanged;
            segRepeat.SelectedChanged += OnConfigChanged;
            fRepeat.ValueChanged += OnConfigChanged;

            Row(body, RowLabel("button"), segButton);
            Row(body, RowLabel("type"), segType);

            Grid repeat = Table("60%", "40%");
            segRepeat.Margin = Theme.Pad(0, 0, 8, 0);
            fRepeat.Margin = Padding.Empty;
            Row(repeat, segRepeat, fRepeat);
            repeat.Margin = RowGap;
            Row(body, RowLabel("repeat"), repeat);

            segMode = Seg("mode.toggle", "mode.hold");
            segMode.AccessibleName = "Hotkey mode";
            lockable.Add(segMode);
            segMode.SelectedChanged += OnConfigChanged;
            Label modeLabel = RowLabel("mode");
            Row(body, modeLabel, segMode);
            Localize(() => tips.SetToolTip(segMode, Loc.T("mode.tip")));
            Localize(() => tips.SetToolTip(modeLabel, Loc.T("mode.tip")));

            fDelay = Num(0, 30, "Start delay seconds", "s");
            fDelay.Anchor = AnchorStyles.Left;
            fDelay.Dock = DockStyle.None;
            fDelay.Size = Theme.Sz(110, 30);
            fDelay.ValueChanged += OnConfigChanged;
            Label delayLabel = RowLabel("delay");
            Row(body, delayLabel, fDelay);
            Localize(() => tips.SetToolTip(fDelay, Loc.T("delay.tip")));
            Localize(() => tips.SetToolTip(delayLabel, Loc.T("delay.tip")));

            return NewCard(body);
        }

        private Control BuildPositionCard()
        {
            Grid body = Table("100%");
            FullRow(body, Title("card.position"));

            segPosition = Seg("pos.cursor", "pos.fixed");
            segPosition.AccessibleName = "Click position";
            lockable.Add(segPosition);
            segPosition.SelectedChanged += OnConfigChanged;
            FullRow(body, segPosition);

            fX = Num(-32768, 32767, "Position X");
            fY = Num(-32768, 32767, "Position Y");
            fX.ValueChanged += OnConfigChanged;
            fY.ValueChanged += OnConfigChanged;
            fX.Margin = Theme.Pad(0, 0, 10, 10);
            fY.Margin = Theme.Pad(0, 0, 10, 10);

            captureButton = new FlatButton
            {
                Kind = ButtonKind.Secondary, Dock = DockStyle.Fill, Margin = Theme.Pad(0, 0, 0, 8), Width = Theme.Dp(100), Height = Theme.Dp(30)
            };
            captureButton.Click += OnCaptureButton;
            lockable.Add(captureButton);

            Grid position = Table("auto", "50%", "auto", "50%", "auto");
            Row(position, Axis("X"), fX, Axis("Y"), fY, captureButton);
            FullRow(body, position);

            return NewCard(body);
        }

        private static Label Axis(string text) => new Label
        {
            Text = text, AutoSize = true, Font = Theme.Ui(9.5f, true), ForeColor = Theme.Dim,
            BackColor = Theme.Surface, Anchor = AnchorStyles.Left, Margin = Theme.Pad(0, 0, 8, 10), UseMnemonic = false
        };

        private Control BuildRightColumn()
        {
            Grid column = Table("100%");
            column.BackColor = Theme.Bg;
            Row(column, BuildStatusCard());
            Row(column, BuildHotkeysCard());
            Row(column, BuildOptionsCard());
            Row(column, BuildFooter());
            AddFiller(column);
            return column;
        }

        private Control BuildStatusCard()
        {
            Grid body = Table("100%");

            badge = new Badge { Anchor = AnchorStyles.Left, Margin = Theme.Pad(0, 0, 0, 12) };
            FullRow(body, badge);

            Grid tiles = Table("34%", "33%", "33%");
            Row(tiles, Tile("stat.clicks", out clicksValue), Tile("stat.cps", out cpsValue), Tile("stat.time", out timeValue));
            tiles.Margin = Theme.Pad(0, 0, 0, 12);
            FullRow(body, tiles);

            startButton = new FlatButton
            {
                Kind = ButtonKind.Primary, Dock = DockStyle.Fill, Height = Theme.Dp(48), Margin = Theme.Pad(0, 0, 0, 10),
                Font = Theme.Ui(12f, true), AccessibleName = "Start or stop clicking"
            };
            startButton.Click += delegate { Toggle(false); };
            FullRow(body, startButton);

            notice = new Label
            {
                AutoSize = false, Height = Theme.Dp(38), Dock = DockStyle.Fill, Font = Theme.Ui(9f), ForeColor = Theme.Dim,
                BackColor = Theme.Surface, Margin = Theme.Pad(0, 0, 0, 6), UseMnemonic = false
            };
            FullRow(body, notice);

            return NewCard(body);
        }

        private Control Tile(string captionKey, out Label value)
        {
            Grid tile = Table("100%");
            tile.Margin = Padding.Empty;
            Label caption = Lbl(captionKey, 8f, dim: true, semibold: true);
            caption.Margin = Theme.Pad(0, 0, 0, 2);
            value = new Label
            {
                AutoSize = true, Font = Theme.Ui(16f, true), ForeColor = Theme.Text, BackColor = Theme.Surface,
                Margin = Padding.Empty, Text = "0", UseMnemonic = false
            };
            Row(tile, caption);
            Row(tile, value);
            return tile;
        }

        private Control BuildHotkeysCard()
        {
            Grid body = Table(LabelColumn, "100%");
            FullRow(body, Title("card.hotkeys"));

            hkToggle = Hk(IdToggle);
            hkStop = Hk(IdStop);
            hkCapture = Hk(IdCapture);
            hkToggle.AccessibleName = "Start or stop hotkey";
            hkStop.AccessibleName = "Stop hotkey";
            hkCapture.AccessibleName = "Capture position hotkey";
            hkToggle.ValueChanged += OnHotkeyChanged;
            hkStop.ValueChanged += OnHotkeyChanged;
            hkCapture.ValueChanged += OnHotkeyChanged;
            Row(body, RowLabel("hk.toggle"), hkToggle);
            Row(body, RowLabel("hk.stop"), hkStop);
            Row(body, RowLabel("hk.capture"), hkCapture);

            return NewCard(body);
        }

        private Control BuildOptionsCard()
        {
            Grid body = Table("50%", "50%");
            chkTop = Check("opt.top");
            chkSounds = Check("opt.sounds");
            chkTray = Check("opt.tray");
            Row(body, chkTop, chkSounds);
            FullRow(body, chkTray);
            return NewCard(body);
        }

        private DarkCheck Check(string key)
        {
            var check = new DarkCheck { Margin = Theme.Pad(0, 0, 0, 6), Anchor = AnchorStyles.Left };
            Localize(() => check.Text = Loc.T(key));
            check.CheckedChanged += OnConfigChanged;
            return check;
        }

        private Control BuildFooter()
        {
            Grid footer = Table("auto", "100%");
            footer.BackColor = Theme.Bg;
            footer.Margin = Theme.Pad(2, 0, 0, 0);

            var github = new LinkLabel
            {
                Text = "GitHub  ·  v" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3),
                AutoSize = true, Font = Theme.Ui(9f), BackColor = Theme.Bg, Anchor = AnchorStyles.Left,
                LinkColor = Theme.Dim, ActiveLinkColor = Theme.Accent, VisitedLinkColor = Theme.Dim,
                LinkBehavior = LinkBehavior.HoverUnderline, Margin = Padding.Empty
            };
            github.LinkClicked += delegate { OpenUrl(RepoUrl); };

            adminLink = new LinkLabel
            {
                AutoSize = true, Font = Theme.Ui(9f), BackColor = Theme.Bg, Anchor = AnchorStyles.Right,
                LinkColor = Theme.Accent, ActiveLinkColor = Theme.AccentHover, VisitedLinkColor = Theme.Accent,
                LinkBehavior = LinkBehavior.HoverUnderline, Margin = Padding.Empty
            };
            adminLink.LinkClicked += delegate { RestartAsAdministrator(); };
            if (IsElevated())
            {
                adminLink.Enabled = false;
                adminLink.LinkColor = adminLink.DisabledLinkColor = Theme.Success;
                Localize(() => adminLink.Text = "● " + Loc.T("admin.on"));
            }
            else
            {
                Localize(() =>
                {
                    adminLink.Text = Loc.T("link.admin");
                    tips.SetToolTip(adminLink, Loc.T("link.admin.tip"));
                });
            }

            Row(footer, github, adminLink);
            return footer;
        }

        private void BuildTray()
        {
            trayShow = new ToolStripMenuItem();
            trayShow.Click += delegate { ShowFromTray(); };
            trayToggle = new ToolStripMenuItem();
            trayToggle.Click += delegate { Toggle(false); };
            trayExit = new ToolStripMenuItem();
            trayExit.Click += delegate { Close(); };

            var menu = new ContextMenuStrip();
            menu.Items.AddRange(new ToolStripItem[] { trayShow, trayToggle, new ToolStripSeparator(), trayExit });

            tray = new NotifyIcon { Icon = Icon, Text = "V-Clicker", ContextMenuStrip = menu, Visible = false };
            tray.MouseClick += (sender, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(); };
        }

        private static Icon LoadIcon()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("icon.ico"))
                return new Icon(stream);
        }

        // ------------------------------------------------------------------ settings <-> UI

        private void ApplySettingsToUi()
        {
            loading = true;
            Interval.Split(settings.IntervalMs, out int h, out int m, out int s, out int ms);
            fHours.Value = h;
            fMinutes.Value = m;
            fSeconds.Value = s;
            fMillis.Value = ms;
            fJitter.Value = settings.JitterPercent;
            segButton.SetSelectedSilently((int)settings.Button);
            segType.SetSelectedSilently(settings.DoubleClick ? 1 : 0);
            segRepeat.SetSelectedSilently(settings.RepeatCount > 0 ? 1 : 0);
            fRepeat.Value = settings.RepeatCount > 0 ? settings.RepeatCount : 100;
            segPosition.SetSelectedSilently(settings.UseFixedPosition ? 1 : 0);
            fX.Value = settings.X;
            fY.Value = settings.Y;
            fDelay.Value = settings.StartDelaySeconds;
            segMode.SetSelectedSilently(settings.HoldMode ? 1 : 0);
            hkToggle.Value = settings.ToggleKey;
            hkStop.Value = settings.StopKey;
            hkCapture.Value = settings.CaptureKey;
            chkTop.Checked = settings.AlwaysOnTop;
            chkTray.Checked = settings.MinimizeToTray;
            chkSounds.Checked = settings.Sounds;
            segLanguage.SetSelectedSilently(Loc.Current == "pt" ? 0 : 1);
            loading = false;
            UpdateRateLabel();
            UpdateEnabledState();
        }

        private void ReadUi()
        {
            foreach (NumberField field in numberFields) field.Commit();
            settings.IntervalMs = Interval.Join(fHours.Value, fMinutes.Value, fSeconds.Value, fMillis.Value);
            settings.JitterPercent = fJitter.Value;
            settings.Button = (ClickButton)segButton.SelectedIndex;
            settings.DoubleClick = segType.SelectedIndex == 1;
            settings.RepeatCount = segRepeat.SelectedIndex == 1 ? fRepeat.Value : 0;
            settings.UseFixedPosition = segPosition.SelectedIndex == 1;
            settings.X = fX.Value;
            settings.Y = fY.Value;
            settings.StartDelaySeconds = fDelay.Value;
            settings.HoldMode = segMode.SelectedIndex == 1;
            settings.ToggleKey = hkToggle.Value;
            settings.StopKey = hkStop.Value;
            settings.CaptureKey = hkCapture.Value;
            settings.AlwaysOnTop = chkTop.Checked;
            settings.MinimizeToTray = chkTray.Checked;
            settings.Sounds = chkSounds.Checked;
        }

        private void SaveSettings()
        {
            if (settings.Save(settingsPath) || saveWarned) return;
            saveWarned = true;
            ShowNotice(Loc.T("msg.saveFailed"), Theme.Warning, 8);
        }

        private void OnConfigChanged(object sender, EventArgs e)
        {
            if (loading) return;
            ReadUi();
            SaveSettings();
            UpdateRateLabel();
            UpdateEnabledState();
            ApplyWindowOptions();
            UpdateStartButton();
        }

        private void OnIntervalFieldChanged(object sender, EventArgs e)
        {
            if (loading) return;
            SetInterval(Interval.Join(fHours.Value, fMinutes.Value, fSeconds.Value, fMillis.Value));
        }

        /// <summary>Puts a total interval into the four fields (overflow carries: 90 s becomes 1 min 30 s).</summary>
        private void SetInterval(int totalMs)
        {
            Interval.Split(totalMs, out int h, out int m, out int s, out int ms);
            loading = true;
            fHours.Value = h;
            fMinutes.Value = m;
            fSeconds.Value = s;
            fMillis.Value = ms;
            loading = false;
            OnConfigChanged(this, EventArgs.Empty);
        }

        private void UpdateRateLabel()
        {
            double perSecond = Interval.ClicksPerSecond(
                Interval.Join(fHours.Value, fMinutes.Value, fSeconds.Value, fMillis.Value));
            rateLabel.Text = perSecond >= 1
                ? Loc.F("rate.perSecond", perSecond.ToString(perSecond >= 10 ? "0" : "0.#", CultureInfo.CurrentCulture))
                : Loc.F("rate.every", (1 / perSecond).ToString("0.#", CultureInfo.CurrentCulture));
        }

        private void UpdateEnabledState()
        {
            bool idle = engine.Phase == EnginePhase.Idle;
            foreach (Control control in lockable) control.Enabled = idle;
            fRepeat.Enabled = idle && segRepeat.SelectedIndex == 1;
            bool fixedSpot = segPosition.SelectedIndex == 1;
            fX.Enabled = fY.Enabled = idle && fixedSpot;
            captureButton.Enabled = idle && fixedSpot;
            fDelay.Enabled = idle && segMode.SelectedIndex == 0;
        }

        private void ApplyWindowOptions()
        {
            if (TopMost != settings.AlwaysOnTop) TopMost = settings.AlwaysOnTop;
            if (tray.Visible != settings.MinimizeToTray) tray.Visible = settings.MinimizeToTray;
        }

        private void ApplyLanguage()
        {
            foreach (Action apply in localizers) apply();
            captureButton.Text = captureSecondsLeft > 0
                ? captureSecondsLeft.ToString(CultureInfo.CurrentCulture)
                : Loc.T("capture");
            trayShow.Text = Loc.T("tray.show");
            trayExit.Text = Loc.T("tray.exit");
            UpdateRateLabel();
            UpdateStartButton();
            ShowHint();
            RefreshStats();
            PerformLayout();
            Invalidate(true);
        }

        private void UpdateStartButton()
        {
            bool running = engine.Phase != EnginePhase.Idle;
            startButton.Kind = running ? ButtonKind.Danger : ButtonKind.Primary;
            startButton.Text = Loc.T(running ? "stop" : "start") + "   ·   " + hkToggle.Value;
            trayToggle.Text = Loc.T(running ? "stop" : "start");
            tips.SetToolTip(captureButton, Loc.F("capture.tip", hkCapture.Value));
        }

        // ------------------------------------------------------------------ language

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            settings.Language = segLanguage.SelectedIndex == 0 ? "pt" : "en";
            Loc.SetLanguage(settings.Language);
            SaveSettings();
            ApplyLanguage();
        }

        // ------------------------------------------------------------------ run control

        internal void Toggle(bool fromHotkey)
        {
            if (engine.Phase != EnginePhase.Idle) engine.Stop();
            else StartClicking(fromHotkey);
        }

        private void StartClicking(bool fromHotkey)
        {
            if (engine.Phase != EnginePhase.Idle) return;
            ReadUi();
            SaveSettings();

            ClickPlan plan = settings.ToPlan();
            if (settings.HoldMode && fromHotkey)
            {
                plan.HoldKey = (int)settings.ToggleKey.Key;
                plan.StartDelayMs = 0;
            }

            lastStop = null;
            cps = 0;
            lastSampleStamp = Stopwatch.GetTimestamp();
            lastSampleClicks = 0;
            ShowHint();
            if (!engine.Start(plan)) return;

            UpdateEnabledState();
            UpdateStartButton();
            RefreshStats();
            Beep(true);
        }

        private void OnEngineStopped(StopReason reason)
        {
            if (!IsHandleCreated || IsDisposed) return;
            try
            {
                BeginInvoke(new Action(() => HandleStopped(reason)));
            }
            catch (InvalidOperationException)
            {
                // The window is closing; nothing left to update.
            }
        }

        private void HandleStopped(StopReason reason)
        {
            lastStop = reason;
            UpdateEnabledState();
            UpdateStartButton();
            if (reason == StopReason.Completed)
                ShowNotice(Loc.F("msg.done", engine.Clicks.ToString("N0", CultureInfo.CurrentCulture)), Theme.Success, 6);
            else if (reason == StopReason.Blocked)
                ShowNotice(Loc.T("msg.blocked"), Theme.Danger, 0);
            Beep(false);
            RefreshStats();
        }

        private void Beep(bool starting)
        {
            if (!settings.Sounds) return;
            uint frequency = starting ? 880u : 520u;
            uint duration = starting ? 70u : 90u;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate { Native.Beep(frequency, duration); });
        }

        // ------------------------------------------------------------------ position capture

        private void OnCaptureButton(object sender, EventArgs e)
        {
            // Give the user three seconds to move the mouse to the spot they want.
            captureSecondsLeft = 3;
            captureButton.Text = captureSecondsLeft.ToString(CultureInfo.CurrentCulture);
            captureTimer.Start();
        }

        private void OnCaptureTick(object sender, EventArgs e)
        {
            captureSecondsLeft--;
            if (captureSecondsLeft > 0)
            {
                captureButton.Text = captureSecondsLeft.ToString(CultureInfo.CurrentCulture);
                return;
            }
            captureTimer.Stop();
            captureButton.Text = Loc.T("capture");
            CapturePosition();
        }

        private void CapturePosition()
        {
            if (engine.Phase != EnginePhase.Idle) return;
            Native.GetCursorPos(out Native.POINT point);
            fX.Value = point.X;
            fY.Value = point.Y;
            segPosition.SetSelectedSilently(1);
            OnConfigChanged(this, EventArgs.Empty);
            ShowNotice(Loc.F("msg.captured", point.X, point.Y), Theme.Success, 5);
        }

        // ------------------------------------------------------------------ hotkeys

        private bool AcceptHotkey(HotkeyBox box, int id, Hotkey hotkey)
        {
            if (!hotkey.IsAcceptable)
            {
                ShowNotice(Loc.T("msg.hkInvalid"), Theme.Warning, 6);
                return false;
            }
            foreach (HotkeyBox other in new[] { hkToggle, hkStop, hkCapture })
            {
                if (other != box && other.Value.Equals(hotkey))
                {
                    ShowNotice(Loc.F("msg.hkDuplicate", hotkey), Theme.Warning, 6);
                    return false;
                }
            }
            // Test-register only; the box re-registers everything from its values when it stops listening.
            if (hotkeys != null && !hotkeys.Register(id, hotkey))
            {
                ShowNotice(Loc.F("msg.hkTaken", hotkey), Theme.Warning, 6);
                return false;
            }
            return true;
        }

        private void OnHotkeyChanged(object sender, EventArgs e)
        {
            OnConfigChanged(sender, e);
            ShowHint();
        }

        private void RegisterAllHotkeys()
        {
            var failed = new List<Hotkey>();
            if (!hotkeys.Register(IdToggle, hkToggle.Value)) failed.Add(hkToggle.Value);
            if (!hotkeys.Register(IdStop, hkStop.Value)) failed.Add(hkStop.Value);
            if (!hotkeys.Register(IdCapture, hkCapture.Value)) failed.Add(hkCapture.Value);
            if (failed.Count > 0)
                ShowNotice(Loc.F("msg.hkUnavailable", string.Join(", ", failed)), Theme.Warning, 0);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                switch (m.WParam.ToInt32())
                {
                    case IdToggle: Toggle(true); break;
                    case IdStop: engine.Stop(); break;
                    case IdCapture: CapturePosition(); break;
                }
                return;
            }
            base.WndProc(ref m);
        }

        // ------------------------------------------------------------------ status

        private void ShowNotice(string text, Color color, int seconds)
        {
            notice.Text = text;
            notice.ForeColor = color;
            noticeExpires = seconds > 0 ? Stopwatch.GetTimestamp() + seconds * Stopwatch.Frequency : 0;
        }

        private void ShowHint()
        {
            notice.Text = Loc.F("hint", hkToggle.Value, hkStop.Value, hkCapture.Value);
            notice.ForeColor = Theme.Dim;
            noticeExpires = 0;
        }

        private void RefreshStats()
        {
            EnginePhase phase = engine.Phase;
            long clicks = engine.Clicks;
            clicksValue.Text = clicks.ToString("N0", CultureInfo.CurrentCulture);
            timeValue.Text = FormatElapsed(engine.Elapsed);

            long now = Stopwatch.GetTimestamp();
            double seconds = (now - lastSampleStamp) / (double)Stopwatch.Frequency;
            if (seconds >= 0.25)
            {
                double instant = (clicks - lastSampleClicks) / seconds;
                cps = phase != EnginePhase.Clicking ? 0 : cps <= 0 ? instant : cps * 0.4 + instant * 0.6;
                lastSampleStamp = now;
                lastSampleClicks = clicks;
            }
            cpsValue.Text = cps >= 100 ? cps.ToString("0", CultureInfo.CurrentCulture)
                                       : cps.ToString("0.0", CultureInfo.CurrentCulture);

            switch (phase)
            {
                case EnginePhase.Countdown:
                    badge.Dot = Theme.Warning;
                    badge.Text = Loc.F("status.countdown",
                        engine.CountdownRemaining.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture) + " s");
                    break;
                case EnginePhase.Clicking:
                    badge.Dot = Theme.Success;
                    badge.Text = Loc.T("status.clicking");
                    break;
                default:
                    badge.Dot = lastStop == StopReason.Blocked ? Theme.Danger
                              : lastStop == StopReason.Completed ? Theme.Success : Theme.Dim;
                    badge.Text = lastStop == StopReason.Blocked ? Loc.T("status.blocked")
                               : lastStop == StopReason.Completed ? Loc.T("status.done")
                               : Loc.T("status.idle");
                    break;
            }
            badge.Size = badge.GetPreferredSize(Size.Empty);
            tray.Text = phase == EnginePhase.Idle ? "V-Clicker" : "V-Clicker - " + Loc.T("status.clicking");

            if (noticeExpires != 0 && now > noticeExpires) ShowHint();
        }

        private static string FormatElapsed(TimeSpan span)
        {
            if (span.TotalHours >= 1)
                return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", (int)span.TotalHours, span.Minutes, span.Seconds);
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", span.Minutes, span.Seconds);
        }

        // ------------------------------------------------------------------ window lifecycle

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            segLanguage.SelectedChanged += OnLanguageChanged;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            FitToScreen(Screen.FromControl(this).WorkingArea);
            Invalidate(true);
        }

        /// <summary>
        /// The window is sized by its content. On a screen too short for it, cap the height and
        /// scroll rather than letting the bottom (or the title bar) run off the screen.
        /// </summary>
        internal void FitToScreen(Rectangle area)
        {
            if (Height <= area.Height)
            {
                if (Bottom > area.Bottom || Top < area.Top) Top = Math.Max(area.Top, area.Bottom - Height);
                return;
            }
            Size full = Size; // read before AutoSize is switched off: the form falls back to its default size
            AutoSize = false;
            AutoScroll = true;
            Size = new Size(full.Width + SystemInformation.VerticalScrollBarWidth, area.Height);
            Top = area.Top;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.StyleTitleBar(Handle, ColorRef(Theme.Bg), ColorRef(Theme.Text), ColorRef(Theme.Border));
            hotkeys = new HotkeyRegistry(Handle);
            RegisterAllHotkeys();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            hotkeys?.UnregisterAll();
            base.OnHandleDestroyed(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized && settings != null && settings.MinimizeToTray) Hide();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            engine.Stop();
            ReadUi();
            settings.Save(settingsPath);
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                uiTimer.Dispose();
                captureTimer.Dispose();
                tips.Dispose();
                tray.Visible = false;
                tray.Dispose();
                engine.Dispose();
            }
            base.Dispose(disposing);
        }

        // ------------------------------------------------------------------ misc

        private static int ColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

        private static bool IsElevated()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }

        private void RestartAsAdministrator()
        {
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--wait-previous")
                {
                    UseShellExecute = true,
                    Verb = "runas"
                });
            }
            catch (Win32Exception)
            {
                // The user declined the UAC prompt (or Windows refused to elevate).
                ShowNotice(Loc.T("msg.adminFailed"), Theme.Warning, 6);
                return;
            }
            Close();
        }

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Win32Exception)
            {
                // No default browser registered: show the address so it can be copied.
                ShowNotice(url, Theme.Warning, 10);
            }
        }
    }

    internal sealed class Grid : TableLayoutPanel
    {
        public Grid()
        {
            DoubleBuffered = true;
        }
    }
}
