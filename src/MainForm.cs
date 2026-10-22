using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
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
        private const string MakerName = "Vemryx", MakerUrl = "https://vemryx.com";
        private const string LeftLabels = "150", RightLabels = "116";
        private const int RowHeight = 32; // clusters and fields in the settings rows
        private static readonly Padding RowGap = Theme.Pad(0, 0, 0, 6);

        private readonly string settingsPath, profileFolder;
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

        private StatusDisplay display;
        private Label rateLabel;
        private KeyButton startButton, captureButton;
        private NumberField fCps, fHours, fMinutes, fSeconds, fMillis, fJitter, fRepeat, fX, fY, fDelay;
        private Segmented segLanguage, segSpeed, segButton, segType, segRepeat, segPosition, segMode;
        private Control cpsRow, intervalRow, jitterRow, quickButtonRow, rightColumn;
        private Grid root;
        private Segmented segLevel, segQuickButton;
        private Label alsoNote;
        private LinkLabel profilesLink;
        private HotkeyBox hkToggle, hkStop, hkCapture;
        private LedCheck chkTop, chkTray, chkSounds, chkLock, chkHud, chkDev;
        private Segmented segLockMode, segHudCorner;
        private Label lockAppLabel;
        private KeyButton lockChoose;
        private LinkLabel adminLink;
        private KeyButton studioButton;
        private MacroStudio studio;

        // Developer Mode (docs/superpowers/specs/2026-10-21-developer-mode-design.md): the log of the clicker's run, and
        // which run a command line started (0 = none, 1 = the clicker, 2 = a macro).
        private RunLog clickLog;
        private int commandRun;
        // The command's --log file: for its own run only, cleared when the command ends.
        private string commandLogPath;
        private bool closing;

        /// <summary>What the command line asked for (null = nothing).</summary>
        internal CommandLine Command;
        /// <summary>The process exit code (a command's result).</summary>
        internal int ExitCode;

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
            profileFolder = Profiles.Folder(settingsPath);
            engine = new ClickEngine(sink);
            settings = ClickSettings.Load(settingsPath);
            Loc.SetLanguage(settings.Language);

            Text = "V-Clicker";
            Icon = LoadIcon();
            AutoScaleMode = AutoScaleMode.None; // pixel metrics are scaled by Theme.Dp() instead
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = Theme.Pad(16);
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
            ApplyLevel();
            ApplyWindowOptions();
            uiTimer.Start();
        }

        internal ClickEngine Engine => engine;

        internal ClickSettings Settings => settings;

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
                BackColor = Theme.Bg
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

        private void Localize(Action apply)
        {
            localizers.Add(apply);
            apply();
        }

        private Label Lbl(string key, float size = 9.75f, bool dim = false)
        {
            var label = new Label
            {
                AutoSize = true,
                Font = Theme.Legend(size),
                ForeColor = dim ? Theme.Dim : Theme.Text,
                BackColor = Theme.Bg,
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

        /// <summary>The silkscreen label that opens a group of keys.</summary>
        private Section Heading(string key)
        {
            var section = new Section { Dock = DockStyle.Fill, Margin = Theme.Pad(0, 0, 0, 4) };
            Localize(() => section.Text = Loc.T(key));
            return section;
        }

        private Segmented Seg(params string[] keys)
        {
            var segmented = new Segmented { Dock = DockStyle.Fill, Height = Theme.Dp(RowHeight), Margin = RowGap };
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
                Margin = RowGap,
                Height = Theme.Dp(RowHeight)
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

        /// <summary>
        /// Header across the top; below it the instrument on the left (display, Start key, speed, hotkeys) and the
        /// settings clusters on the right; the footer across the bottom.
        /// </summary>
        private void BuildLayout()
        {
            root = new Grid
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 3,
                Margin = Padding.Empty,
                BackColor = Theme.Bg
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.Dp(372)));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.Dp(380)));
            for (int i = 0; i < 3; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Control header = BuildHeader();
            header.TabIndex = 10; // the settings come first in the tab order; the Macro Studio button after them
            root.Controls.Add(header, 0, 0);
            root.SetColumnSpan(header, 2);
            root.Controls.Add(BuildLeftColumn(), 0, 1);
            rightColumn = BuildRightColumn();
            rightColumn.Margin = Theme.Pad(28, 0, 0, 0);
            root.Controls.Add(rightColumn, 1, 1);
            Control footer = BuildFooter();
            root.Controls.Add(footer, 0, 2);
            root.SetColumnSpan(footer, 2);

            // Not docked (the form sizes itself to it), so the padding has to be applied by hand on the top-left side.
            root.Location = new Point(Padding.Left, Padding.Top);
            Controls.Add(root);
        }

        private Control BuildHeader()
        {
            Image logo;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("icon-256.png"))
            using (var decoded = new Bitmap(stream))
                logo = new Bitmap(decoded); // GDI+ needs the source stream open for the life of a stream-backed Bitmap
            Disposed += delegate { logo.Dispose(); };

            var header = Table("auto", "100%", "auto", "auto");
            header.Margin = Theme.Pad(0, 0, 0, 12);

            var mark = new LogoBox(logo) { Size = Theme.Sz(40, 40), Margin = Theme.Pad(0, 0, 12, 0) };

            var titles = Table("100%");
            titles.Anchor = AnchorStyles.Left;
            titles.Dock = DockStyle.None;
            var title = new Label
            {
                Text = "V-Clicker", AutoSize = true, Font = Theme.Legend(17f, bold: true), ForeColor = Theme.Text,
                BackColor = Theme.Bg, Margin = Padding.Empty, UseMnemonic = false
            };
            var subtitle = new Label
            {
                AutoSize = true, Font = Theme.Ui(9f), ForeColor = Theme.Dim, BackColor = Theme.Bg,
                Margin = Padding.Empty, UseMnemonic = false
            };
            Localize(() => subtitle.Text = Loc.T("subtitle"));
            Row(titles, title);
            Row(titles, subtitle);

            segLanguage = new Segmented
            {
                Items = new[] { "PT", "EN" }, Size = Theme.Sz(96, 34), Anchor = AnchorStyles.Right,
                Margin = Padding.Empty
            };
            segLanguage.AccessibleName = "Language";

            studioButton = new KeyButton
            {
                Kind = ButtonKind.Secondary, Size = Theme.Sz(136, 36), Anchor = AnchorStyles.Right, Margin = Theme.Pad(0, 0, 10, 0)
            };
            Localize(() =>
            {
                studioButton.Text = Loc.T("studio.open");
                tips.SetToolTip(studioButton, Loc.T("studio.open.tip"));
            });
            studioButton.Click += delegate { OpenStudio(); };

            Row(header, mark, titles, studioButton, segLanguage);
            return header;
        }

        private Control BuildLeftColumn()
        {
            Grid column = Table("100%");

            // Two levels (docs/superpowers/specs/2026-10-21-adaptive-interface-design.md), always in this place.
            segLevel = Seg("level.simple", "level.advanced");
            segLevel.AccessibleName = "Interface level";
            Localize(() => tips.SetToolTip(segLevel, Loc.T("level.tip")));
            segLevel.SelectedChanged += delegate
            {
                if (loading) return;
                settings.Advanced = segLevel.SelectedIndex == 1;
                SaveSettings();
                ApplyLevel();
            };
            FullRow(column, segLevel);

            display = new StatusDisplay { Dock = DockStyle.Fill, Height = Theme.Dp(164), Margin = Theme.Pad(0, 0, 0, 12) };
            Localize(() => tips.SetToolTip(display, Loc.T("stat.note.tip")));
            Row(column, display);

            startButton = new KeyButton
            {
                Kind = ButtonKind.Primary, Dock = DockStyle.Fill, Height = Theme.Dp(60), Margin = Theme.Pad(0, 0, 0, 8),
                Font = Theme.Legend(13f, bold: true)
            };
            startButton.Click += delegate { Toggle(false); };
            Row(column, startButton);

            BuildSpeed(column);

            // Simple mode only: the mouse button, the same setting as the advanced "Button" keys.
            Grid quick = Table(LeftLabels, "100%");
            segQuickButton = Seg("button.left", "button.middle", "button.right");
            segQuickButton.AccessibleName = "Mouse button (simple)";
            lockable.Add(segQuickButton);
            segQuickButton.SelectedChanged += delegate
            {
                segButton.SetSelectedSilently(segQuickButton.SelectedIndex);
                OnConfigChanged(this, EventArgs.Empty);
            };
            Row(quick, RowLabel("quick.button"), segQuickButton);
            quickButtonRow = quick;
            FullRow(column, quick);

            BuildHotkeys(column);

            // Simple mode only: what the advanced settings still do while they are out of sight.
            alsoNote = new Label
            {
                AutoSize = true, Font = Theme.Ui(9f), ForeColor = Theme.Dim, BackColor = Theme.Bg, UseMnemonic = false,
                MaximumSize = new Size(Theme.Dp(372), 0), Margin = Theme.Pad(0, 6, 0, 0)
            };
            FullRow(column, alsoNote);
            AddFiller(column);
            return column;
        }

        private Control BuildRightColumn()
        {
            Grid column = Table("100%");
            BuildClick(column);
            BuildPosition(column);
            BuildLock(column);
            BuildOptions(column);
            AddFiller(column);
            return column;
        }

        /// <summary>Empty last row that soaks up spare height so the rows above keep their natural size.</summary>
        private static void AddFiller(TableLayoutPanel column)
        {
            column.RowCount++;
            column.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        }

        private void BuildSpeed(TableLayoutPanel column)
        {
            FullRow(column, Heading("card.interval"));

            // One speed, two ways to edit it: clicks per second (default) or the classic h / min / s / ms.
            segSpeed = Seg("speed.cps", "speed.interval");
            segSpeed.AccessibleName = "Speed unit";
            lockable.Add(segSpeed);
            segSpeed.SelectedChanged += delegate { ShowSpeedMode(); OnConfigChanged(this, EventArgs.Empty); };
            FullRow(column, segSpeed);

            Grid cps = Table(LeftLabels, "100%");
            fCps = Num(Interval.MinCps, Interval.MaxCps, "Clicks per second", "CPS");
            fCps.Decimals = 1;
            fCps.Anchor = AnchorStyles.Left;
            fCps.Dock = DockStyle.None;
            fCps.Size = Theme.Sz(150, RowHeight);
            fCps.ValueChanged += delegate { if (!loading) SetInterval(Interval.FromCps(fCps.Number)); };
            Row(cps, RowLabel("speed.perSecond"), fCps);
            cpsRow = cps;
            FullRow(column, cps);

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
            FullRow(column, fields);

            // The number row: one key per common speed.
            int[] presets = { 5, 10, 15, 20, 50 };
            Grid row = Table("20%", "20%", "20%", "20%", "20%");
            row.Margin = Theme.Pad(0, 2, 0, 4);
            var keys = new Control[presets.Length];
            for (int i = 0; i < presets.Length; i++)
            {
                int perSecond = presets[i];
                var key = new KeyButton
                {
                    Kind = ButtonKind.Chip, Text = perSecond.ToString(CultureInfo.InvariantCulture), SubLegend = "CPS",
                    Dock = DockStyle.Fill, Height = Theme.Dp(42), Margin = Theme.Pad(0, 0, i == presets.Length - 1 ? 0 : 6, 0),
                    Font = Theme.Legend(12f, bold: true)
                };
                key.AccessibleName = perSecond + " CPS";
                key.Click += delegate { SetInterval(1000.0 / perSecond); };
                keys[i] = key;
                lockable.Add(key);
            }
            Row(row, keys);
            FullRow(column, row);

            rateLabel = new Label
            {
                AutoSize = true, Font = Theme.Ui(9f), ForeColor = Theme.Dim, BackColor = Theme.Bg,
                Margin = Theme.Pad(0, 2, 0, 6), UseMnemonic = false
            };
            FullRow(column, rateLabel);

            Grid jitter = Table(LeftLabels, "100%");
            fJitter = Num(0, 50, "Random variation percent", "%");
            fJitter.Anchor = AnchorStyles.Left;
            fJitter.Dock = DockStyle.None;
            fJitter.Size = Theme.Sz(110, RowHeight);
            fJitter.ValueChanged += OnConfigChanged;
            Label jitterLabel = RowLabel("jitter");
            Row(jitter, jitterLabel, fJitter);
            Localize(() => tips.SetToolTip(fJitter, Loc.T("jitter.tip")));
            Localize(() => tips.SetToolTip(jitterLabel, Loc.T("jitter.tip")));
            jitterRow = jitter;
            FullRow(column, jitter);
        }

        private Label UnitLabel(string key)
        {
            Label label = Lbl(key, 8.5f, dim: true);
            label.Anchor = AnchorStyles.None;
            label.Margin = Theme.Pad(0, 0, 8, 0);
            return label;
        }

        private void BuildHotkeys(TableLayoutPanel column)
        {
            FullRow(column, Heading("card.hotkeys"));
            hkToggle = Hk(IdToggle);
            hkStop = Hk(IdStop);
            hkCapture = Hk(IdCapture);
            hkToggle.AccessibleName = "Start or stop hotkey";
            hkStop.AccessibleName = "Stop hotkey";
            hkCapture.AccessibleName = "Capture position hotkey";
            hkToggle.ValueChanged += OnHotkeyChanged;
            hkStop.ValueChanged += OnHotkeyChanged;
            hkCapture.ValueChanged += OnHotkeyChanged;
            hkToggle.Margin = hkStop.Margin = Theme.Pad(0, 0, 8, 4);
            hkCapture.Margin = Theme.Pad(0, 0, 0, 4);

            Grid keys = Table("33%", "34%", "33%");
            Row(keys, HotkeyCaption("hk.toggle"), HotkeyCaption("hk.stop"), HotkeyCaption("hk.capture"));
            Row(keys, hkToggle, hkStop, hkCapture);
            FullRow(column, keys);
        }

        private Label HotkeyCaption(string key)
        {
            Label label = Lbl(key, 8.75f, dim: true);
            label.Margin = Theme.Pad(0, 0, 4, 4);
            label.AutoEllipsis = true;
            return label;
        }

        private void BuildClick(TableLayoutPanel column)
        {
            FullRow(column, Heading("card.click"));
            Grid body = Table(RightLabels, "100%");

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

            Grid repeat = Table("62%", "38%");
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
            fDelay.Size = Theme.Sz(110, RowHeight);
            fDelay.ValueChanged += OnConfigChanged;
            Label delayLabel = RowLabel("delay");
            Row(body, delayLabel, fDelay);
            Localize(() => tips.SetToolTip(fDelay, Loc.T("delay.tip")));
            Localize(() => tips.SetToolTip(delayLabel, Loc.T("delay.tip")));

            FullRow(column, body);
        }

        private void BuildPosition(TableLayoutPanel column)
        {
            FullRow(column, Heading("card.position"));

            segPosition = Seg("pos.cursor", "pos.fixed");
            segPosition.AccessibleName = "Click position";
            lockable.Add(segPosition);
            segPosition.SelectedChanged += OnConfigChanged;
            FullRow(column, segPosition);

            fX = Num(-32768, 32767, "Position X");
            fY = Num(-32768, 32767, "Position Y");
            fX.ValueChanged += OnConfigChanged;
            fY.ValueChanged += OnConfigChanged;
            fX.Margin = fY.Margin = Theme.Pad(0, 0, 12, 8);

            captureButton = new KeyButton
            {
                Kind = ButtonKind.Secondary, Dock = DockStyle.Fill, Margin = Theme.Pad(0, 0, 0, 8), Width = Theme.Dp(110), Height = Theme.Dp(36)
            };
            captureButton.Click += OnCaptureButton;
            lockable.Add(captureButton);

            Grid position = Table("auto", "50%", "auto", "50%", "auto");
            Row(position, Axis("X"), fX, Axis("Y"), fY, captureButton);
            FullRow(column, position);
        }

        private void BuildLock(TableLayoutPanel column)
        {
            FullRow(column, Heading("card.lock"));
            // Two rows (switch + Choose, app + focus-loss mode), each its own grid so the wide mode keys never narrow the switch.
            Grid top = Table("100%", "auto");
            Grid bottom = Table(RightLabels, "100%"); // the mode keys start on the same line as the click keys above

            chkLock = Check("lock.enable");
            chkLock.AccessibleName = "Window Lock";
            lockable.Add(chkLock);
            Localize(() => tips.SetToolTip(chkLock, Loc.T("lock.tip")));

            lockAppLabel = new Label
            {
                AutoSize = false, Anchor = AnchorStyles.Left | AnchorStyles.Right, Height = Theme.Dp(22), // one line: a long name ends in "…" (the tooltip has the exe)
                TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Font = Theme.Legend(9.75f, bold: true),
                BackColor = Theme.Bg, Margin = Theme.Pad(0, 0, 10, 8), UseMnemonic = false
            };
            lockChoose = new KeyButton
            {
                Kind = ButtonKind.Secondary, Width = Theme.Dp(110), Height = Theme.Dp(36), Margin = Theme.Pad(0, 0, 0, 8),
                AccessibleName = "Choose the app"
            };
            Localize(() =>
            {
                lockChoose.Text = Loc.T("lock.choose");
                ShowLockApp();
            });
            lockChoose.Click += OnChooseLockApp;
            lockable.Add(lockChoose);
            chkLock.Anchor = AnchorStyles.Left;
            lockChoose.Anchor = AnchorStyles.Right;
            Row(top, chkLock, lockChoose);

            segLockMode = Seg("lock.pause", "lock.stop");
            segLockMode.AccessibleName = "When the app loses focus";
            lockable.Add(segLockMode);
            segLockMode.SelectedChanged += OnConfigChanged;
            Localize(() => tips.SetToolTip(segLockMode, Loc.T("lock.onFocusLoss")));
            Row(bottom, lockAppLabel, segLockMode);

            FullRow(column, top);
            FullRow(column, bottom);
        }

        private void ShowLockApp()
        {
            bool none = settings.LockExe.Length == 0;
            lockAppLabel.Text = none ? Loc.T("lock.none") : settings.LockName;
            lockAppLabel.ForeColor = none ? Theme.Dim : Theme.Text;
            lockAppLabel.Font = Theme.Legend(9.75f, bold: !none);
            tips.SetToolTip(lockAppLabel, none ? null : settings.LockExe);
        }

        private void OnChooseLockApp(object sender, EventArgs e)
        {
            AppWindow chosen = WindowPicker.Pick(this, Loc.T("picker.hint"), WindowLock.List());
            if (chosen != null) UseLockApp(chosen);
        }

        /// <summary>The Window Lock app chosen: its profile, if it has one, replaces the click settings.</summary>
        internal void UseLockApp(AppWindow chosen)
        {
            if (engine.Phase != EnginePhase.Idle) return; // a profile never changes during a run
            settings.LockExe = chosen.ExeName;
            settings.LockName = chosen.DisplayName;
            settings.Validate();
            ShowLockApp();
            if (!chkLock.Checked)
            {
                chkLock.Checked = true; // choosing an app means "use it"; OnConfigChanged loads its profile and saves
                return;
            }
            LoadProfile();
            SaveSettings();
            SaveProfile();
        }

        /// <summary>The locked app's profile replaces the click settings (only when stopped; none = nothing changes).</summary>
        private void LoadProfile()
        {
            if (engine.Phase != EnginePhase.Idle || settings.LockExe.Length == 0) return;
            AppProfile profile = Profiles.Load(profileFolder, settings.LockExe);
            if (profile == null) return;
            Profiles.CopyFields(profile.Settings, settings);
            ApplySettingsToUi();
            ShowNotice(Loc.F("msg.profileLoaded", settings.LockName, Profiles.Summary(settings)), Theme.Success, 6);
        }

        /// <summary>With the Window Lock on, the click settings are also the locked app's profile.</summary>
        private void SaveProfile()
        {
            if (!settings.WindowLock || settings.LockExe.Length == 0) return;
            var profile = new AppProfile { ExeName = settings.LockExe, Name = settings.LockName, UsedUtc = DateTime.UtcNow };
            Profiles.CopyFields(settings, profile.Settings);
            Profiles.Save(profileFolder, profile); // same folder as the settings: SaveSettings already reports a failure
        }

        /// <summary>An imported profile for the locked app takes effect now if nothing runs (else when the app is chosen again).</summary>
        internal void ProfileImported(AppProfile profile)
        {
            if (!settings.WindowLock || !profile.ExeName.Equals(settings.LockExe, StringComparison.OrdinalIgnoreCase) ||
                engine.Phase != EnginePhase.Idle) return;
            Profiles.CopyFields(profile.Settings, settings);
            ApplySettingsToUi();
            SaveSettings();
        }

        private void OpenProfiles()
        {
            using (var dialog = new ProfilesDialog(profileFolder, MacroFile.Folder(settingsPath), this)) dialog.ShowDialog(this);
            if (studio != null && !studio.IsDisposed) studio.ReloadMacroList(); // imported macros
        }

        private static Label Axis(string text) => new Label
        {
            Text = text, AutoSize = true, Font = Theme.Legend(10f, bold: true), ForeColor = Theme.Dim,
            BackColor = Theme.Bg, Anchor = AnchorStyles.Left, Margin = Theme.Pad(0, 0, 8, 8), UseMnemonic = false
        };

        /// <summary>Window options as lock keys, two per row; the indicator's corner beside its switch.</summary>
        private void BuildOptions(TableLayoutPanel column)
        {
            FullRow(column, Heading("card.options"));
            Grid body = Table("50%", "50%");

            chkTop = Check("opt.top");
            chkSounds = Check("opt.sounds");
            chkTray = Check("opt.tray");
            chkHud = Check("opt.hud");
            Localize(() => tips.SetToolTip(chkHud, Loc.T("opt.hud.tip")));
            segHudCorner = new Segmented
            {
                Glyphs = new[] { Glyph.CornerTopLeft, Glyph.CornerTopRight, Glyph.CornerBottomLeft, Glyph.CornerBottomRight },
                Size = Theme.Sz(150, 34), Anchor = AnchorStyles.Right, Margin = Theme.Pad(0, 0, 0, 6), AccessibleName = "Indicator corner"
            };
            Localize(() => segHudCorner.Items = new[] { Loc.T("corner.0"), Loc.T("corner.1"), Loc.T("corner.2"), Loc.T("corner.3") });
            segHudCorner.SelectedChanged += OnConfigChanged;

            chkDev = Check("opt.dev");
            Localize(() => tips.SetToolTip(chkDev, Loc.T("opt.dev.tip")));

            Row(body, chkTop, chkSounds);
            FullRow(body, chkTray);
            Row(body, chkHud, segHudCorner);
            FullRow(body, chkDev);
            FullRow(column, body);
        }

        private LedCheck Check(string key)
        {
            var check = new LedCheck { Margin = Theme.Pad(0, 0, 0, 6), Anchor = AnchorStyles.Left };
            Localize(() => check.Text = Loc.T(key));
            check.CheckedChanged += OnConfigChanged;
            return check;
        }

        private Control BuildFooter()
        {
            Grid footer = Table("auto", "100%", "auto");
            footer.Margin = Theme.Pad(0, 8, 0, 0);

            // "Made by Vemryx  ·  GitHub  ·  v1.0.0": the maker's name and GitHub are links, the rest is plain.
            var credits = new LinkLabel
            {
                AutoSize = true, Font = Theme.Legend(9f), BackColor = Theme.Bg, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Dim, UseCompatibleTextRendering = true,
                LinkColor = Theme.Text, ActiveLinkColor = Theme.Cap, VisitedLinkColor = Theme.Text,
                LinkBehavior = LinkBehavior.HoverUnderline, Margin = Padding.Empty
            };
            Localize(() =>
            {
                string maker = Loc.F("madeBy", MakerName);
                credits.Text = maker + "  ·  GitHub  ·  v" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
                credits.Links.Clear();
                credits.Links.Add(maker.IndexOf(MakerName, StringComparison.Ordinal), MakerName.Length, MakerUrl);
                credits.Links.Add(credits.Text.IndexOf("GitHub", StringComparison.Ordinal), "GitHub".Length, RepoUrl);
                tips.SetToolTip(credits, MakerUrl.Replace("https://", string.Empty));
            });
            credits.LinkClicked += (sender, e) => OpenUrl((string)e.Link.LinkData);

            adminLink = new LinkLabel
            {
                AutoSize = true, Font = Theme.Legend(9f), BackColor = Theme.Bg, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, UseCompatibleTextRendering = true, // both links measured the same way (a split link label uses GDI+)
                LinkColor = Theme.Text, ActiveLinkColor = Theme.Cap, VisitedLinkColor = Theme.Text,
                LinkBehavior = LinkBehavior.HoverUnderline, Margin = Padding.Empty
            };
            adminLink.LinkClicked += delegate { RestartAsAdministrator(); };
            if (IsElevated())
            {
                adminLink.Enabled = false;
                adminLink.LinkColor = adminLink.DisabledLinkColor = Theme.Success;
                Localize(() => adminLink.Text = Loc.T("admin.on"));
            }
            else
            {
                Localize(() =>
                {
                    adminLink.Text = Loc.T("link.admin");
                    tips.SetToolTip(adminLink, Loc.T("link.admin.tip"));
                });
            }

            profilesLink = new LinkLabel
            {
                AutoSize = true, Font = Theme.Legend(9f), BackColor = Theme.Bg, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, UseCompatibleTextRendering = true,
                LinkColor = Theme.Text, ActiveLinkColor = Theme.Cap, VisitedLinkColor = Theme.Text,
                LinkBehavior = LinkBehavior.HoverUnderline, Margin = Padding.Empty
            };
            Localize(() =>
            {
                profilesLink.Text = Loc.T("link.profiles");
                tips.SetToolTip(profilesLink, Loc.T("link.profiles.tip"));
            });
            profilesLink.LinkClicked += delegate { OpenProfiles(); };
            lockable.Add(profilesLink); // an import during a run could not take effect, and the run's settings would overwrite it
            adminLink.Margin = Theme.Pad(18, 0, 0, 0);

            Row(footer, credits, profilesLink, adminLink);
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

            var menu = new ContextMenuStrip { Renderer = new KeyMenuRenderer(), Font = Theme.Ui(9.5f), ShowImageMargin = false };
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
            segQuickButton.SetSelectedSilently((int)settings.Button);
            segLevel.SetSelectedSilently(settings.Advanced ? 1 : 0);
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
            chkDev.Checked = settings.DeveloperMode;
            segHudCorner.SetSelectedSilently((int)settings.HudCorner);
            ShowLockApp();
            loading = false;
            ShowSpeedMode();
            UpdateEnabledState();
            UpdateAlsoNote();
        }

        /// <summary>
        /// Simple: one column (speed, mouse button, hotkeys). Advanced: everything. Hiding a control never changes what
        /// it does: the settings stay in effect and <see cref="UpdateAlsoNote"/> says so.
        /// </summary>
        private void ApplyLevel()
        {
            bool advanced = settings.Advanced;
            SuspendLayout();
            rightColumn.Visible = advanced;
            root.ColumnStyles[1].Width = advanced ? Theme.Dp(380) : 0;
            // "Restart as admin" stays: refusals for an elevated app name it.
            studioButton.Visible = segSpeed.Visible = jitterRow.Visible = profilesLink.Visible = advanced;
            quickButtonRow.Visible = !advanced;
            segQuickButton.SetSelectedSilently((int)settings.Button);
            ShowSpeedMode();
            UpdateAlsoNote();
            ResumeLayout(true);
            if (!AutoSize)
            {
                // FitToScreen capped the previous layout: size to the new content, then fit again.
                AutoScroll = false;
                AutoSize = true;
            }
            if (Visible) FitToScreen(Screen.FromControl(this).WorkingArea);
        }

        /// <summary>Simple mode: the advanced settings that are not at their defaults, in a few words.</summary>
        private void UpdateAlsoNote()
        {
            var active = new List<string>();
            if (settings.WindowLock) active.Add(Loc.F("also.lock", settings.LockExe.Length > 0 ? settings.LockName : Loc.T("lock.none").ToLowerInvariant()));
            if (settings.UseFixedPosition) active.Add(Loc.F("also.fixed", settings.X, settings.Y));
            if (settings.RepeatCount > 0) active.Add(Loc.F("also.repeat", settings.RepeatCount.ToString("N0", CultureInfo.CurrentCulture)));
            if (settings.DoubleClick) active.Add(Loc.T("also.double"));
            if (settings.HoldMode) active.Add(Loc.T("also.hold"));
            if (settings.JitterPercent > 0) active.Add(Loc.F("also.jitter", settings.JitterPercent));
            if (settings.StartDelaySeconds > 1) active.Add(Loc.F("also.delay", settings.StartDelaySeconds));
            alsoNote.Text = Loc.F("also.intro", string.Join(" · ", active));
            alsoNote.Visible = !settings.Advanced && active.Count > 0;
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
            settings.DeveloperMode = chkDev.Checked;
            settings.HudCorner = (HudCorner)segHudCorner.SelectedIndex;
        }

        internal void SaveSettings()
        {
            if (settings.Save(settingsPath) || saveWarned) return;
            saveWarned = true;
            ShowNotice(Loc.T("msg.saveFailed"), Theme.Warning, 8);
        }

        private void OnConfigChanged(object sender, EventArgs e)
        {
            if (loading) return;
            bool wasLocked = settings.WindowLock;
            ReadUi();
            if (!wasLocked && settings.WindowLock) LoadProfile(); // turning the lock on picks the app's profile up
            SaveSettings();
            SaveProfile();
            UpdateRateLabel();
            UpdateEnabledState();
            ApplyWindowOptions();
            UpdateStartButton();
            UpdateAlsoNote();
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

        /// <summary>The speed is edited as CPS (the simple window always does).</summary>
        private bool CpsView => !settings.Advanced || segSpeed.SelectedIndex == 0;

        private void ShowSpeedMode()
        {
            bool cps = CpsView;
            cpsRow.Visible = cps;
            intervalRow.Visible = !cps;
            UpdateRateLabel();
        }

        private void UpdateRateLabel()
        {
            double perSecond = Interval.ClicksPerSecond(settings.IntervalMs);
            string text;
            if (CpsView)
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
            UpdateAlsoNote();
            ShowHint();
            RefreshStats();
            PerformLayout();
            Invalidate(true);
        }

        private void UpdateStartButton()
        {
            bool running = engine.Phase != EnginePhase.Idle;
            // While a run is on, the key stays down and turns into the stop key.
            startButton.Kind = running ? ButtonKind.Danger : ButtonKind.Primary;
            startButton.Latched = running;
            startButton.Text = Loc.T(running ? "stop" : "start");
            startButton.SubLegend = hkToggle.Value.ToString();
            startButton.AccessibleName = startButton.Text + " (" + hkToggle.Value + ")";
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
            if (studio != null && !studio.IsDisposed) studio.ApplyLanguage();
        }

        // ------------------------------------------------------------------ macro studio

        internal MacroStudio OpenStudio()
        {
            if (studio == null || studio.IsDisposed)
            {
                studio = new MacroStudio(this, settings, settingsPath);
                studio.FormClosed += delegate { studio = null; };
                studio.Show();
            }
            else
            {
                if (studio.WindowState == FormWindowState.Minimized) studio.WindowState = FormWindowState.Normal;
                studio.Activate();
            }
            return studio;
        }

        private bool StudioBusy => studio != null && !studio.IsDisposed && studio.Busy;

        // ------------------------------------------------------------------ developer mode

        /// <summary>Where this run is logged: the command's --log file, else Developer Mode's daily file; null = nowhere.</summary>
        internal RunLog OpenLog()
        {
            string path = commandLogPath ?? (settings.DeveloperMode ? RunLog.DailyPath(settingsPath) : null);
            return path == null ? null : new RunLog(path);
        }

        /// <summary>
        /// Does what the command line asked, the way the Start or Run button would (every safeguard included).
        /// </summary>
        /// <returns>The exit code if the command ended here; -1 while its run goes on.</returns>
        internal int RunCommand(CommandLine command)
        {
            Command = command;
            commandLogPath = command.LogPath;
            if (command.Profile != null)
            {
                AppProfile profile = Profiles.List(profileFolder).FirstOrDefault(p =>
                    p.ExeName.Equals(command.Profile, StringComparison.OrdinalIgnoreCase) || p.Name.Equals(command.Profile, StringComparison.OrdinalIgnoreCase));
                if (profile == null) return CommandFailed(ExitCodes.BadArguments, Loc.F("cli.noProfile", command.Profile));
                UseLockApp(new AppWindow { ExeName = profile.ExeName, DisplayName = profile.Name });
                StartClicking(false);
                if (engine.Phase == EnginePhase.Idle) return CommandFailed(ExitCodes.NotStarted, null); // the display says why
                commandRun = 1;
                return -1;
            }
            string file = MacroPath(command.Macro);
            if (file == null) return CommandFailed(ExitCodes.BadArguments, Loc.F("cli.noMacro", command.Macro));
            Macro macro;
            try { macro = MacroFile.Load(file); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is FormatException)
            {
                return CommandFailed(ExitCodes.BadArguments, Loc.F("mmsg.openFailed", Path.GetFileName(file)));
            }
            List<string> missing = MacroVariables.Missing(macro, command.Vars);
            if (missing.Count > 0) return CommandFailed(ExitCodes.BadArguments, Loc.F("cli.noValue", string.Join(", ", missing)));
            MacroStudio opened = OpenStudio();
            opened.Open(macro, file);
            opened.CommandValues = command.Vars;
            opened.BeginRun();
            opened.CommandValues = null; // a refused start must not hand them to a later run
            if (!opened.Busy) return CommandFailed(ExitCodes.NotStarted, null); // the Studio says why
            commandRun = 2;
            return -1;
        }

        /// <summary>A file path, or the name of a macro in the Macros folder; null = no such file.</summary>
        private string MacroPath(string macro)
        {
            if (File.Exists(macro)) return Path.GetFullPath(macro);
            string saved = Path.Combine(MacroFile.Folder(settingsPath), MacroFile.SafeFileName(macro) + MacroFile.Extension);
            return File.Exists(saved) ? saved : null;
        }

        private int CommandFailed(int code, string message)
        {
            if (message != null)
            {
                ShowNotice(message, Theme.Warning, 0);
                Program.Say(message);
            }
            OpenLog()?.Write("command.end", "code", code, "message", message);
            commandLogPath = null;
            return code;
        }

        /// <summary>The command's run ended: its exit code, and the window closes if asked to.</summary>
        private void FinishCommand(int code)
        {
            ExitCode = code;
            commandRun = 0;
            commandLogPath = null;
            if (Command != null && Command.ExitWhenDone && !closing) BeginInvoke(new Action(Close));
        }

        /// <summary>The Studio's run ended (a command may be waiting for it).</summary>
        internal void MacroRunEnded(MacroStopReason reason)
        {
            if (commandRun == 2) FinishCommand(ExitCodes.For(reason));
        }

        internal bool ClickerBusy => engine.Phase != EnginePhase.Idle;

        // ------------------------------------------------------------------ run control

        internal void Toggle(bool fromHotkey)
        {
            if (engine.Phase != EnginePhase.Idle) engine.Stop();
            else StartClicking(fromHotkey);
        }

        private void StartClicking(bool fromHotkey)
        {
            if (engine.Phase != EnginePhase.Idle) return;
            if (StudioBusy)
            {
                hudArmed = true;
                Refuse(Loc.T("mmsg.studioBusy"), Loc.T("mmsg.studioBusy"));
                return;
            }
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
            clickLog = OpenLog();
            clickLog?.Write("run.start", "kind", "clicker", "cps", Math.Round(Interval.ClicksPerSecond(settings.IntervalMs), 3),
                            "button", settings.Button.ToString(), "double", settings.DoubleClick, "repeat", settings.RepeatCount,
                            "app", settings.WindowLock ? settings.LockExe : null);

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
            if (e.Mode != PowerModes.Suspend) return;
            engine.Stop(StopReason.Suspended);
            studio?.StopAll(MacroStopReason.Suspended);
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.ConsoleDisconnect ||
                e.Reason == SessionSwitchReason.RemoteDisconnect)
            {
                engine.Stop(StopReason.Suspended);
                studio?.StopAll(MacroStopReason.Suspended);
            }
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
            clickLog?.Write("run.stop", "kind", "clicker", "reason", reason.ToString(), "clicks", engine.Clicks,
                            "elapsedMs", (long)engine.SessionElapsed.TotalMilliseconds);
            clickLog = null;
            if (commandRun == 1) FinishCommand(ExitCodes.For(reason));
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

        internal void Beep(bool starting)
        {
            if (!settings.Sounds) return;
            uint frequency = starting ? 880u : 520u;
            uint duration = starting ? 70u : 90u;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate { Native.Beep(frequency, duration); });
        }

        /// <summary>Lower and longer than the stop beep: "this did not do what you expected".</summary>
        internal void ErrorBeep()
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
                    case IdStop:
                        engine.Stop();
                        studio?.StopAll(MacroStopReason.Manual);
                        break;
                    case IdCapture:
                        if (studio != null && studio.OnCaptureHotkey()) break; // the Studio was waiting for it
                        if (Enabled) CapturePosition();
                        break;
                }
                return;
            }
            base.WndProc(ref m);
        }

        // ------------------------------------------------------------------ status

        private void ShowNotice(string text, Color color, int seconds)
        {
            display.SetMessage(text, color);
            noticeExpires = seconds > 0 ? Stopwatch.GetTimestamp() + seconds * Stopwatch.Frequency : 0;
        }

        /// <summary>The display's resting message: honest about what the readouts measure (the hotkeys are on their keys).</summary>
        private void ShowHint()
        {
            display.SetMessage(Loc.T("stat.note"), Theme.Dim);
            noticeExpires = 0;
        }

        private void RefreshStats()
        {
            EnginePhase phase = engine.Phase;
            long clicks = engine.Clicks;
            display.SetRate(FormatCps(Interval.ClicksPerSecond(settings.IntervalMs)), Loc.T("stat.requested"));
            display.SetReadout(0, Loc.T("stat.clicks"), clicks.ToString("N0", CultureInfo.CurrentCulture));
            display.SetReadout(2, Loc.T("stat.time"), FormatElapsed(engine.SessionElapsed));
            display.SetLock(settings.WindowLock, "LOCK");

            long now = Stopwatch.GetTimestamp();
            string sentText = "—";
            if (phase == EnginePhase.Clicking)
            {
                // Average over a few intervals (at least 2 s) so slow and fast rates both read steadily.
                double sent = sentRate.Sample(clicks, engine.LastClickStamp, Math.Max(2, 3 * settings.IntervalMs / 1000));
                if (!double.IsNaN(sent)) sentText = FormatCps(sent);
            }
            else
            {
                sentRate.Reset(); // after a pause, measure the resumed clicking only
            }
            display.SetReadout(1, Loc.T("stat.sent"), sentText);

            switch (phase)
            {
                case EnginePhase.Countdown:
                    display.SetStatus(Theme.Warning, true, Loc.F("status.countdown",
                        engine.CountdownRemaining.TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture) + " s"));
                    break;
                case EnginePhase.Clicking:
                    display.SetStatus(Theme.Success, true, Loc.T("status.clicking"));
                    break;
                case EnginePhase.Paused:
                    display.SetStatus(Theme.Warning, true, PausedText());
                    break;
                default:
                    // Stopped: the lamp is dark, unless it reports how the last run ended.
                    Color led = lastStop == StopReason.Blocked ? Theme.Danger
                              : lastStop == StopReason.Completed ? Theme.Success
                              : lastStop.HasValue && lastStop.Value >= StopReason.FocusLost ? Theme.Warning : Theme.Success;
                    bool lit = lastStop == StopReason.Blocked || lastStop == StopReason.Completed ||
                               (lastStop.HasValue && lastStop.Value >= StopReason.FocusLost);
                    display.SetStatus(led, lit, lastStop == StopReason.Blocked ? Loc.T("status.blocked")
                                              : lastStop == StopReason.Completed ? Loc.T("status.done")
                                              : Loc.T("status.idle"));
                    break;
            }
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
            string text = phase == EnginePhase.Idle ? "V-Clicker" : "V-Clicker - " + display.Status;
            Tuple<Color, string> macro = phase == EnginePhase.Idle ? studio?.Indicator : null;
            if (macro != null && macro.Item1 != Theme.Dim)
            {
                // Recording or running a macro: green while it acts, amber while it waits.
                icon = macro.Item1 == Theme.Warning ? trayPaused : trayClicking;
                text = "V-Clicker - " + macro.Item2;
            }
            if (tray.Icon != icon) tray.Icon = icon;
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
            Tuple<Color, string> macro = phase == EnginePhase.Idle ? studio?.Indicator : null;
            if (macro != null && settings.Hud)
            {
                IntPtr window = studio.IndicatorWindow;
                if (hud == null || hud.IsDisposed) hud = new Hud();
                Rectangle screen = window != IntPtr.Zero ? Screen.FromHandle(window).Bounds : Screen.PrimaryScreen.Bounds;
                hud.Display(macro.Item1, macro.Item2, screen, settings.HudCorner);
                if (++hudTicks % 10 == 0) hud.KeepOnTop();
                return;
            }
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
            if (Command != null && Command.HasAction)
                BeginInvoke(new Action(() =>
                {
                    int code = RunCommand(Command);
                    if (code >= 0) FinishCommand(code);
                }));
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
            }
            else
            {
                Size full = Size; // read before AutoSize is switched off: the form falls back to its default size
                AutoSize = false;
                AutoScroll = true;
                Size = new Size(full.Width + SystemInformation.VerticalScrollBarWidth, area.Height);
                Top = area.Top;
            }
            // Last, with the scroll bar counted: the advanced level widens the window.
            if (Right > area.Right) Left = Math.Max(area.Left, area.Right - Width);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.StyleTitleBar(Handle, ColorRef(Theme.Bg), ColorRef(Theme.Text), ColorRef(Theme.Border));
            Native.SetWindowTheme(Handle, "DarkMode_Explorer", null); // dark scroll bars on short screens
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
            closing = true;
            if (studio != null && !studio.IsDisposed)
            {
                studio.Close(); // asks to save unsaved changes
                if (studio != null && !studio.IsDisposed && studio.Visible)
                {
                    e.Cancel = true;
                    closing = false;
                    return;
                }
            }
            // The engine's stop is never handled once the window is gone: a run cut short here is "stopped".
            if (engine.Phase != EnginePhase.Idle)
            {
                clickLog?.Write("run.stop", "kind", "clicker", "reason", StopReason.Manual.ToString(), "clicks", engine.Clicks,
                                "elapsedMs", (long)engine.SessionElapsed.TotalMilliseconds);
                clickLog = null;
                if (commandRun == 1) FinishCommand(ExitCodes.Stopped);
            }
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

        internal static bool IsElevated()
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
