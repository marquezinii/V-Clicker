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
using Microsoft.Win32;

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
        private Label clicksValue, requestedValue, sentValue, timeValue, notice, rateLabel;
        private FlatButton startButton, captureButton;
        private NumberField fCps, fHours, fMinutes, fSeconds, fMillis, fJitter, fRepeat, fX, fY, fDelay;
        private Segmented segLanguage, segSpeed, segButton, segType, segRepeat, segPosition, segMode;
        private Control cpsRow, intervalRow;
        private HotkeyBox hkToggle, hkStop, hkCapture;
        private DarkCheck chkTop, chkTray, chkSounds, chkLock, chkHud;
        private Segmented segLockMode, segHudCorner;
        private Label lockAppLabel;
        private FlatButton lockChoose;
        private LinkLabel adminLink;

        // Window Lock: the exact window this run is bound to (Zero = no lock).
        private IntPtr lockedWindow;

        // On-screen indicator: created on the first start attempt; a stop or a refusal shows briefly, then hides.
        private Hud hud;
        private bool hudArmed;
        private Rectangle hudScreen;
        private string hudFlashText;
        private long hudFlashUntil;
        private int hudTicks;

        // Tray icon per state: the app icon when stopped, with a green / amber dot while clicking / paused.
        private Icon trayClicking, trayPaused;

        private bool loading, saveWarned;
        private StopReason? lastStop;
        private readonly RateMeter sentRate = new RateMeter();
        private long noticeExpires;
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
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;

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

        private NumberField Num(double min, double max, string accessibleName, string unit = "")
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
            field.Number = min;
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
            Row(column, BuildLockCard());
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

            // One speed, two ways to edit it: clicks per second (default) or the classic h / min / s / ms.
            segSpeed = Seg("speed.cps", "speed.interval");
            segSpeed.AccessibleName = "Speed unit";
            lockable.Add(segSpeed);
            segSpeed.SelectedChanged += delegate { ShowSpeedMode(); OnConfigChanged(this, EventArgs.Empty); };
            FullRow(body, segSpeed);

            Grid cps = Table(LabelColumn, "100%");
            fCps = Num(Interval.MinCps, Interval.MaxCps, "Clicks per second", "CPS");
            fCps.Decimals = 1;
            fCps.Anchor = AnchorStyles.Left;
            fCps.Dock = DockStyle.None;
            fCps.Size = Theme.Sz(140, 30);
            fCps.ValueChanged += delegate { if (!loading) SetInterval(Interval.FromCps(fCps.Number)); };
            Row(cps, RowLabel("speed.perSecond"), fCps);
            cpsRow = cps;
            FullRow(body, cps);

            Grid fields = Table("25%", "25%", "25%", "25%");
            fHours = Num(0, 99999, "Hours");
            fMinutes = Num(0, 99999, "Minutes");
            fSeconds = Num(0, 99999, "Seconds");
            fMillis = Num(0, 99999999, "Milliseconds");
            fMillis.Decimals = 3;
            foreach (NumberField field in new[] { fHours, fMinutes, fSeconds, fMillis })
            {
                field.Margin = Theme.Pad(0, 0, 8, 2);
                field.ValueChanged += OnIntervalFieldChanged;
            }
            fMillis.Margin = Theme.Pad(0, 0, 0, 2);
            Row(fields, fHours, fMinutes, fSeconds, fMillis);
            Row(fields, UnitLabel("unit.h"), UnitLabel("unit.min"), UnitLabel("unit.s"), UnitLabel("unit.ms"));
            intervalRow = fields;
            FullRow(body, fields);

            var chips = new FlowLayoutPanel
            {
                AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = Theme.Pad(0, 2, 0, 6),
                BackColor = Theme.Surface
            };
            foreach (int perSecond in new[] { 5, 10, 15, 20, 50 })
            {
                var chip = new FlatButton
                {
                    Kind = ButtonKind.Chip, Text = perSecond + " CPS", Size = Theme.Sz(66, 26),
                    Margin = Theme.Pad(0, 0, 6, 0), Font = Theme.Ui(9f, true)
                };
                chip.Click += delegate { SetInterval(1000.0 / perSecond); };
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

        private Control BuildLockCard()
        {
            Grid body = Table(LabelColumn, "100%");
            FullRow(body, Title("card.lock"));

            chkLock = Check("lock.enable");
            chkLock.AccessibleName = "Window Lock";
            lockable.Add(chkLock);
            Localize(() => tips.SetToolTip(chkLock, Loc.T("lock.tip")));
            FullRow(body, chkLock);

            lockAppLabel = new Label
            {
                AutoSize = false, Anchor = AnchorStyles.Left | AnchorStyles.Right, Height = Theme.Dp(30),
                TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Font = Theme.Ui(9.5f, true),
                BackColor = Theme.Surface, Margin = Theme.Pad(0, 0, 8, 8), UseMnemonic = false
            };
            lockChoose = new FlatButton
            {
                Kind = ButtonKind.Secondary, Width = Theme.Dp(100), Height = Theme.Dp(30), Margin = Theme.Pad(0, 0, 0, 8),
                AccessibleName = "Choose the app"
            };
            Localize(() =>
            {
                lockChoose.Text = Loc.T("lock.choose");
                ShowLockApp();
            });
            lockChoose.Click += OnChooseLockApp;
            lockable.Add(lockChoose);
            Grid app = Table("100%", "auto");
            Row(app, lockAppLabel, lockChoose);
            FullRow(body, app);

            segLockMode = Seg("lock.pause", "lock.stop");
            segLockMode.AccessibleName = "When the app loses focus";
            lockable.Add(segLockMode);
            segLockMode.SelectedChanged += OnConfigChanged;
            Row(body, RowLabel("lock.onFocusLoss"), segLockMode);

            return NewCard(body);
        }

        private void ShowLockApp()
        {
            bool none = settings.LockExe.Length == 0;
            lockAppLabel.Text = none ? Loc.T("lock.none") : settings.LockName;
            lockAppLabel.ForeColor = none ? Theme.Dim : Theme.Text;
            tips.SetToolTip(lockAppLabel, none ? null : settings.LockExe);
        }

        private void OnChooseLockApp(object sender, EventArgs e)
        {
            AppWindow chosen = WindowPicker.Pick(this, Loc.T("picker.hint"), WindowLock.List());
            if (chosen == null) return;
            settings.LockExe = chosen.ExeName;
            settings.LockName = chosen.DisplayName;
            settings.Validate();
            ShowLockApp();
            if (chkLock.Checked) SaveSettings();
            else chkLock.Checked = true; // choosing an app means "use it"; saves through OnConfigChanged
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
            Row(column, BuildPositionCard());
            Row(column, BuildSettingsCard());
            Row(column, BuildFooter());
            AddFiller(column);
            return column;
        }

        private Control BuildStatusCard()
        {
            Grid body = Table("100%");

            badge = new Badge { Anchor = AnchorStyles.Left, Margin = Theme.Pad(0, 0, 0, 12) };
            FullRow(body, badge);

            Grid tiles = Table("24%", "26%", "26%", "24%");
            Row(tiles, Tile("stat.clicks", out clicksValue), Tile("stat.requested", out requestedValue),
                Tile("stat.sent", out sentValue), Tile("stat.time", out timeValue));
            tiles.Margin = Theme.Pad(0, 0, 0, 4);
            FullRow(body, tiles);

            // Honest about what is measured: what Windows accepted, not what the game recognised.
            Label sentNote = Lbl("stat.note", 8f, dim: true);
            sentNote.Margin = Theme.Pad(0, 0, 0, 10);
            sentNote.MaximumSize = new Size(Theme.Dp(300), 0);
            Localize(() => tips.SetToolTip(sentNote, Loc.T("stat.note.tip")));
            FullRow(body, sentNote);

            startButton = new FlatButton
            {
                Kind = ButtonKind.Primary, Dock = DockStyle.Fill, Height = Theme.Dp(48), Margin = Theme.Pad(0, 0, 0, 10),
                Font = Theme.Ui(12f, true), AccessibleName = "Start or stop clicking"
            };
            startButton.Click += delegate { Toggle(false); };
            FullRow(body, startButton);

            notice = new Label
            {
                AutoSize = false, AutoEllipsis = true, Height = Theme.Dp(38), Dock = DockStyle.Fill, Font = Theme.Ui(9f), ForeColor = Theme.Dim,
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
                AutoSize = true, Font = Theme.Ui(14f, true), ForeColor = Theme.Text, BackColor = Theme.Surface,
                Margin = Padding.Empty, Text = "0", UseMnemonic = false
            };
            Row(tile, caption);
            Row(tile, value);
            return tile;
        }

        /// <summary>
        /// Hotkeys and options in one card (one header and padding fewer keeps the window laptop-sized).
        /// One grid, no nesting: 33 / 17 / 17 / 33 % columns give three equal hotkey cells and two equal option cells.
        /// </summary>
        private Control BuildSettingsCard()
        {
            Grid body = Table("33%", "17%", "17%", "33%");
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
            hkToggle.Margin = hkStop.Margin = Theme.Pad(0, 0, 6, 12);
            hkCapture.Margin = Theme.Pad(0, 0, 0, 12);
            Thirds(body, HotkeyCaption("hk.toggle"), HotkeyCaption("hk.stop"), HotkeyCaption("hk.capture"));
            Thirds(body, hkToggle, hkStop, hkCapture);

            chkTop = Check("opt.top");
            chkSounds = Check("opt.sounds");
            chkTray = Check("opt.tray");
            chkHud = Check("opt.hud");
            Localize(() => tips.SetToolTip(chkHud, Loc.T("opt.hud.tip")));
            segHudCorner = new Segmented
            {
                Items = new[] { "↖", "↗", "↙", "↘" }, Size = Theme.Sz(150, 28), Anchor = AnchorStyles.Left,
                Margin = Theme.Pad(0, 0, 0, 6), AccessibleName = "Indicator corner"
            };
            segHudCorner.SelectedChanged += OnConfigChanged;
            Halves(body, chkTop, chkSounds);
            FullRow(body, chkTray);
            Halves(body, chkHud, segHudCorner);
            return NewCard(body);
        }

        private static void Thirds(TableLayoutPanel table, Control a, Control b, Control c)
        {
            Row(table, a, b, null, c);
            table.SetColumnSpan(b, 2);
        }

        private static void Halves(TableLayoutPanel table, Control a, Control b)
        {
            Row(table, a, null, b);
            table.SetColumnSpan(a, 2);
            table.SetColumnSpan(b, 2);
        }

        private Label HotkeyCaption(string key)
        {
            Label label = Lbl(key, 8.5f, dim: true);
            label.Margin = Theme.Pad(0, 0, 4, 4);
            label.AutoEllipsis = true;
            return label;
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
            trayToggle.Click += delegate { if (Enabled) Toggle(false); }; // not while a dialog of ours is open
            trayExit = new ToolStripMenuItem();
            trayExit.Click += delegate { Close(); };

            var menu = new ContextMenuStrip();
            menu.Items.AddRange(new ToolStripItem[] { trayShow, trayToggle, new ToolStripSeparator(), trayExit });

            tray = new NotifyIcon { Icon = Icon, Text = "V-Clicker", ContextMenuStrip = menu, Visible = false };
            trayClicking = WithDot(Icon, Theme.Success);
            trayPaused = WithDot(Icon, Theme.Warning);
            tray.MouseClick += (sender, e) => { if (e.Button == MouseButtons.Left) ShowFromTray(); };
        }

        /// <summary>The app icon at tray size with a colored status dot in the corner.</summary>
        internal static Icon WithDot(Icon source, Color color)
        {
            Size size = SystemInformation.SmallIconSize;
            using (var sized = new Icon(source, size))
            using (Bitmap bitmap = sized.ToBitmap())
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                using (var fill = new SolidBrush(color))
                using (var ring = new Pen(Theme.Bg, Math.Max(1f, size.Width / 12f)))
                {
                    Theme.Smooth(g);
                    float d = size.Width * 0.55f;
                    var dot = new RectangleF(size.Width - d - 0.5f, size.Height - d - 0.5f, d, d);
                    g.FillEllipse(fill, dot);
                    g.DrawEllipse(ring, dot);
                }
                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Icon borrowed = Icon.FromHandle(handle)) return (Icon)borrowed.Clone();
                }
                finally { Native.DestroyIcon(handle); }
            }
        }

        private static Icon LoadIcon()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("icon.ico"))
                return new Icon(stream);
        }

        // ------------------------------------------------------------------ settings <-> UI

        private void ApplySettingsToUi()
        {
            ShowInterval(settings.IntervalMs);
            loading = true;
            segSpeed.SetSelectedSilently(settings.SpeedInCps ? 0 : 1);
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
            chkLock.Checked = settings.WindowLock;
            segLockMode.SetSelectedSilently(settings.LockStopOnFocusLoss ? 1 : 0);
            chkHud.Checked = settings.Hud;
            segHudCorner.SetSelectedSilently((int)settings.HudCorner);
            ShowLockApp();
            loading = false;
            ShowSpeedMode();
            UpdateEnabledState();
        }

        private void ReadUi()
        {
            foreach (NumberField field in numberFields) field.Commit();
            // IntervalMs is not read back from the fields: they show it rounded. SetInterval() is the only writer.
            settings.SpeedInCps = segSpeed.SelectedIndex == 0;
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
            // LockExe / LockName are only changed by the app picker.
            settings.WindowLock = chkLock.Checked;
            settings.LockStopOnFocusLoss = segLockMode.SelectedIndex == 1;
            settings.Hud = chkHud.Checked;
            settings.HudCorner = (HudCorner)segHudCorner.SelectedIndex;
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
            SetInterval(Interval.Join(fHours.Value, fMinutes.Value, fSeconds.Value, fMillis.Number));
        }

        /// <summary>The one way the speed changes: stores it exactly and refreshes both editors.</summary>
        private void SetInterval(double totalMs)
        {
            settings.IntervalMs = Interval.Clamp(totalMs);
            ShowInterval(settings.IntervalMs);
            OnConfigChanged(this, EventArgs.Empty);
        }

        /// <summary>Puts a total interval into the CPS field and the four fields (90 s shows as 1 min 30 s).</summary>
        private void ShowInterval(double totalMs)
        {
            Interval.Split(totalMs, out int h, out int m, out int s, out double ms);
            bool wasLoading = loading;
            loading = true;
            fHours.Value = h;
            fMinutes.Value = m;
            fSeconds.Value = s;
            fMillis.Number = ms;
            double perSecond = Interval.ClicksPerSecond(totalMs);
            if (perSecond < Interval.MinCps - 1e-9)
                fCps.ShowText("< " + Interval.MinCps.ToString("0.#", CultureInfo.CurrentCulture));
            else
                fCps.Number = perSecond;
            loading = wasLoading;
        }

        private void ShowSpeedMode()
        {
            bool cps = segSpeed.SelectedIndex == 0;
            cpsRow.Visible = cps;
            intervalRow.Visible = !cps;
            UpdateRateLabel();
        }

        private void UpdateRateLabel()
        {
            double perSecond = Interval.ClicksPerSecond(settings.IntervalMs);
            string text;
            if (segSpeed.SelectedIndex == 0)
                text = perSecond < Interval.MinCps - 1e-9
                    ? Loc.T("rate.belowCps")
                    : Loc.F("rate.ms", settings.IntervalMs.ToString(settings.IntervalMs >= 100 ? "0.#" : "0.##", CultureInfo.CurrentCulture));
            else
                text = perSecond >= 1
                    ? Loc.F("rate.perSecond", perSecond.ToString(perSecond >= 10 ? "0" : "0.#", CultureInfo.CurrentCulture))
                    : Loc.F("rate.every", (1 / perSecond).ToString("0.#", CultureInfo.CurrentCulture));
            // A double click is two clicks for Windows: say so, since the speed counts actions.
            if (segType.SelectedIndex == 1)
                text += Loc.F("rate.double", FormatCps(perSecond * 2));
            rateLabel.Text = text;
        }

        internal static string FormatCps(double value) =>
            value >= 0.01
                ? value.ToString(value >= 100 ? "0" : value >= 1 ? "0.0" : "0.##", CultureInfo.CurrentCulture)
                : "< " + 0.01.ToString("0.##", CultureInfo.CurrentCulture); // a 5-min interval is not "0 CPS"

        private void UpdateEnabledState()
        {
            bool idle = engine.Phase == EnginePhase.Idle;
            foreach (Control control in lockable) control.Enabled = idle;
            fRepeat.Enabled = idle && segRepeat.SelectedIndex == 1;
            bool fixedSpot = segPosition.SelectedIndex == 1;
            fX.Enabled = fY.Enabled = idle && fixedSpot;
            captureButton.Enabled = idle && fixedSpot;
            fDelay.Enabled = idle && segMode.SelectedIndex == 0;
            segLockMode.Enabled = idle && chkLock.Checked;
            segHudCorner.Enabled = chkHud.Checked;
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

            hudArmed = true; // even a refused start shows on the indicator
            ClickPlan plan = settings.ToPlan();
            if (settings.HoldMode && fromHotkey)
            {
                plan.HoldKey = (int)settings.ToggleKey.Key;
                plan.StartDelayMs = 0;
            }

            lockedWindow = IntPtr.Zero;
            // The indicator goes on the screen where the clicks will land.
            hudScreen = Screen.FromPoint(plan.FixedPosition ?? Cursor.Position).Bounds;
            if (settings.WindowLock)
            {
                AppWindow target = FindLockTarget(fromHotkey);
                if (target == null) return; // refused (and told why) or cancelled
                if (engine.Phase != EnginePhase.Idle) return; // started meanwhile (the picker was open)
                if (plan.FixedPosition.HasValue &&
                    WindowLock.Check(target.Handle, target.ProcessId, plan.FixedPosition) == LockStatus.PointOutside)
                {
                    Refuse(Loc.F("msg.lockOutside", settings.LockName), Loc.T("hud.outside"));
                    return;
                }
                IntPtr window = target.Handle;
                uint process = target.ProcessId;
                plan.Lock = at => WindowLock.Check(window, process, at);
                plan.StopOnFocusLoss = settings.LockStopOnFocusLoss;
                lockedWindow = window;
                hudScreen = Screen.FromHandle(window).Bounds;
            }

            lastStop = null;
            sentRate.Reset();
            ShowHint();
            if (!engine.Start(plan)) return;

            UpdateEnabledState();
            UpdateStartButton();
            RefreshStats();
            Beep(true);
        }

        /// <summary>
        /// The window a Window Lock run binds to. The hotkey only accepts the locked app when it is already in
        /// front (it never switches windows); the Start button brings the app's window to the front first and
        /// asks which one when there are several. Nothing is ever armed to start later.
        /// </summary>
        /// <returns>null if the start is refused (the user is told why) or cancelled.</returns>
        private AppWindow FindLockTarget(bool fromHotkey)
        {
            string name = settings.LockName;
            if (settings.LockExe.Length == 0)
            {
                Refuse(Loc.T("msg.lockNone"), Loc.T("hud.chooseApp"));
                return null;
            }
            if (fromHotkey)
            {
                // A game's own dialog in front still counts as the game: bind to its top-level window.
                AppWindow front = WindowLock.FromHandle(WindowLock.RootOwner(WindowLock.Foreground()));
                if (front == null || !front.ExeName.Equals(settings.LockExe, StringComparison.OrdinalIgnoreCase))
                {
                    Refuse(Loc.F("msg.lockFocus", name), Loc.F("hud.switchTo", name));
                    return null;
                }
                return Accessible(front) ? front : null;
            }

            List<AppWindow> windows = WindowLock.List(settings.LockExe);
            if (windows.Count == 0)
            {
                Refuse(Loc.F("msg.lockNotOpen", name), Loc.F("hud.open", name));
                return null;
            }
            AppWindow chosen = windows.Count == 1 ? windows[0] : WindowPicker.Pick(this, Loc.F("picker.which", name), windows);
            if (chosen == null || !Accessible(chosen)) return null;
            if (!WindowLock.BringToFront(chosen.Handle))
            {
                Refuse(Loc.F("msg.lockFocusFailed", name, hkToggle.Value), Loc.F("hud.switchTo", name));
                return null;
            }
            return chosen;
        }

        /// <summary>
        /// Windows silently drops clicks a normal process sends to an elevated one, so refuse up front and say
        /// how to fix it, rather than "click" with no effect.
        /// </summary>
        private bool Accessible(AppWindow target)
        {
            if (!target.Restricted || IsElevated()) return true;
            Refuse(Loc.F("msg.lockElevated", settings.LockName), Loc.T("hud.elevated"));
            return false;
        }

        private void Refuse(string message, string hudText)
        {
            ShowNotice(message, Theme.Warning, 8);
            ErrorBeep();
            FlashHud(hudText, WindowLock.Foreground()); // where the user is looking now
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend) engine.Stop(StopReason.Suspended);
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.ConsoleDisconnect ||
                e.Reason == SessionSwitchReason.RemoteDisconnect)
                engine.Stop(StopReason.Suspended);
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
            // Queued from the engine thread: a new run may have started since. Its state wins; this stop is old news.
            if (engine.Phase != EnginePhase.Idle)
            {
                UpdateEnabledState();
                UpdateStartButton();
                return;
            }
            lastStop = reason;
            UpdateEnabledState();
            UpdateStartButton();
            string name = settings.LockName;
            string hudText = Loc.T("status.idle");
            bool error = true;
            switch (reason)
            {
                case StopReason.Completed:
                    ShowNotice(Loc.F("msg.done", engine.Clicks.ToString("N0", CultureInfo.CurrentCulture)), Theme.Success, 6);
                    hudText = Loc.T("status.done");
                    error = false;
                    break;
                case StopReason.Blocked:
                    ShowNotice(Loc.T("msg.blocked"), Theme.Danger, 0);
                    hudText = Loc.T("hud.blocked");
                    error = false; // unchanged sound for this long-standing case
                    break;
                case StopReason.FocusLost:
                    ShowNotice(Loc.F("msg.focusLost", name), Theme.Warning, 0);
                    hudText = Loc.T("hud.focusLost");
                    break;
                case StopReason.TargetClosed:
                    ShowNotice(Loc.F("msg.targetClosed", name), Theme.Warning, 0);
                    hudText = Loc.F("hud.closed", name);
                    break;
                case StopReason.PauseTimeout:
                    ShowNotice(Loc.T("msg.pauseTimeout"), Theme.Warning, 0);
                    hudText = Loc.T("hud.timeout");
                    break;
                case StopReason.Suspended:
                    ShowNotice(Loc.T("msg.suspended"), Theme.Warning, 0);
                    break;
                default:
                    error = false;
                    break;
            }
            if (error) ErrorBeep();
            else Beep(false);
            FlashHud(hudText, IntPtr.Zero); // on the run's screen: a closed window has no screen to ask
            lockedWindow = IntPtr.Zero;
            RefreshStats();
        }

        private void Beep(bool starting)
        {
            if (!settings.Sounds) return;
            uint frequency = starting ? 880u : 520u;
            uint duration = starting ? 70u : 90u;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate { Native.Beep(frequency, duration); });
        }

        /// <summary>Lower and longer than the stop beep: "this did not do what you expected".</summary>
        private void ErrorBeep()
        {
            if (!settings.Sounds) return;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate { Native.Beep(330, 220); });
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
                    // While a dialog of ours is open (the app picker) only Stop works: nothing may start under it.
                    case IdToggle: if (Enabled || engine.Phase != EnginePhase.Idle) Toggle(true); break;
                    case IdStop: engine.Stop(); break;
                    case IdCapture: if (Enabled) CapturePosition(); break;
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
            timeValue.Text = FormatElapsed(engine.SessionElapsed);
            requestedValue.Text = FormatCps(Interval.ClicksPerSecond(settings.IntervalMs));

            long now = Stopwatch.GetTimestamp();
            if (phase == EnginePhase.Clicking)
            {
                // Average over a few intervals (at least 2 s) so slow and fast rates both read steadily.
                double sent = sentRate.Sample(clicks, engine.LastClickStamp, Math.Max(2, 3 * settings.IntervalMs / 1000));
                sentValue.Text = double.IsNaN(sent) ? "\u2014" : FormatCps(sent);
            }
            else
            {
                sentRate.Reset(); // after a pause, measure the resumed clicking only
                sentValue.Text = "\u2014";
            }

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
                case EnginePhase.Paused:
                    badge.Dot = Theme.Warning;
                    badge.Text = PausedText();
                    break;
                default:
                    badge.Dot = lastStop == StopReason.Blocked ? Theme.Danger
                              : lastStop == StopReason.Completed ? Theme.Success
                              : lastStop.HasValue && lastStop.Value >= StopReason.FocusLost ? Theme.Warning : Theme.Dim;
                    badge.Text = lastStop == StopReason.Blocked ? Loc.T("status.blocked")
                               : lastStop == StopReason.Completed ? Loc.T("status.done")
                               : Loc.T("status.idle");
                    break;
            }
            Size badgeSize = badge.GetPreferredSize(Size.Empty);
            badge.Size = new Size(Math.Min(badgeSize.Width, Theme.Dp(300)), badgeSize.Height); // long app names end in "…"
            UpdateTray(phase);
            UpdateHud(phase);

            if (noticeExpires != 0 && now > noticeExpires) ShowHint();
        }

        private string PausedText(bool brief = false) =>
            engine.PauseCause == LockStatus.PointOutside ? Loc.T(brief ? "hud.outside" : "status.outside")
            : Loc.F(brief ? "hud.waiting" : "status.waiting", settings.LockName);

        private void UpdateTray(EnginePhase phase)
        {
            Icon icon = phase == EnginePhase.Clicking ? trayClicking
                      : phase == EnginePhase.Idle ? Icon : trayPaused;
            if (tray.Icon != icon) tray.Icon = icon;
            string text = phase == EnginePhase.Idle ? "V-Clicker" : "V-Clicker - " + badge.Text;
            if (text.Length > 63) text = text.Substring(0, 62) + "\u2026"; // NotifyIcon tooltip limit
            if (tray.Text != text) tray.Text = text;
        }

        /// <summary>Shows a stop or a refusal on the indicator for ~2 s.</summary>
        private void FlashHud(string text, IntPtr screenOf)
        {
            hudFlashText = text;
            hudFlashUntil = Stopwatch.GetTimestamp() + 2 * Stopwatch.Frequency;
            if (screenOf != IntPtr.Zero) hudScreen = Screen.FromHandle(screenOf).Bounds;
            UpdateHud(engine.Phase);
        }

        private void UpdateHud(EnginePhase phase)
        {
            if (!hudArmed || !settings.Hud)
            {
                hud?.Hide();
                return;
            }
            Color dot;
            string text;
            switch (phase)
            {
                case EnginePhase.Clicking:
                    dot = Theme.Success;
                    text = FormatCps(Interval.ClicksPerSecond(settings.IntervalMs)) + " CPS";
                    break;
                case EnginePhase.Countdown:
                    dot = Theme.Warning;
                    text = Loc.F("hud.starting", engine.CountdownRemaining.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture) + " s");
                    break;
                case EnginePhase.Paused:
                    dot = Theme.Warning;
                    text = PausedText(brief: true);
                    break;
                default:
                    if (hudFlashText == null || Stopwatch.GetTimestamp() > hudFlashUntil)
                    {
                        hudFlashText = null;
                        hud?.Hide();
                        return;
                    }
                    dot = Theme.Dim;
                    text = hudFlashText;
                    break;
            }
            if (hud == null || hud.IsDisposed) hud = new Hud();
            hud.Display(dot, text, hudScreen.IsEmpty ? Screen.PrimaryScreen.Bounds : hudScreen, settings.HudCorner);
            if (++hudTicks % 10 == 0) hud.KeepOnTop();
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
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                SystemEvents.SessionSwitch -= OnSessionSwitch;
                uiTimer.Dispose();
                captureTimer.Dispose();
                tips.Dispose();
                tray.Visible = false;
                tray.Dispose();
                trayClicking.Dispose();
                trayPaused.Dispose();
                hud?.Dispose();
                engine.Dispose();
            }
            base.Dispose(disposing);
        }

        // ------------------------------------------------------------------ misc

        internal static int ColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

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
