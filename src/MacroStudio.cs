using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.VisualBasic.FileIO;

namespace VClicker
{
    /// <summary>
    /// The Macro Studio window: record a task once in one app window, see it as a list of plain sentences, edit,
    /// save and repeat it. See docs/superpowers/specs/2026-10-21-macro-studio-design.md.
    /// </summary>
    internal sealed partial class MacroStudio : Form
    {
        private const int CountdownMs = 3000;
        private const int UndoLimit = 100;
        private const int FlashMs = 2500;

        private readonly MainForm host; // null in tests
        private readonly ClickSettings settings;
        private readonly string folder;
        private readonly MacroPlayer player;
        private readonly Timer timer = new Timer { Interval = 100 };
        private readonly ToolTip tips = new ToolTip { InitialDelay = 400, ReshowDelay = 200 };
        private readonly List<Macro> undo = new List<Macro>(), redo = new List<Macro>();
        private readonly List<Action> localizers = new List<Action>();

        private Macro macro = new Macro();
        private string path; // null = never saved
        private bool dirty, loadingUi;

        // Recording: a countdown, then the recorder; the window chosen in Gravar.
        private AppWindow recordWindow;
        private bool recordAppend;
        private long recordStartAt; // Stopwatch timestamp; 0 = no countdown
        private MacroRecorder recorder;
        private bool recordWasPaused;

        // Running.
        private AppWindow runWindow;
        private int shownStep = -1, lastBeepSecond = -1;
        private MacroPhase lastPhase;

        // Capturing a point for the selected step (F7 in the target app).
        private AppWindow captureWindow;
        private int captureIndex = -1;
        private enum CaptureFor { Point, Convert, Insert, Repoint, Target, Retarget }
        private CaptureFor captureFor;

        // After F7 for a condition or a target: the point is read off the UI thread (the app stays in front).
        private Task<object> analyzing;
        private int analyzingIndex;
        private CaptureFor analyzingFor;
        private AppWindow analyzingWindow;

        // The simulation is open: like the condition dialog, nothing else starts or edits the macro meanwhile.
        private bool simulating;
        // The condition dialog is open: F7 is swallowed, the clicker cannot start and nothing else edits the macro.
        private bool conditionDialogOpen;

        /// <summary>The local copy an unsaved macro's safety approval belongs to: new for every macro opened or created.</summary>
        private string sessionInstance = Guid.NewGuid().ToString("N");

        // The status line, and what the indicator flashes after a stop.
        private string message;
        private Color messageColor = Theme.Dim;
        private string flashText;
        private long flashUntil;

        private ChoiceBox macroList;
        private KeyButton bNew, bRename, bDelete, bSave, bRecord, bRun, bSimulate, bStop, bCommand, bAi;
        // The open macro is an AI proposal: the note shown above it (null = not a proposal).
        private string aiNote;

        /// <summary>Values the command line gave for the next run's {{variables}}.</summary>
        internal IDictionary<string, string> CommandValues;
        // What was typed for a variable without a value, offered again next time (this session only).
        private readonly Dictionary<string, string> rememberedValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // The structured log of the run in progress (Developer Mode, or a command's --log); null = none.
        private RunLog runLog;
        private Stopwatch runClock;
        private NumberField fRepeat, fLimit;
        private LedCheck chkForever, chkLimit;
        private LedBadge status;
        private Label statusText;
        private TableLayoutPanel banners;
        private ListBox list;
        private Label emptyHint;
        private Panel detail;
        private readonly List<Action> detailCommits = new List<Action>();
        private Label detailTitle; // the selected step as a sentence: kept current while its values are edited
        private KeyButton bUp, bDown, bDuplicate, bRemove, bAddWait, bAddCondition, bUndo, bRedo;

        internal MacroStudio(MainForm host, ClickSettings settings, string settingsPath)
        {
            this.host = host;
            this.settings = settings;
            folder = MacroFile.Folder(settingsPath);
            player = new MacroPlayer(new SendInputInputSink());
            // Kind and position only: never what is typed.
            player.StepStarted += (index, pass, step) => System.Threading.Volatile.Read(ref runLog)?.Write("step", "index", index + 1, "iteration", pass, "kind", step.Kind.ToString());
            player.Stopped += reason =>
            {
                if (!IsHandleCreated || IsDisposed) return;
                try { BeginInvoke(new Action(() => OnPlayerStopped(reason))); }
                catch (InvalidOperationException) { } // closing
            };

            Text = Loc.T("studio.open");
            Icon = host?.Icon;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Ui(9.5f);
            Padding = Theme.Pad(16);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = Theme.Sz(760, 520);
            Size = Theme.Sz(900, 640);
            KeyPreview = true;
            DoubleBuffered = true;

            BuildLayout();
            timer.Tick += delegate { Tick(); };
            timer.Start();

            string latest = SavedMacros().OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (latest != null) LoadFile(latest);
            RefreshAll();
        }

        // ------------------------------------------------------------------ state the main window reads

        /// <summary>
        /// Recording (or counting down to it), running, simulating, analyzing a captured point, or the condition dialog
        /// is open: nothing else starts or edits the macro.
        /// </summary>
        public bool Busy => recordStartAt != 0 || recorder != null || player.Phase != MacroPhase.Idle || conditionDialogOpen || simulating || analyzing != null;

        // Busy, or waiting for F7 to capture a point.
        private bool BusyOrCapturing => Busy || captureIndex >= 0;

        /// <summary>What the on-screen indicator and the tray show for the Studio; null = nothing.</summary>
        public Tuple<Color, string> Indicator
        {
            get
            {
                if (BusyOrCapturing) return Tuple.Create(StateDot(), StateText());
                if (flashText != null && Stopwatch.GetTimestamp() < flashUntil) return Tuple.Create(Theme.Dim, flashText);
                return null;
            }
        }

        /// <summary>The window the macro works in (the indicator goes on its screen); Zero = none.</summary>
        public IntPtr IndicatorWindow => (recordWindow ?? runWindow ?? captureWindow ?? analyzingWindow)?.Handle ?? IntPtr.Zero;

        internal Macro Current => macro;

        // Test seams: the end-to-end test drives the real flow with a window of its own process (which the app
        // lists never show) and with synthetic input (which the recorder normally ignores).
        internal Func<string, List<AppWindow>> ListWindows = exe => WindowLock.List(exe);
        internal Func<string, List<AppWindow>, AppWindow> PickWindow;
        internal bool AcceptInjectedInput;
        /// <summary>Answers the safety review instead of the dialog (tests).</summary>
        internal Func<List<ImpactfulAction>, ReviewChoice> ReviewSafety;

        private AppWindow Pick(string prompt, List<AppWindow> windows) =>
            PickWindow != null ? PickWindow(prompt, windows) : WindowPicker.Pick(this, prompt, windows);

        // The app's only window, or the one the user picks among several; null = cancelled.
        private AppWindow ChooseWindow(List<AppWindow> windows) =>
            windows.Count == 1 ? windows[0] : Pick(Loc.F("picker.which", windows[0].DisplayName), windows);
        internal bool Dirty => dirty;

        internal int Selected
        {
            get => list.SelectedIndex;
            set => list.SelectedIndex = Math.Max(-1, Math.Min(macro.Steps.Count - 1, value));
        }

        /// <summary>F8 / Stop, a lock or sleep of the computer, closing the window: ends whatever is going on.</summary>
        public void StopAll(MacroStopReason reason)
        {
            if (!BusyOrCapturing) return;
            if (recordStartAt != 0)
            {
                recordStartAt = 0;
                recordWindow = null;
                SetMessage(Loc.T("mstop.Manual"), Theme.Dim);
            }
            if (recorder != null) FinishRecording();
            if (player.Phase != MacroPhase.Idle) player.Stop(reason);
            if (captureIndex >= 0)
            {
                captureIndex = -1;
                captureWindow = null;
            }
            if (analyzing != null)
            {
                // The read cannot be cancelled; its answer is dropped (and what it holds released) when it comes.
                analyzing.ContinueWith(t => { if (t.Status == TaskStatus.RanToCompletion) (t.Result as IDisposable)?.Dispose(); });
                analyzing = null;
                analyzingWindow = null;
                BringBack();
            }
            RefreshAll();
        }

        /// <summary>The capture hotkey: true when the Studio was waiting for it (the main window then ignores it).</summary>
        public bool OnCaptureHotkey()
        {
            if (conditionDialogOpen || simulating || analyzing != null) return true;
            if (captureIndex < 0) return false;
            int index = captureIndex;
            AppWindow window = captureWindow;
            captureIndex = -1;
            captureWindow = null;
            if (captureFor != CaptureFor.Point)
            {
                if (index > macro.Steps.Count || window == null || !StartAnalysis(index, window, captureFor)) BringBack();
                RefreshAll();
                return true;
            }
            if (index < macro.Steps.Count && window != null)
            {
                Point at;
                WindowRef reference;
                bool inside;
                using (MacroTarget.PerMonitorDpi())
                {
                    Native.GetCursorPos(out Native.POINT cursor);
                    inside = MacroTarget.Locate(window.Handle, new Point(cursor.X, cursor.Y), out reference, out at);
                }
                if (inside)
                {
                    Edit(() =>
                    {
                        macro.Steps[index].At = at;
                        macro.Steps[index].Window = reference;
                        macro.Steps[index].NeedsPoint = false;
                    });
                    SetMessage(Loc.F("mmsg.captured", at.X, at.Y), Theme.Success);
                    host?.Beep(false);
                }
                else
                {
                    SetMessage(Loc.F("mmsg.captureOutside", window.DisplayName), Theme.Warning);
                    host?.ErrorBeep();
                }
            }
            BringBack();
            RefreshAll();
            return true;
        }

        private static bool IsTargetMode(CaptureFor mode) => mode == CaptureFor.Target || mode == CaptureFor.Retarget;

        /// <summary>
        /// F7 for a condition or a target: the point is read off the UI thread, with the app still in front (UI
        /// Automation reads what is visible there). The Studio stays responsive; <see cref="Tick"/> opens the dialog
        /// with the answer; F8 drops it. false = refused (the message says why).
        /// </summary>
        private bool StartAnalysis(int index, AppWindow window, CaptureFor mode)
        {
            bool target = IsTargetMode(mode);
            IntPtr bound = window.Handle, root;
            Point screen;
            Rectangle client = Rectangle.Empty;
            Bitmap shot = null;
            bool inside;
            using (MacroTarget.PerMonitorDpi())
            {
                Native.GetCursorPos(out Native.POINT cursor);
                screen = new Point(cursor.X, cursor.Y);
                root = MacroTarget.RootAt(screen);
                inside = MacroTarget.Locate(bound, root, screen, out WindowRef where, out _);
                if (inside && target)
                {
                    // Searched where the click was recorded: the same window, never another dialog.
                    WindowRef recorded = index < macro.Steps.Count ? macro.Steps[index].Window : null;
                    if (index >= macro.Steps.Count || (where == null ? recorded != null : !where.Matches(recorded)))
                    {
                        SetMessage(Loc.T("mmsg.targetOtherWindow"), Theme.Warning);
                        host?.ErrorBeep();
                        return false;
                    }
                    client = WindowLock.ClientArea(root);
                    try { shot = SmartTargets.Shoot(client); } // now, before anything on the screen changes
                    catch (Exception error) when (error is Win32Exception || error is ArgumentException)
                    {
                        SetMessage(Loc.F("mmsg.analyzeFailed", settings.CaptureKey), Theme.Warning);
                        host?.ErrorBeep();
                        return false;
                    }
                }
            }
            if (!inside)
            {
                SetMessage(Loc.F("mmsg.captureOutside", window.DisplayName), Theme.Warning);
                host?.ErrorBeep();
                return false;
            }
            analyzing = Task.Run(() =>
            {
                using (MacroTarget.PerMonitorDpi())
                    return target ? (object)SmartTargets.Probe(root, screen, client, shot) : MacroConditions.Capture(bound, root, screen);
            });
            analyzingIndex = index;
            analyzingFor = mode;
            analyzingWindow = window;
            message = null;
            return true;
        }

        /// <summary>The F7 read finished: back to the Studio, and the dialog for what was found.</summary>
        private void FinishAnalysis()
        {
            Task<object> task = analyzing;
            analyzing = null;
            analyzingWindow = null;
            object result = task.Status == TaskStatus.RanToCompletion ? task.Result : null;
            BringBack();
            if (IsTargetMode(analyzingFor)) ApplyTarget(analyzingIndex, analyzingFor, result as TargetProbe);
            else ApplyCondition(analyzingIndex, analyzingFor, result as ConditionProbe);
            RefreshAll();
        }

        /// <summary>A condition read: ask what to wait for, apply it.</summary>
        private void ApplyCondition(int index, CaptureFor mode, ConditionProbe probe)
        {
            if (probe == null)
            {
                SetMessage(Loc.F("mmsg.analyzeFailed", settings.CaptureKey), Theme.Warning);
                host?.ErrorBeep();
                return;
            }
            // The step must still be what the capture was started for.
            MacroStep old = mode == CaptureFor.Insert || index >= macro.Steps.Count ? null : macro.Steps[index];
            bool valid = mode == CaptureFor.Insert ? index <= macro.Steps.Count
                       : mode == CaptureFor.Convert ? old?.Kind == StepKind.Wait : old?.Kind == StepKind.WaitFor;
            if (!valid) return;
            int timeout = old != null && old.Kind == StepKind.WaitFor ? old.DurationMs : MacroStep.DefaultTimeoutMs;
            MacroStep condition;
            conditionDialogOpen = true;
            try
            {
                using (var dialog = new ConditionDialog(probe, timeout))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    condition = dialog.Result;
                }
            }
            finally { conditionDialogOpen = false; }
            switch (mode)
            {
                case CaptureFor.Convert: ConvertToCondition(index, condition); break;
                case CaptureFor.Insert: InsertCondition(index, condition); break;
                default:
                    condition.WasMs = old.WasMs;
                    condition.WasRecordedMs = old.WasRecordedMs;
                    condition.Reverts = old.Reverts;
                    Edit(() => macro.Steps[index] = condition);
                    break;
            }
            host?.Beep(false);
        }

        /// <summary>A target read: ask how to recognize it, apply it.</summary>
        private void ApplyTarget(int index, CaptureFor mode, TargetProbe probe)
        {
            using (probe)
            {
                if (probe == null)
                {
                    SetMessage(Loc.F("mmsg.analyzeFailed", settings.CaptureKey), Theme.Warning);
                    host?.ErrorBeep();
                    return;
                }
                MacroStep old = index < macro.Steps.Count ? macro.Steps[index] : null;
                if (old == null || (old.Kind != StepKind.Click && old.Kind != StepKind.DoubleClick) ||
                    (mode == CaptureFor.Target) != (old.Target == null)) return; // the step changed meanwhile
                SmartTarget target;
                conditionDialogOpen = true;
                try
                {
                    using (var dialog = new TargetDialog(probe, old.Target?.TimeoutMs ?? SmartTarget.DefaultTimeoutMs))
                    {
                        if (dialog.ShowDialog(this) != DialogResult.OK) return;
                        target = dialog.Result;
                    }
                }
                finally { conditionDialogOpen = false; }
                ConvertToTarget(index, target);
                host?.Beep(false);
            }
        }

        /// <summary>A click finds its target before clicking; its recorded point and window stay as they are, to go back.</summary>
        internal void ConvertToTarget(int index, SmartTarget target)
        {
            if (index < 0 || index >= macro.Steps.Count) return;
            MacroStep step = macro.Steps[index];
            if (step.Kind != StepKind.Click && step.Kind != StepKind.DoubleClick) return;
            Edit(() => step.Target = target);
            list.SelectedIndex = index;
        }

        /// <summary>The click goes back to exactly its recorded point.</summary>
        internal void RevertTarget(int index)
        {
            if (index < 0 || index >= macro.Steps.Count || macro.Steps[index].Target == null) return;
            MacroStep step = macro.Steps[index];
            Edit(() => step.Target = null);
            list.SelectedIndex = index;
        }

        /// <summary>A wait becomes a condition; the wait is kept inside it (duration and recorded time) to go back.</summary>
        internal void ConvertToCondition(int index, MacroStep condition)
        {
            if (index < 0 || index >= macro.Steps.Count || macro.Steps[index].Kind != StepKind.Wait) return;
            MacroStep wait = macro.Steps[index];
            condition.WasMs = wait.DurationMs;
            condition.WasRecordedMs = wait.RecordedMs;
            condition.Reverts = true;
            Edit(() => macro.Steps[index] = condition);
            list.SelectedIndex = index;
        }

        internal void InsertCondition(int index, MacroStep condition)
        {
            index = Math.Max(0, Math.Min(macro.Steps.Count, index));
            condition.WasMs = condition.WasRecordedMs = 0;
            condition.Reverts = false;
            Edit(() => macro.Steps.Insert(index, condition));
            list.SelectedIndex = index;
        }

        /// <summary>The condition becomes the exact wait it replaced.</summary>
        internal void RevertCondition(int index)
        {
            if (index < 0 || index >= macro.Steps.Count) return;
            MacroStep condition = macro.Steps[index];
            if (condition.Kind != StepKind.WaitFor || !condition.Reverts) return;
            Edit(() => macro.Steps[index] = new MacroStep { Kind = StepKind.Wait, DurationMs = condition.WasMs, RecordedMs = condition.WasRecordedMs });
            list.SelectedIndex = index;
        }

        // ------------------------------------------------------------------ layout

        private void Localize(Action apply)
        {
            localizers.Add(apply);
            apply();
        }

        private KeyButton Button(string key, ButtonKind kind, EventHandler click, int width)
        {
            var button = new KeyButton { Kind = kind, Size = Theme.Sz(width, 36), Margin = Theme.Pad(0, 0, 8, 0) };
            if (kind == ButtonKind.Chip) button.Font = Theme.Legend(9f, bold: true);
            Localize(() => button.Text = Loc.T(key));
            button.Click += click;
            return button;
        }

        private static FlowLayoutPanel Bar() => new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
            BackColor = Theme.Bg, Margin = Padding.Empty, Padding = Theme.Pad(0, 0, 0, 12)
        };

        private void BuildLayout()
        {
            // Top: which macro, and the file actions.
            FlowLayoutPanel top = Bar();
            macroList = new ChoiceBox { Size = Theme.Sz(300, 36), Margin = Theme.Pad(0, 0, 10, 0) };
            macroList.AccessibleName = "Macro";
            macroList.SelectedIndexChanged += OnMacroChosen;
            bNew = Button("studio.new", ButtonKind.Secondary, delegate { NewMacro(); }, 84);
            bRename = Button("studio.rename", ButtonKind.Secondary, delegate { RenameMacro(); }, 104);
            bDelete = Button("studio.delete", ButtonKind.Secondary, delegate { DeleteMacro(); }, 94);
            bSave = Button("studio.save", ButtonKind.Secondary, delegate { Save(); }, 94);
            bAi = Button("studio.ai", ButtonKind.Secondary, delegate { BuildWithAi(); }, 130);
            top.Controls.AddRange(new Control[] { macroList, bNew, bRename, bDelete, bSave, bAi });

            // Second: record / run, stop. Record is a graphite key with a red lamp that lights while it records.
            FlowLayoutPanel run = Bar();
            bRecord = Button("studio.record", ButtonKind.Secondary, delegate { BeginRecording(); }, 118);
            bRecord.Led = Theme.Danger;
            bRun = Button("studio.run", ButtonKind.Primary, delegate { BeginRun(); }, 118);
            bSimulate = Button("studio.simulate", ButtonKind.Secondary, delegate { BeginSimulation(); }, 110);
            bStop = Button("studio.stop", ButtonKind.Secondary, delegate { StopAll(MacroStopReason.Manual); }, 94);
            bCommand = Button("studio.command", ButtonKind.Secondary, delegate { CopyCommand(); }, 150);
            bCommand.Visible = settings.DeveloperMode;
            run.Controls.AddRange(new Control[] { bRecord, bRun, bSimulate, bStop, bCommand });

            // Third: how many times, and how long at most (SafeClick: an explicit, optional duration limit).
            FlowLayoutPanel options = Bar();
            var repeatLabel = new Label
            {
                AutoSize = true, ForeColor = Theme.Dim, BackColor = Theme.Bg, Font = Theme.Legend(9.75f),
                Margin = Theme.Pad(0, 9, 8, 0), UseMnemonic = false
            };
            Localize(() => repeatLabel.Text = Loc.T("studio.repeat"));
            fRepeat = new NumberField { Minimum = 1, Maximum = 1000000, Size = Theme.Sz(92, 36), Margin = Theme.Pad(0, 0, 14, 0) };
            fRepeat.Number = 1;
            fRepeat.FieldName = "Repetitions";
            fRepeat.ValueChanged += delegate { if (!loadingUi) SetRepeat(); };
            chkForever = new LedCheck { Margin = Theme.Pad(0, 5, 0, 0) };
            Localize(() => chkForever.Text = Loc.T("studio.forever"));
            chkForever.CheckedChanged += delegate { if (!loadingUi) SetRepeat(); };
            chkLimit = new LedCheck { Margin = Theme.Pad(28, 5, 8, 0) };
            Localize(() => chkLimit.Text = Loc.T("studio.limit"));
            chkLimit.CheckedChanged += delegate { if (!loadingUi) SetLimit(); };
            fLimit = new NumberField { Minimum = 1, Maximum = Macro.MaxLimitMinutes, Size = Theme.Sz(104, 36), Unit = "min", Margin = Padding.Empty };
            fLimit.Number = 60;
            fLimit.FieldName = "Duration limit";
            fLimit.ValueChanged += delegate { if (!loadingUi) SetLimit(); };
            options.Controls.AddRange(new Control[] { repeatLabel, fRepeat, chkForever, chkLimit, fLimit });

            // The display: the status line (the same text the indicator shows, which may be turned off), then what to know
            // about this macro, on one glass, like the main window's display.
            var display = new WellPanel
            {
                Look = WellLook.Glass, Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = Theme.Pad(14, 10, 14, 10)
            };
            var glassBody = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, BackColor = Theme.Glass
            };
            glassBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var statusRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, BackColor = Theme.Glass, Margin = Padding.Empty
            };
            statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            status = new LedBadge { Margin = Theme.Pad(0, 0, 8, 0), Anchor = AnchorStyles.Left };
            statusText = new Label
            {
                AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, BackColor = Theme.Glass,
                ForeColor = Theme.Dim, AutoEllipsis = true, UseMnemonic = false
            };
            statusRow.Controls.Add(status, 0, 0);
            statusRow.Controls.Add(statusText, 1, 0);

            banners = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1,
                BackColor = Theme.Glass, Margin = Padding.Empty
            };
            banners.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            glassBody.Controls.Add(statusRow);
            glassBody.Controls.Add(banners);
            display.Controls.Add(glassBody);
            var gap = new Panel { Dock = DockStyle.Top, Height = Theme.Dp(12), BackColor = Theme.Bg };

            // Bottom: edit actions for the selected step.
            FlowLayoutPanel edits = Bar();
            edits.Dock = DockStyle.Bottom;
            edits.Padding = Theme.Pad(0, 12, 0, 0);
            bUp = Button("studio.up", ButtonKind.Chip, delegate { MoveStep(-1); }, 72);
            bDown = Button("studio.down", ButtonKind.Chip, delegate { MoveStep(1); }, 78);
            bDuplicate = Button("studio.duplicate", ButtonKind.Chip, delegate { Duplicate(); }, 96);
            bRemove = Button("studio.remove", ButtonKind.Chip, delegate { Remove(); }, 82);
            bAddWait = Button("studio.addWait", ButtonKind.Chip, delegate { InsertWait(); }, 84);
            bAddCondition = Button("studio.addCondition", ButtonKind.Chip, delegate { BeginCapture(list.SelectedIndex + 1, CaptureFor.Insert); }, 108);
            bUndo = Button("studio.undo", ButtonKind.Chip, delegate { Undo(); }, 86);
            bRedo = Button("studio.redo", ButtonKind.Chip, delegate { Redo(); }, 86);
            edits.Controls.AddRange(new Control[] { bUp, bDown, bDuplicate, bRemove, bAddWait, bAddCondition, bUndo, bRedo });

            // Middle: the steps in a recess, and the selected step's values on the plate beside it.
            var middle = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Bg };
            middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            middle.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var listWell = new WellPanel { Dock = DockStyle.Fill, Margin = Theme.Pad(0, 0, 18, 0), Padding = Theme.Pad(6) };
            list = new ListBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Theme.Field, ForeColor = Theme.Text,
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = Theme.Dp(32), IntegralHeight = true, Font = Theme.Ui(10f)
            };
            list.AccessibleName = "Steps";
            list.DrawItem += DrawStep;
            list.SelectedIndexChanged += delegate { ShowDetail(); UpdateEnabled(); };
            emptyHint = new Label
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Dim, BackColor = Theme.Field,
                UseMnemonic = false, Padding = Theme.Pad(24)
            };
            Localize(() => emptyHint.Text = Loc.T("studio.empty"));
            listWell.Controls.Add(list);
            listWell.Controls.Add(emptyHint);

            // The selected step's values scroll inside an outlined pane that ends level with the list.
            var detailFrame = new WellPanel { Look = WellLook.Frame, Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = Theme.Pad(14, 10, 4, 10) };
            detail = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, AutoScroll = true, Margin = Padding.Empty };
            detailFrame.Controls.Add(detail);

            middle.Controls.Add(listWell, 0, 0);
            middle.Controls.Add(detailFrame, 1, 0);

            // Docked controls are laid out last-added-first: the fill goes in first.
            Controls.Add(middle);
            Controls.Add(edits);
            Controls.Add(gap);
            Controls.Add(display);
            Controls.Add(options);
            Controls.Add(run);
            Controls.Add(top);

            tips.SetToolTip(bRecord, Loc.T("picker.record"));
        }

        private Label Note(string text, Color color, float size = 9.5f, bool semibold = false) => new Label
        {
            Text = text, AutoSize = true, ForeColor = color, BackColor = Theme.Bg, Font = Theme.Ui(size, semibold),
            UseMnemonic = false, Margin = Theme.Pad(0, 0, 0, 6), MaximumSize = new Size(Theme.Dp(300), 0)
        };

        // ------------------------------------------------------------------ banners

        /// <summary>One block of the display's message area, under a hairline.</summary>
        private static TableLayoutPanel GlassBlock(int columns) => new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = columns,
            BackColor = Theme.Glass, Margin = Theme.Pad(0, 10, 0, 0), Padding = Theme.Pad(0, 10, 0, 0)
        };

        private int BannerTextWidth(int reserved) =>
            Math.Max(Theme.Dp(280), ClientSize.Width - Padding.Horizontal - Theme.Dp(48) - reserved);

        /// <summary>A notice that asks for a decision: title and text on the left, the key on the right.</summary>
        private Control ActionBanner(string title, string text, Color accent, string buttonKey, Action click)
        {
            TableLayoutPanel body = GlassBlock(2);
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            body.Paint += PaintHairline;
            var words = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, BackColor = Theme.Glass, Margin = Padding.Empty, Dock = DockStyle.Fill };
            words.Controls.Add(new Label
            {
                Text = title, AutoSize = true, ForeColor = accent, BackColor = Theme.Glass, Font = Theme.Legend(10.5f, bold: true),
                UseMnemonic = false, Margin = Theme.Pad(0, 0, 0, 3)
            });
            words.Controls.Add(new Label
            {
                Text = text, AutoSize = true, ForeColor = Theme.Text, BackColor = Theme.Glass, UseMnemonic = false,
                Margin = Padding.Empty, MaximumSize = new Size(BannerTextWidth(Theme.Dp(290)), 0)
            });
            var button = new KeyButton
            {
                Text = Loc.T(buttonKey), Kind = ButtonKind.Secondary, Anchor = AnchorStyles.Right, Margin = Theme.Pad(12, 0, 0, 0),
                Size = new Size(TextRenderer.MeasureText(Loc.T(buttonKey), Theme.Legend(9.5f, bold: true)).Width + Theme.Dp(40), Theme.Dp(36))
            };
            button.Click += delegate { click(); };
            body.Controls.Add(words, 0, 0);
            body.Controls.Add(button, 1, 0);
            return body;
        }

        /// <summary>Things to know about this macro, one short line each.</summary>
        private Control NotesBanner(List<Tuple<string, Color>> notes)
        {
            TableLayoutPanel body = GlassBlock(1);
            body.Paint += PaintHairline;
            for (int i = 0; i < notes.Count; i++)
                body.Controls.Add(new Label
                {
                    Text = "•  " + notes[i].Item1, AutoSize = true, ForeColor = notes[i].Item2, BackColor = Theme.Glass,
                    Font = Theme.Ui(9f), UseMnemonic = false, Margin = Theme.Pad(0, 0, 0, i == notes.Count - 1 ? 0 : 4),
                    MaximumSize = new Size(BannerTextWidth(0), 0)
                });
            return body;
        }

        private static void PaintHairline(object sender, PaintEventArgs e)
        {
            var block = (Control)sender;
            using (var pen = new Pen(Theme.Mix(Theme.Glass, Theme.Border, 0.9f))) e.Graphics.DrawLine(pen, 0, 0, block.Width, 0);
        }

        private void UpdateBanners()
        {
            banners.SuspendLayout();
            foreach (Control old in banners.Controls.Cast<Control>().ToList()) old.Dispose();
            banners.Controls.Clear();
            if (macro.LongWaits().Any() && !Busy)
                banners.Controls.Add(ActionBanner(Loc.T("banner.longPause.title"), Loc.T("banner.longPause"), Theme.Warning,
                    "banner.longPause.button", OptimizePauses));
            var notes = new List<Tuple<string, Color>>();
            if (aiNote != null)
            {
                notes.Add(Tuple.Create(Loc.T("banner.ai"), Theme.Warning));
                if (aiNote.Length > 0) notes.Add(Tuple.Create(aiNote, Theme.Dim));
            }
            else if (macro.Steps.Any(st => st.WaitsForPoint)) notes.Add(Tuple.Create(Loc.T("banner.needsPoint"), Theme.Warning));
            if (macro.HasUnknown) notes.Add(Tuple.Create(Loc.T("banner.unknown"), Theme.Danger));
            if (macro.HasPassword) notes.Add(Tuple.Create(Loc.T("banner.password"), Theme.Warning));
            if (macro.HasText) notes.Add(Tuple.Create(Loc.T("banner.text"), Theme.Dim));
            if (MacroTarget.IsBrowser(macro.ExeName)) notes.Add(Tuple.Create(Loc.T("banner.browser"), Theme.Dim));
            if (macro.Steps.Any(st => st.Kind == StepKind.Hover)) notes.Add(Tuple.Create(Loc.T("banner.hover"), Theme.Dim));
            if (notes.Count > 0) banners.Controls.Add(NotesBanner(notes));
            banners.ResumeLayout();
        }

        // ------------------------------------------------------------------ the step list

        private static Glyph GlyphFor(MacroStep step)
        {
            switch (step.Kind)
            {
                case StepKind.Click: return step.Target != null ? Glyph.Target : Glyph.Click;
                case StepKind.DoubleClick: return step.Target != null ? Glyph.Target : Glyph.DoubleClick;
                case StepKind.Drag: return Glyph.Drag;
                case StepKind.Hover: return Glyph.Hover;
                case StepKind.Scroll: return Glyph.Scroll;
                case StepKind.Type: return Glyph.Type;
                case StepKind.Key: case StepKind.Shortcut: return Glyph.Key;
                case StepKind.Wait: return Glyph.Wait;
                case StepKind.WaitFor: return Glyph.WaitFor;
                case StepKind.Password: return Glyph.Lock;
                default: return Glyph.Unknown;
            }
        }

        private void DrawStep(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= macro.Steps.Count) return;
            MacroStep step = macro.Steps[e.Index];
            Graphics g = e.Graphics;
            Theme.Smooth(g);
            float k = Theme.Scale;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            bool current = player.Phase != MacroPhase.Idle && player.StepIndex == e.Index;
            using (var back = new SolidBrush(Theme.Field))
                g.FillRectangle(back, e.Bounds);
            if (selected)
            {
                // The selected step sits on a key-colored bar; the amber edge only when the list has the keyboard.
                var bar = new RectangleF(e.Bounds.Left + k, e.Bounds.Top + k, e.Bounds.Width - 2 * k, e.Bounds.Height - 2 * k);
                using (GraphicsPath path = Theme.RoundRect(bar, 5 * k))
                using (var fill = new SolidBrush(Theme.Key))
                    g.FillPath(fill, path);
                if (list.Focused) Theme.DrawFocus(g, bar, 5 * k);
            }

            Color color = step.Kind == StepKind.Password ? Theme.Warning
                        : step.Kind == StepKind.Unknown ? Theme.Danger
                        : step.Kind == StepKind.Wait ? Theme.Dim : Theme.Text;
            if (step.Kind == StepKind.Wait && step.DurationMs >= Macro.LongWaitMs) color = Theme.Warning;
            if (step.WaitsForPoint) color = Theme.Warning;
            int x = e.Bounds.Left + Theme.Dp(8);
            if (current) Theme.DrawLed(g, new PointF(x + 4 * k, e.Bounds.Top + e.Bounds.Height / 2f), 7 * k, Theme.Success, true);
            var number = new Rectangle(x + Theme.Dp(8), e.Bounds.Top, Theme.Dp(28), e.Bounds.Height);
            TextRenderer.DrawText(g, (e.Index + 1).ToString(CultureInfo.CurrentCulture), Theme.Legend(9.5f), number, Theme.Dim,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPrefix);
            float side = 16 * k;
            var icon = new RectangleF(number.Right + 10 * k, e.Bounds.Top + (e.Bounds.Height - side) / 2, side, side);
            Icons.Draw(g, GlyphFor(step), icon, color);
            int textLeft = (int)(icon.Right + 10 * k);
            var text = new Rectangle(textLeft, e.Bounds.Top, e.Bounds.Right - textLeft - Theme.Dp(10), e.Bounds.Height);
            TextRenderer.DrawText(g, step.Describe(), list.Font, text, color,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix |
                TextFormatFlags.EndEllipsis);
        }

        private void RefreshList()
        {
            int keep = list.SelectedIndex;
            list.BeginUpdate();
            if (list.Items.Count != macro.Steps.Count)
            {
                list.Items.Clear();
                for (int i = 0; i < macro.Steps.Count; i++) list.Items.Add(i);
            }
            list.EndUpdate();
            list.SelectedIndex = Math.Min(keep, macro.Steps.Count - 1);
            list.Invalidate();
            emptyHint.Visible = macro.Steps.Count == 0;
            list.Visible = macro.Steps.Count > 0;
        }

        // ------------------------------------------------------------------ the selected step

        /// <summary>Applies a value still being typed in the detail panel (it is applied on leaving the field).</summary>
        private void CommitDetail()
        {
            foreach (Action commit in detailCommits.ToList()) commit();
        }

        private void ShowDetail()
        {
            detailCommits.Clear();
            detail.SuspendLayout();
            foreach (Control old in detail.Controls.Cast<Control>().ToList()) old.Dispose();
            detail.Controls.Clear();
            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2,
                BackColor = Theme.Bg, Padding = Theme.Pad(0, 0, 4, 0)
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            detail.Controls.Add(body);

            int index = list.SelectedIndex;
            if (index < 0 || index >= macro.Steps.Count)
            {
                Full(body, Note(Loc.T("studio.noSelection"), Theme.Dim));
                detail.ResumeLayout();
                return;
            }
            MacroStep step = macro.Steps[index];
            bool editable = !Busy;
            detailTitle = Note(StepTitle(index), Theme.Text, 10f, true);
            detailTitle.Margin = Theme.Pad(0, 2, 0, 12);
            detailTitle.MaximumSize = new Size(Math.Max(Theme.Dp(220), detail.ClientSize.Width - Theme.Dp(28)), 0); // the panel width, so a title wraps once, not word by word
            Full(body, detailTitle);

            if (step.Kind == StepKind.Click || step.Kind == StepKind.DoubleClick || step.Kind == StepKind.Drag)
            {
                var button = new Segmented { Items = new[] { Loc.T("step.left"), Loc.T("step.middle"), Loc.T("step.right") }, Size = Theme.Sz(220, 36) };
                button.SelectedIndex = (int)step.Button;
                button.SelectedChanged += delegate { Edit(() => step.Button = (ClickButton)button.SelectedIndex, false); };
                button.Enabled = editable;
                Pair(body, "studio.button", button);
            }
            if (step.Target != null && (step.Kind == StepKind.Click || step.Kind == StepKind.DoubleClick)) TargetDetail(body, step, index, editable);
            else if (step.IsMouse)
            {
                // Typing both X and Y gives a proposed click its place, like a capture; one alone would click at an edge.
                if (step.WaitsForPoint) Full(body, Note(Loc.T("banner.needsPoint"), Theme.Warning, 9f));
                bool typedX = false, typedY = false;
                Pair(body, "X", Coordinate(step.At.X, v => { step.At = new Point(v, step.At.Y); typedX = true; if (typedY) step.NeedsPoint = false; }, editable));
                Pair(body, "Y", Coordinate(step.At.Y, v => { step.At = new Point(step.At.X, v); typedY = true; if (typedX) step.NeedsPoint = false; }, editable));
                if (step.Kind == StepKind.Drag)
                {
                    Pair(body, "studio.toX", Coordinate(step.To.X, v => step.To = new Point(v, step.To.Y), editable));
                    Pair(body, "studio.toY", Coordinate(step.To.Y, v => step.To = new Point(step.To.X, v), editable));
                }
                if (step.Kind == StepKind.Scroll)
                {
                    NumberField notches = Field(-MacroStep.MaxCount, MacroStep.MaxCount, step.Notches, editable);
                    notches.ValueChanged += delegate
                    {
                        if (notches.Value == 0) { notches.Value = step.Notches; return; } // 0 notches would do nothing
                        Edit(() => step.Notches = notches.Value, false);
                    };
                    Pair(body, "studio.notches", notches);
                }
                var capture = new KeyButton { Text = Loc.T("studio.capture"), Kind = ButtonKind.Secondary, Size = Theme.Sz(170, 36), Margin = Theme.Pad(0, 4, 0, 8) };
                capture.Enabled = editable && macro.ExeName.Length > 0;
                capture.Click += delegate { BeginCapture(index); };
                Full(body, capture);
                Full(body, Note(Loc.T("studio.relative"), Theme.Dim, 8.5f));
                if (step.Kind == StepKind.Click || step.Kind == StepKind.DoubleClick)
                {
                    var smart = new KeyButton
                    {
                        Text = Loc.T("studio.toTarget"), Kind = ButtonKind.Secondary, Size = Theme.Sz(220, 36),
                        Enabled = editable && macro.ExeName.Length > 0, Margin = Theme.Pad(0, 4, 0, 4)
                    };
                    smart.Click += delegate { BeginCapture(index, CaptureFor.Target); };
                    Full(body, smart);
                    Full(body, Note(Loc.T("studio.toTarget.tip"), Theme.Dim, 8.5f));
                }
            }
            switch (step.Kind)
            {
                case StepKind.Type:
                    var textField = new TextField { Width = Theme.Dp(240) };
                    TextBox text = textField.Box;
                    text.Text = step.Text;
                    text.ReadOnly = !editable;
                    text.AccessibleName = Loc.T("studio.text");
                    void CommitText()
                    {
                        if (text.Text.Length > 0 && text.Text != step.Text) Edit(() => step.Text = text.Text, false);
                        else if (text.Text.Length == 0) text.Text = step.Text; // an empty "type" step would do nothing
                    }
                    text.Leave += delegate { CommitText(); };
                    detailCommits.Add(CommitText);
                    text.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { CommitText(); e.SuppressKeyPress = true; } };
                    Pair(body, "studio.text", textField);
                    break;
                case StepKind.Key:
                    NumberField count = Field(1, MacroStep.MaxCount, step.Count, editable);
                    count.ValueChanged += delegate { Edit(() => step.Count = count.Value, false); };
                    Pair(body, "studio.count", count);
                    Full(body, Note(Loc.T("studio.keyInfo"), Theme.Dim, 8.5f));
                    break;
                case StepKind.Shortcut:
                    Full(body, Note(Loc.T("studio.keyInfo"), Theme.Dim, 8.5f));
                    break;
                case StepKind.Wait:
                    NumberField seconds = Field(0, MacroStep.MaxWaitMs / 1000, step.DurationMs / 1000.0, editable);
                    seconds.Decimals = 1;
                    seconds.Number = step.DurationMs / 1000.0;
                    seconds.ValueChanged += delegate { Edit(() => step.DurationMs = (int)Math.Round(seconds.Number * 1000), false); };
                    Pair(body, "studio.seconds", seconds);
                    if (step.IsShortened)
                    {
                        var restore = new KeyButton
                        {
                            Text = Loc.F("studio.restore", MacroStep.Seconds(step.RecordedMs)), Kind = ButtonKind.Secondary,
                            Size = Theme.Sz(170, 36), Enabled = editable, Margin = Theme.Pad(0, 4, 0, 8)
                        };
                        restore.Click += delegate { RestoreWait(index); };
                        Full(body, restore);
                    }
                    var convert = new KeyButton
                    {
                        Text = Loc.T("studio.toCondition"), Kind = ButtonKind.Secondary, Size = Theme.Sz(220, 36),
                        Enabled = editable && macro.ExeName.Length > 0, Margin = Theme.Pad(0, 4, 0, 4)
                    };
                    convert.Click += delegate { BeginCapture(index, CaptureFor.Convert); };
                    Full(body, convert);
                    Full(body, Note(Loc.T("studio.toCondition.tip"), Theme.Dim, 8.5f));
                    break;
                case StepKind.WaitFor:
                    NumberField limit = Field(MacroStep.MinTimeoutMs / 1000, MacroStep.MaxTimeoutMs / 1000, step.DurationMs / 1000, editable);
                    limit.Unit = "s";
                    limit.ValueChanged += delegate { Edit(() => step.DurationMs = limit.Value * 1000, false); };
                    Pair(body, "studio.timeout", limit);
                    var repoint = new KeyButton
                    {
                        Text = Loc.T("studio.repoint"), Kind = ButtonKind.Secondary, Size = Theme.Sz(170, 36),
                        Enabled = editable, Margin = Theme.Pad(0, 4, 0, 4)
                    };
                    repoint.Click += delegate { BeginCapture(index, CaptureFor.Repoint); };
                    Full(body, repoint);
                    if (step.Reverts)
                    {
                        var back = new KeyButton
                        {
                            Text = Loc.F("studio.backToWait", MacroStep.Seconds(step.WasMs)), Kind = ButtonKind.Secondary,
                            Size = Theme.Sz(220, 36), Enabled = editable, Margin = Theme.Pad(0, 4, 0, 4)
                        };
                        back.Click += delegate { RevertCondition(index); };
                        Full(body, back);
                    }
                    Full(body, Note(Loc.T(step.Condition == ConditionKind.Color ? "studio.colorInfo" : "studio.conditionInfo"), Theme.Dim, 8.5f));
                    break;
                case StepKind.Password:
                    Full(body, Note(Loc.T("studio.passwordInfo"), Theme.Warning));
                    break;
                case StepKind.Unknown:
                    Full(body, Note(Loc.T("studio.unknownInfo"), Theme.Danger));
                    break;
            }
            detail.ResumeLayout();
        }

        /// <summary>A click with a target: what it looks for, for how long, and the way back to its recorded point.</summary>
        private void TargetDetail(TableLayoutPanel body, MacroStep step, int index, bool editable)
        {
            if (step.Target.Kind == TargetKind.Image)
            {
                Bitmap image = step.Target.Decode();
                var view = new PixelView { Image = image, Size = Theme.Sz(96, 96), BackColor = Theme.Field };
                view.Disposed += delegate { image?.Dispose(); };
                Full(body, view);
            }
            NumberField limit = Field(MacroStep.MinTimeoutMs / 1000, MacroStep.MaxTimeoutMs / 1000, step.Target.TimeoutMs / 1000, editable);
            limit.Unit = "s";
            limit.ValueChanged += delegate { Edit(() => step.Target.TimeoutMs = limit.Value * 1000, false); };
            Pair(body, "studio.timeout", limit); // the short label: the detail panel is narrow
            var repoint = new KeyButton
            {
                Text = Loc.T("studio.repoint"), Kind = ButtonKind.Secondary, Size = Theme.Sz(170, 36),
                Enabled = editable && macro.ExeName.Length > 0, Margin = Theme.Pad(0, 4, 0, 4)
            };
            repoint.Click += delegate { BeginCapture(index, CaptureFor.Retarget); };
            Full(body, repoint);
            var back = new KeyButton
            {
                Text = Loc.F("studio.backToPoint", step.At.X, step.At.Y), Kind = ButtonKind.Secondary, Size = Theme.Sz(240, 36),
                Enabled = editable, Margin = Theme.Pad(0, 4, 0, 4)
            };
            back.Click += delegate { RevertTarget(index); };
            Full(body, back);
            Full(body, Note(Loc.F(step.Target.Kind == TargetKind.Image ? "studio.targetImageInfo" : "studio.targetInfo", step.At.X, step.At.Y), Theme.Dim, 8.5f));
        }

        /// <summary>"6. Clicar (esquerdo) em 812, 440": the step's number as in the list, then the step.</summary>
        private string StepTitle(int index) =>
            (index + 1).ToString(CultureInfo.CurrentCulture) + ".  " + macro.Steps[index].Describe();

        private static void Full(TableLayoutPanel body, Control control)
        {
            // Action keys in the detail panel are as wide as their legend needs.
            if (control is KeyButton key)
                key.Width = Math.Max(key.Width, TextRenderer.MeasureText(key.Text, key.Font).Width + Theme.Dp(44));
            body.Controls.Add(control);
            body.SetColumnSpan(control, 2);
        }

        private void Pair(TableLayoutPanel body, string key, Control field)
        {
            var label = new Label
            {
                Text = key.Length <= 2 ? key : Loc.T(key), AutoSize = true, ForeColor = Theme.Dim, BackColor = Theme.Bg, Font = Theme.Legend(9.75f),
                Anchor = AnchorStyles.Left, Margin = Theme.Pad(0, 0, 10, 8), UseMnemonic = false
            };
            field.Margin = Theme.Pad(0, 0, 0, 8);
            body.Controls.Add(label);
            body.Controls.Add(field);
        }

        private NumberField Field(double min, double max, double value, bool editable)
        {
            var field = new NumberField { Minimum = min, Maximum = max, Size = Theme.Sz(110, 36), Enabled = editable };
            field.Number = value;
            detailCommits.Add(field.Commit);
            return field;
        }

        private NumberField Coordinate(int value, Action<int> set, bool editable)
        {
            NumberField field = Field(-MacroStep.MaxCoordinate, MacroStep.MaxCoordinate, value, editable);
            field.ValueChanged += delegate { Edit(() => set(field.Value), false); };
            return field;
        }

        // ------------------------------------------------------------------ editing (every change can be undone)

        /// <param name="rebuildDetail">false when the change comes from the detail panel itself (it must not be rebuilt under the user).</param>
        private void Edit(Action change, bool rebuildDetail = true)
        {
            if (BusyOrCapturing) return; // never under a pending capture (it would land on another step)
            undo.Add(macro.Clone());
            if (undo.Count > UndoLimit) undo.RemoveAt(0);
            redo.Clear();
            change();
            dirty = true;
            RefreshList();
            UpdateBanners();
            if (rebuildDetail) ShowDetail();
            else if (detailTitle != null && !detailTitle.IsDisposed && list.SelectedIndex >= 0) detailTitle.Text = StepTitle(list.SelectedIndex);
            UpdateEnabled();
            UpdateTitle();
        }

        internal void MoveStep(int delta)
        {
            int index = list.SelectedIndex, target = index + delta;
            if (index < 0 || target < 0 || target >= macro.Steps.Count) return;
            Edit(() =>
            {
                MacroStep step = macro.Steps[index];
                macro.Steps.RemoveAt(index);
                macro.Steps.Insert(target, step);
            });
            list.SelectedIndex = target;
        }

        internal void Duplicate()
        {
            int index = list.SelectedIndex;
            if (index < 0) return;
            Edit(() => macro.Steps.Insert(index + 1, macro.Steps[index].Clone()));
            list.SelectedIndex = index + 1;
        }

        internal void Remove()
        {
            int index = list.SelectedIndex;
            if (index < 0) return;
            Edit(() => macro.Steps.RemoveAt(index));
            list.SelectedIndex = Math.Min(index, macro.Steps.Count - 1);
        }

        internal void InsertWait()
        {
            int index = list.SelectedIndex + 1; // after the selection, or first in an empty list
            Edit(() => macro.Steps.Insert(index, new MacroStep { Kind = StepKind.Wait, DurationMs = 1000 }));
            list.SelectedIndex = index;
        }

        internal void Undo() => Swap(undo, redo);

        internal void Redo() => Swap(redo, undo);

        private void Swap(List<Macro> from, List<Macro> to)
        {
            if (from.Count == 0 || BusyOrCapturing) return;
            to.Add(macro.Clone());
            macro = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            dirty = true;
            RefreshAll();
        }

        /// <param name="durations">Step index → new wait in ms, for the waits the user ticked; nothing else changes.</param>
        internal void ApplyOptimization(IDictionary<int, int> durations)
        {
            if (durations.Count == 0) return;
            Edit(() =>
            {
                foreach (KeyValuePair<int, int> pair in durations)
                {
                    MacroStep step = macro.Steps[pair.Key];
                    if (step.Kind != StepKind.Wait) continue;
                    if (step.RecordedMs == 0) step.RecordedMs = step.DurationMs; // keep what it was, to restore
                    step.DurationMs = Math.Max(0, Math.Min(MacroStep.MaxWaitMs, pair.Value));
                }
            });
        }

        internal void RestoreWait(int index)
        {
            if (index < 0 || index >= macro.Steps.Count || !macro.Steps[index].IsShortened) return;
            Edit(() => macro.Steps[index].DurationMs = macro.Steps[index].RecordedMs);
        }

        private void SetRepeat()
        {
            int repeat = chkForever.Checked ? 0 : Math.Max(1, fRepeat.Value);
            if (repeat == macro.Repeat) return;
            macro.Repeat = repeat;
            dirty = true;
            fRepeat.Enabled = !chkForever.Checked && !Busy;
            UpdateTitle();
        }

        private void SetLimit()
        {
            int limit = chkLimit.Checked ? Math.Max(1, fLimit.Value) : 0;
            fLimit.Enabled = chkLimit.Checked && !Busy;
            if (limit == macro.LimitMinutes) return;
            macro.LimitMinutes = limit;
            dirty = true;
            UpdateTitle();
        }

        private void OptimizePauses()
        {
            List<int> waits = macro.LongWaits().ToList();
            if (waits.Count == 0) return;
            using (var dialog = new PauseOptimizer(macro, waits))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) ApplyOptimization(dialog.Result);
            }
        }

        // ------------------------------------------------------------------ files

        private string[] SavedMacros() => MacroFile.SavedFiles(folder);

        private string DisplayName => path == null ? Loc.T("studio.untitled") : (macro.Name.Length > 0 ? macro.Name : Path.GetFileNameWithoutExtension(path));

        private sealed class Entry
        {
            public string Path, Name;
            public override string ToString() => Name;
        }

        /// <summary>Also called when macro files change outside the Studio (a profile import).</summary>
        internal void RefreshMacroList()
        {
            loadingUi = true;
            macroList.BeginUpdate();
            macroList.Items.Clear();
            if (path == null) macroList.Items.Add(new Entry { Name = Loc.T("studio.untitled") });
            foreach (string file in SavedMacros().OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase))
            {
                bool current = path != null && MacroFile.SamePath(file, path);
                macroList.Items.Add(new Entry { Path = file, Name = current ? DisplayName : Path.GetFileNameWithoutExtension(file) });
            }
            macroList.SelectedItem = macroList.Items.Cast<Entry>().FirstOrDefault(e =>
                e.Path == null ? path == null : path != null && MacroFile.SamePath(e.Path, path));
            macroList.EndUpdate();
            loadingUi = false;
        }

        private void OnMacroChosen(object sender, EventArgs e)
        {
            if (loadingUi || !(macroList.SelectedItem is Entry entry) || entry.Path == null) return;
            if (path != null && MacroFile.SamePath(entry.Path, path)) return;
            if (Busy || !ConfirmDiscard())
            {
                RefreshMacroList();
                return;
            }
            LoadFile(entry.Path);
            RefreshAll();
        }

        private void LoadFile(string file)
        {
            try
            {
                Open(MacroFile.Load(file), file);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is FormatException)
            {
                SetMessage(Loc.F("mmsg.openFailed", Path.GetFileName(file)), Theme.Warning);
            }
        }

        /// <summary>Shows <paramref name="loaded"/> as the current macro (file = where it lives; null = unsaved).</summary>
        internal void Open(Macro loaded, string file)
        {
            macro = loaded;
            path = file;
            aiNote = null;
            sessionInstance = Guid.NewGuid().ToString("N");
            dirty = false;
            undo.Clear();
            redo.Clear();
            list.SelectedIndex = -1;
            RefreshAll();
        }

        /// <returns>false if the user cancelled.</returns>
        private bool ConfirmDiscard()
        {
            CommitDetail();
            if (!dirty) return true;
            int answer = Dialogs.Ask(this, Loc.F("ask.unsaved", DisplayName), Loc.T("studio.save"), Loc.T("ask.discard"), Loc.T("ask.cancel"));
            if (answer == 0) return Save();
            return answer == 1;
        }

        private void NewMacro()
        {
            if (Busy || !ConfirmDiscard()) return;
            Open(new Macro(), null);
        }

        /// <returns>false if not saved (cancelled, or the disk said no).</returns>
        internal bool Save()
        {
            if (Busy) return false;
            CommitDetail();
            if (path == null)
            {
                string name = AskName(macro.Name);
                if (name == null) return false;
                macro.Name = name;
                path = MacroFile.PathFor(folder, name);
            }
            bool firstSave = !File.Exists(path);
            try
            {
                MacroFile.Save(macro, path);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                SetMessage(Loc.F("mmsg.saveFailed", error.Message), Theme.Danger);
                return false;
            }
            // Approved before its first save: the new file is the same copy the user approved.
            string signature = SafeClick.Signature(macro), unsaved = SafeClick.ApprovalKey(signature, sessionInstance);
            if (firstSave && settings.IsApproved(unsaved))
            {
                settings.ApprovedMacros.Remove(unsaved);
                settings.Approve(ApprovalKey(signature));
                host?.SaveSettings();
            }
            dirty = false;
            aiNote = null; // saved: it is the user's macro now
            SetMessage(Loc.T("mmsg.saved"), Theme.Success);
            RefreshMacroList();
            UpdateTitle();
            return true;
        }

        /// <summary>Asks for a name whose file does not exist yet (other than the current one); null = cancelled.</summary>
        private string AskName(string initial)
        {
            while (true)
            {
                string name = Dialogs.Prompt(this, Loc.T("ask.name"), initial);
                if (name == null) return null;
                name = name.Trim();
                if (name.Length == 0) continue;
                string file = MacroFile.PathFor(folder, name);
                bool same = path != null && MacroFile.SamePath(file, path);
                if (!same && File.Exists(file))
                {
                    Dialogs.Ask(this, Loc.T("mmsg.nameTaken"), Loc.T("ask.ok"));
                    initial = name;
                    continue;
                }
                return name;
            }
        }

        private void RenameMacro()
        {
            if (Busy) return;
            if (path == null)
            {
                Save();
                return;
            }
            string name = AskName(macro.Name.Length > 0 ? macro.Name : Path.GetFileNameWithoutExtension(path));
            if (name == null) return;
            string target = MacroFile.PathFor(folder, name);
            try
            {
                if (!MacroFile.SamePath(target, path))
                    File.Move(path, target);
                path = target;
                macro.Name = name;
                MacroFile.Save(macro, path);
                dirty = false;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                SetMessage(Loc.F("mmsg.saveFailed", error.Message), Theme.Danger);
            }
            RefreshMacroList();
            UpdateTitle();
        }

        private void DeleteMacro()
        {
            if (Busy) return;
            if (path == null)
            {
                Open(new Macro(), null);
                return;
            }
            if (Dialogs.Ask(this, Loc.F("ask.delete", DisplayName), ButtonKind.Danger, Loc.T("studio.delete"), Loc.T("ask.cancel")) != 0) return;
            try
            {
                FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is OperationCanceledException)
            {
                SetMessage(Loc.F("mmsg.saveFailed", error.Message), Theme.Danger);
                return;
            }
            Open(new Macro(), null);
        }

        // ------------------------------------------------------------------ recording

        internal void BeginRecording()
        {
            if (Busy) return;
            CommitDetail();
            if (host != null && host.ClickerBusy)
            {
                Refuse(Loc.T("mmsg.clickerBusy"));
                return;
            }
            bool append = false;
            if (macro.Steps.Count > 0)
            {
                int answer = Dialogs.Ask(this, Loc.T("ask.replace"), Loc.T("ask.replace.yes"), Loc.T("ask.replace.append"), Loc.T("ask.cancel"));
                if (answer < 0 || answer == 2) return;
                append = answer == 1;
            }
            AppWindow chosen = Pick(Loc.T("picker.record"), ListWindows(null));
            if (chosen == null || !Accessible(chosen)) return;
            if (append && macro.ExeName.Length > 0 && !chosen.ExeName.Equals(macro.ExeName, StringComparison.OrdinalIgnoreCase))
            {
                Refuse(Loc.F("mmsg.otherApp", AppName));
                return;
            }
            if (!WindowLock.BringToFront(chosen.Handle))
            {
                Refuse(Loc.F("mmsg.front", chosen.DisplayName));
                return;
            }
            recordWindow = chosen;
            recordAppend = append;
            recordStartAt = Stopwatch.GetTimestamp() + CountdownMs * Stopwatch.Frequency / 1000;
            lastBeepSecond = -1;
            message = null;
            RefreshAll();
        }

        // V-Clicker's own hotkeys: never recorded, and never pressed by a macro.
        private Hotkey[] Hotkeys => new[] { settings.ToggleKey, settings.StopKey, settings.CaptureKey };

        private void StartRecorder()
        {
            recordStartAt = 0;
                        recorder = new MacroRecorder(recordWindow.Handle, Hotkeys, AcceptInjectedInput);
            try
            {
                recorder.Start();
            }
            catch (InvalidOperationException error)
            {
                recorder.Dispose();
                recorder = null;
                recordWindow = null;
                Refuse(Loc.F("mmsg.hooks", error.Message));
                return;
            }
            recordWasPaused = false;
            host?.Beep(true);
        }

        private void FinishRecording()
        {
            List<MacroStep> steps = recorder.Stop();
            recorder.Dispose();
            recorder = null;
            AppWindow window = recordWindow;
            recordWindow = null;
            host?.Beep(false);
            if (steps.Count == 0)
            {
                SetMessage(Loc.F("mstop.nothing", window.DisplayName), Theme.Warning);
            }
            else
            {
                Size size;
                using (MacroTarget.PerMonitorDpi()) size = WindowLock.ClientArea(window.Handle).Size;
                Edit(() =>
                {
                    if (!recordAppend)
                    {
                        aiNote = null; // a new recording replaced the proposal
                        macro.Steps = steps;
                        macro.ExeName = window.ExeName;
                        macro.Title = window.Title;
                        macro.ClientSize = size;
                    }
                    else
                    {
                        macro.Steps.AddRange(steps);
                    }
                });
                SetMessage(Loc.F("mstop.recorded", steps.Count), Theme.Success);
            }
            BringBack();
        }

        // ------------------------------------------------------------------ running

        private string AppName => macro.ExeName.Length > 0 ? Path.GetFileNameWithoutExtension(macro.ExeName) : "?";

        internal void BeginRun()
        {
            if (Busy) return;
            CommitDetail();
            if (host != null && host.ClickerBusy)
            {
                Refuse(Loc.T("mmsg.clickerBusy"));
                return;
            }
            if (macro.Steps.Count == 0 || macro.ExeName.Length == 0)
            {
                Refuse(Loc.T("mmsg.noSteps"));
                return;
            }
            if (macro.HasUnknown)
            {
                Refuse(Loc.T("mstop.Unsupported"));
                return;
            }
            int pending = macro.Steps.FindIndex(s => s.WaitsForPoint);
            if (pending >= 0)
            {
                list.SelectedIndex = pending;
                Refuse(Loc.F("mmsg.needsPoint", pending + 1));
                return;
            }
            // Windows fires V-Clicker's hotkeys for keys a macro presses too: F8 in a macro would stop it.
                        for (int i = 0; i < macro.Steps.Count; i++)
            {
                MacroStep step = macro.Steps[i];
                if (step.Kind != StepKind.Key && step.Kind != StepKind.Shortcut) continue;
                Hotkey pressed = Hotkey.FromKeyData(step.Key);
                if (!Hotkeys.Any(h => h.Equals(pressed))) continue;
                list.SelectedIndex = i;
                Refuse(Loc.F("mmsg.hotkeyStep", i + 1, pressed));
                return;
            }
            // {{variables}}: the command line's values, the file's, then asked for (and remembered for this session).
            Dictionary<string, string> values = MacroVariables.Resolve(macro, CommandValues ?? new Dictionary<string, string>());
            CommandValues = null;
            foreach (string name in MacroVariables.Missing(macro, values))
            {
                string value = Dialogs.Prompt(this, Loc.F("ask.variable", name), rememberedValues.TryGetValue(name, out string last) ? last : string.Empty);
                if (value == null) return;
                values[name] = rememberedValues[name] = value;
            }
            if (!Reviewed()) return;
            List<AppWindow> windows = ListWindows(macro.ExeName);
            if (windows.Count == 0)
            {
                Refuse(Loc.F("mmsg.openApp", AppName));
                return;
            }
            AppWindow chosen = ChooseWindow(windows);
            if (chosen == null || !Accessible(chosen)) return;

            Size now;
            using (MacroTarget.PerMonitorDpi()) now = WindowLock.ClientArea(chosen.Handle).Size;
            if (!macro.ClientSize.IsEmpty && now != macro.ClientSize)
            {
                string Format(Size s) => s.Width.ToString(CultureInfo.CurrentCulture) + "\u00D7" + s.Height.ToString(CultureInfo.CurrentCulture);
                int answer = Dialogs.Ask(this, Loc.F("ask.size", chosen.DisplayName, Format(macro.ClientSize), Format(now)),
                    Loc.T("ask.size.yes"), Loc.T("ask.cancel"));
                if (answer != 0) return;
            }
            if (!WindowLock.BringToFront(chosen.Handle))
            {
                Refuse(Loc.F("mmsg.front", chosen.DisplayName));
                return;
            }
            IntPtr handle = chosen.Handle;
            uint process = chosen.ProcessId;
            runWindow = chosen;
            lastBeepSecond = -1;
            message = null;
            if (!player.Start(macro, step => MacroTarget.Check(handle, process, step), CountdownMs,
                              (step, cancel) => MacroConditions.Read(handle, process, step, cancel),
                              (step, cancel) => SmartTargets.Find(handle, process, step, cancel),
                              (step, look, cancel) => SmartTargets.Confirm(handle, process, step, look, cancel), values))
            {
                runWindow = null;
                return;
            }
            RunLog log = host?.OpenLog();
            log?.Write("run.start", "kind", "macro", "name", DisplayName, "file", path, "app", macro.ExeName, "repeat", macro.Repeat,
                       "steps", macro.Steps.Count, "limitMinutes", macro.LimitMinutes);
            System.Threading.Volatile.Write(ref runLog, log);
            runClock = Stopwatch.StartNew();
            RefreshAll();
        }

        /// <summary>This macro's safety approval: its content and this local copy (the file, or this session for an unsaved one).</summary>
        private string ApprovalKey(string signature) => SafeClick.ApprovalKey(signature, SafeClick.FileInstance(path) ?? sessionInstance);

        /// <summary>
        /// SafeClick: before running a macro with impactful actions, the safety review, once per version of this copy.
        /// Approving records that the user saw the risks; it never promises the macro is safe.
        /// </summary>
        /// <returns>false = do not run now.</returns>
        private bool Reviewed()
        {
            List<ImpactfulAction> impactful = SafeClick.Impactful(macro);
            if (impactful.Count == 0) return true;
            string key = ApprovalKey(SafeClick.Signature(macro));
            if (settings.IsApproved(key)) return true;
            ReviewChoice choice = ReviewSafety != null ? ReviewSafety(impactful) : SafetyReview.Ask(this, impactful);
            if (choice == ReviewChoice.Simulate) BeginSimulation();
            if (choice != ReviewChoice.Run) return false;
            settings.Approve(key);
            host?.SaveSettings();
            return true;
        }

        /// <summary>SafeClick: the simulation of the current macro against the app as it is now (nothing is sent to it).</summary>
        internal void BeginSimulation()
        {
            if (Busy) return;
            CommitDetail();
            if (macro.Steps.Count == 0)
            {
                Refuse(Loc.T("mmsg.noSteps"));
                return;
            }
            IntPtr handle = IntPtr.Zero;
            uint process = 0;
            List<AppWindow> windows = macro.ExeName.Length > 0 ? ListWindows(macro.ExeName) : new List<AppWindow>();
            if (windows.Count > 0) // none open: the simulation still lists the steps and their risks
            {
                AppWindow chosen = ChooseWindow(windows);
                if (chosen == null || !Accessible(chosen)) return;
                handle = chosen.Handle;
                process = chosen.ProcessId;
            }
            Macro copy = macro.Clone();
            simulating = true;
            UpdateEnabled();
            try
            {
                using (var dialog = new SimulationDialog(copy, cancel => MacroPreview.Evaluate(copy, handle, process, WindowShot.Take, cancel),
                                                         list.SelectedIndex))
                {
                    dialog.ShowDialog(this);
                    if (dialog.Step < macro.Steps.Count) list.SelectedIndex = dialog.Step;
                }
            }
            finally { simulating = false; }
            UpdateEnabled();
        }

        // Writes the run's last record and closes its log; false = no run was being logged.
        private bool EndRunLog(MacroStopReason reason)
        {
            RunLog log = System.Threading.Volatile.Read(ref runLog);
            if (log == null) return false;
            log.Write("run.stop", "kind", "macro", "reason", reason.ToString(), "iteration", player.Iteration,
                      "step", player.StepIndex + 1, "elapsedMs", runClock?.ElapsedMilliseconds ?? 0L);
            System.Threading.Volatile.Write(ref runLog, null);
            return true;
        }

        private void OnPlayerStopped(MacroStopReason reason)
        {
            if (player.Phase != MacroPhase.Idle) return; // a new run already started
            EndRunLog(reason);
            string name = runWindow?.DisplayName ?? AppName;
            runWindow = null;
            shownStep = -1;
            bool fine = reason == MacroStopReason.Completed || reason == MacroStopReason.Manual;
            string text = Loc.F("mstop." + reason, name, Macro.LimitText(macro.LimitMinutes));
            int at = player.StepIndex;
            if (reason == MacroStopReason.TargetTimeout && at < macro.Steps.Count && macro.Steps[at].Target != null)
                text = TargetStopText(macro.Steps[at], player.LastTarget) + " " + Loc.F("mstop.noClick", at + 1);
            if (reason == MacroStopReason.ConditionTimeout && at < macro.Steps.Count)
            {
                MacroStep step = macro.Steps[at];
                string seconds = MacroStep.Seconds(step.DurationMs);
                switch (step.Condition)
                {
                    case ConditionKind.Element: text = Loc.F("mstop.timeoutElement", step.Text, seconds); break;
                    case ConditionKind.Color: text = Loc.F("mstop.timeoutColor", step.At.X, step.At.Y, seconds); break;
                    default: text = Loc.F("mstop.timeoutWindow", step.Window?.Title ?? string.Empty, seconds); break;
                }
                text += " " + (at + 1 < macro.Steps.Count ? Loc.F("mstop.beforeStep", at + 2) : Loc.T("mstop.lastStep"));
            }
            SetMessage(text, reason == MacroStopReason.Completed ? Theme.Success : fine ? Theme.Dim : Theme.Warning);
            if (fine) host?.Beep(false);
            else host?.ErrorBeep();
            if ((reason == MacroStopReason.Password || reason == MacroStopReason.ConditionTimeout || reason == MacroStopReason.TargetTimeout) &&
                at < macro.Steps.Count)
                list.SelectedIndex = at;
            BringBack();
            RefreshAll();
            host?.MacroRunEnded(reason);
        }

        /// <summary>
        /// AI Macro Builder: choose the app, describe the task, get a proposal. It opens unsaved with a note; nothing runs.
        /// </summary>
        private void BuildWithAi()
        {
            if (Busy || !ConfirmDiscard()) return;
            AppWindow chosen = Pick(Loc.T("picker.ai"), ListWindows(null));
            if (chosen == null) return;
            var bound = new Macro { ExeName = chosen.ExeName, Title = chosen.Title };
            using (MacroTarget.PerMonitorDpi()) bound.ClientSize = WindowLock.ClientArea(chosen.Handle).Size;
            AiProposal proposal;
            using (var dialog = new AiDialog(settings, () => host?.SaveSettings(), chosen.DisplayName, bound))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                proposal = dialog.Result;
            }
            OpenProposal(proposal);
        }

        /// <summary>Shows an AI proposal: unsaved (closing asks), with its note, never run.</summary>
        internal void OpenProposal(AiProposal proposal)
        {
            Open(proposal.Macro, null);
            aiNote = proposal.Note ?? string.Empty;
            dirty = true;
            SetMessage(Loc.T("banner.ai"), Theme.Warning);
            RefreshAll();
        }

        /// <summary>Developer Mode: the command line that runs this macro, on the clipboard.</summary>
        private void CopyCommand()
        {
            CommitDetail();
            if (path == null || dirty)
            {
                Refuse(Loc.T("mmsg.saveFirst"));
                return;
            }
            Dictionary<string, string> defaults = MacroVariables.Defaults(macro);
            string text = CommandLineFor(Application.ExecutablePath, path, MacroVariables.Used(macro).Where(n => !defaults.ContainsKey(n)));
            try
            {
                Clipboard.SetText(text);
                SetMessage(Loc.T("mmsg.commandCopied"), Theme.Success);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                SetMessage(text, Theme.Dim); // the clipboard is busy: show it to copy by hand
            }
        }

        /// <summary>
        /// A PowerShell line that runs the macro, waits for it (V-Clicker is a window app) and shows the exit code.
        /// Each argument is single-quoted ('' inside); the macro path is also double-quoted, as Start-Process joins them.
        /// </summary>
        internal static string CommandLineFor(string exe, string file, IEnumerable<string> variables)
        {
            // PowerShell closes a single-quoted string at ' and at the typographic ‘ ’ ‚ ‛ too: double every one of them.
            string Single(string text) =>
                "'" + System.Text.RegularExpressions.Regex.Replace(text, "['‘’‚‛]", m => m.Value + m.Value) + "'";
            IEnumerable<string> args = new[] { "--macro", "\"" + file + "\"" }
                .Concat(variables.SelectMany(n => new[] { "--var", n + "=" }))
                .Concat(new[] { "--exit-when-done" });
            return "$p = Start-Process -Wait -PassThru -FilePath " + Single(exe) + " -ArgumentList " + string.Join(",", args.Select(Single)) + "; $p.ExitCode";
        }

        /// <summary>Why a click was not made, from the last look for its target.</summary>
        internal static string TargetStopText(MacroStep step, TargetLook look)
        {
            SmartTarget target = step.Target;
            string seconds = MacroStep.Seconds(target.TimeoutMs);
            TargetStatus status = look?.Status ?? TargetStatus.NotFound;
            int count = look?.Count ?? 0;
            if (status == TargetStatus.Found) status = TargetStatus.Unconfirmed; // found, but never twice in place and confirmed
            if (target.Kind == TargetKind.Image)
            {
                if (status == TargetStatus.Disabled || status == TargetStatus.Offscreen) status = TargetStatus.NotFound;
                return Loc.F("mstop.image." + status, seconds, count);
            }
            return Loc.F("mstop.target." + status, target.Name, MacroStep.Lower(Loc.ControlTypeLabel(target.ControlType)), seconds, count);
        }

        // ------------------------------------------------------------------ capturing a point

        private void BeginCapture(int index, CaptureFor mode = CaptureFor.Point)
        {
            if (Busy || index < 0) return;
            CommitDetail();
            List<AppWindow> windows = ListWindows(macro.ExeName);
            if (windows.Count == 0)
            {
                Refuse(Loc.F("mmsg.openApp", AppName));
                return;
            }
            AppWindow chosen = ChooseWindow(windows);
            if (chosen == null) return;
            if (!WindowLock.BringToFront(chosen.Handle))
            {
                Refuse(Loc.F("mmsg.front", chosen.DisplayName));
                return;
            }
            captureWindow = chosen;
            captureIndex = index;
            captureFor = mode;
            message = null;
            RefreshAll();
        }

        // ------------------------------------------------------------------ status

        private bool Accessible(AppWindow target)
        {
            if (!target.Restricted || MainForm.IsElevated()) return true;
            Refuse(Loc.F("msg.lockElevated", target.DisplayName));
            return false;
        }

        private void Refuse(string text)
        {
            SetMessage(text, Theme.Warning);
            host?.ErrorBeep();
            RefreshAll();
        }

        private void SetMessage(string text, Color color)
        {
            message = text;
            messageColor = color;
            flashText = text;
            flashUntil = Stopwatch.GetTimestamp() + FlashMs * Stopwatch.Frequency / 1000;
        }

        /// <summary>The Studio comes back to the front after a recording, a run or a capture.</summary>
        private void BringBack()
        {
            if (!Visible) return;
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        private Color StateDot()
        {
            if (recorder != null) return recorder.Paused ? Theme.Warning : Theme.Danger;
            if (recordStartAt != 0 || captureIndex >= 0 || analyzing != null) return Theme.Warning;
            switch (player.Phase)
            {
                case MacroPhase.Running: return Theme.Success;
                case MacroPhase.Idle: return Theme.Dim;
                default: return Theme.Warning;
            }
        }

        private static string Seconds(double seconds) => Math.Max(1, (int)Math.Ceiling(seconds)).ToString(CultureInfo.CurrentCulture);

        private string StateText()
        {
            if (recordStartAt != 0)
                return Loc.F("mstatus.recCountdown", Seconds((recordStartAt - Stopwatch.GetTimestamp()) / (double)Stopwatch.Frequency));
            if (recorder != null)
                return recorder.Paused ? Loc.F("mstatus.recPaused", recordWindow.DisplayName)
                                       : Loc.F("mstatus.recording", recordWindow.DisplayName, settings.StopKey);
            if (analyzing != null) return Loc.T("mstatus.analyzing");
            if (captureIndex >= 0)
                return captureFor == CaptureFor.Point ? Loc.F("mstatus.capture", captureWindow.DisplayName, settings.CaptureKey)
                     : IsTargetMode(captureFor) ? Loc.F("mstatus.captureTarget", settings.CaptureKey)
                     : Loc.F("mstatus.captureCondition", settings.CaptureKey);
            string app = runWindow?.DisplayName ?? AppName;
            switch (player.Phase)
            {
                case MacroPhase.Countdown: return Loc.F("mstatus.runCountdown", Seconds(player.CountdownRemaining.TotalSeconds));
                case MacroPhase.Running:
                    if (player.Watching && player.StepIndex < macro.Steps.Count)
                        return WatchingText(macro.Steps[player.StepIndex], player.Watched);
                    int step = Math.Min(player.StepIndex + 1, macro.Steps.Count);
                    string name = macro.Name.Length > 0 ? macro.Name : app;
                    return macro.Repeat == 0
                        ? Loc.F("mstatus.runningForever", name, player.Iteration, step, macro.Steps.Count)
                        : Loc.F("mstatus.running", name, player.Iteration, macro.Repeat, step, macro.Steps.Count);
                case MacroPhase.Paused:
                    MacroStep current = player.StepIndex < macro.Steps.Count ? macro.Steps[player.StepIndex] : null;
                    switch (player.PauseCause)
                    {
                        case MacroCheck.AnchorMissing:
                            return current?.Window == null || current.Window.IsMenu ? Loc.T("mstatus.waitMenu")
                                 : Loc.F("mstatus.waitWindow", current.Window.Title);
                        case MacroCheck.PointOutside: return Loc.F("mstatus.outside", app);
                        case MacroCheck.Covered: return Loc.F("mstatus.covered", app);
                        default: return Loc.F("mstatus.waitApp", app);
                    }
                default: return Loc.T("mstatus.ready");
            }
        }

        private static string WatchingText(MacroStep step, TimeSpan watched)
        {
            string seconds = ((int)watched.TotalSeconds).ToString(CultureInfo.CurrentCulture);
            if (step.Target != null)
                return step.Target.Kind == TargetKind.Image ? Loc.F("mstatus.findImage", seconds) : Loc.F("mstatus.findElement", step.Target.Name, seconds);
            switch (step.Condition)
            {
                case ConditionKind.Element: return Loc.F("mstatus.watchElement", step.Text, seconds);
                case ConditionKind.Color: return Loc.F("mstatus.watchColor", step.At.X, step.At.Y, seconds);
                default: return Loc.F("mstatus.watchWindow", step.Window?.Title ?? string.Empty, seconds);
            }
        }

        /// <summary>Every 100 ms: countdowns, the status line, the running step, beeps, the end of an F7 read.</summary>
        private void Tick()
        {
            if (bCommand.Visible != settings.DeveloperMode) bCommand.Visible = settings.DeveloperMode;
            if (analyzing != null && analyzing.IsCompleted && !conditionDialogOpen) FinishAnalysis();
            if (recordStartAt != 0)
            {
                long left = recordStartAt - Stopwatch.GetTimestamp();
                if (left <= 0) StartRecorder();
                else CountdownBeep(left / (double)Stopwatch.Frequency);
            }
            if (player.Phase == MacroPhase.Countdown) CountdownBeep(player.CountdownRemaining.TotalSeconds);

            if (recorder != null && recorder.Paused != recordWasPaused)
            {
                recordWasPaused = recorder.Paused;
                if (recordWasPaused) host?.ErrorBeep();
                else host?.Beep(true);
            }
            MacroPhase phase = player.Phase;
            if (phase != lastPhase)
            {
                if (phase == MacroPhase.Running && lastPhase == MacroPhase.Countdown) host?.Beep(true);
                if (phase == MacroPhase.Paused) host?.ErrorBeep();
                lastPhase = phase;
                UpdateEnabled();
            }
            if (phase != MacroPhase.Idle && player.StepIndex != shownStep)
            {
                shownStep = player.StepIndex;
                if (shownStep < list.Items.Count && (shownStep < list.TopIndex || shownStep >= list.TopIndex + list.ClientSize.Height / Math.Max(1, list.ItemHeight)))
                    list.TopIndex = Math.Max(0, shownStep - 2);
                list.Invalidate();
            }
            UpdateStatus();
        }

        private void CountdownBeep(double secondsLeft)
        {
            int second = (int)Math.Ceiling(secondsLeft);
            if (second == lastBeepSecond) return;
            lastBeepSecond = second;
            if (second > 0 && settings.Sounds) System.Threading.ThreadPool.QueueUserWorkItem(delegate { Native.Beep(660, 60); });
        }

        private void UpdateStatus()
        {
            bool active = BusyOrCapturing;
            status.Dot = active ? StateDot() : messageColor;
            bRecord.LedLit = recorder != null && !recorder.Paused; // the red lamp is on while it records
            status.Text = active ? StateText() : Loc.T("mstatus.ready");
            Size preferred = status.GetPreferredSize(Size.Empty);
            status.Size = new Size(Math.Min(preferred.Width, Theme.Dp(520)), preferred.Height);
            string note = active ? string.Empty : message ?? string.Empty;
            if (statusText.Text != note) statusText.Text = note;
            statusText.ForeColor = messageColor;
            tips.SetToolTip(statusText, note);
        }

        private void UpdateEnabled()
        {
            bool idle = !BusyOrCapturing;
            int index = list.SelectedIndex;
            bRecord.Enabled = bRun.Enabled = idle;
            bSimulate.Enabled = idle && macro.Steps.Count > 0;
            bStop.SubLegend = settings.StopKey.ToString();
            bStop.Enabled = !idle;
            bNew.Enabled = bRename.Enabled = bDelete.Enabled = bSave.Enabled = bAi.Enabled = macroList.Enabled = idle;
            fRepeat.Enabled = idle && !chkForever.Checked;
            chkForever.Enabled = chkLimit.Enabled = idle;
            fLimit.Enabled = idle && chkLimit.Checked;
            bUp.Enabled = idle && index > 0;
            bDown.Enabled = idle && index >= 0 && index < macro.Steps.Count - 1;
            bDuplicate.Enabled = bRemove.Enabled = idle && index >= 0;
            bAddWait.Enabled = idle;
            bAddCondition.Enabled = idle && macro.ExeName.Length > 0;
            bUndo.Enabled = idle && undo.Count > 0;
            bRedo.Enabled = idle && redo.Count > 0;
        }

        private void UpdateTitle()
        {
            Text = Loc.T("studio.open") + " \u2014 " + DisplayName + (dirty ? " *" : string.Empty);
        }

        private void RefreshAll()
        {
            loadingUi = true;
            chkForever.Checked = macro.Repeat == 0;
            fRepeat.Number = Math.Max(1, macro.Repeat);
            chkLimit.Checked = macro.LimitMinutes > 0;
            if (macro.LimitMinutes > 0) fLimit.Number = macro.LimitMinutes;
            loadingUi = false;
            RefreshList();
            RefreshMacroList();
            UpdateBanners();
            ShowDetail();
            UpdateEnabled();
            UpdateTitle();
            UpdateStatus();
        }

        /// <summary>Re-applies texts after the language changes.</summary>
        internal void ApplyLanguage()
        {
            foreach (Action apply in localizers) apply();
            RefreshAll();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Text fields keep their own Ctrl+Z / Delete.
            if (!(ActiveControl is TextBox) && !(ActiveControl is NumberField))
            {
                switch (keyData)
                {
                    case Keys.Control | Keys.Z: Undo(); return true;
                    case Keys.Control | Keys.Y: Redo(); return true;
                    case Keys.Delete: if (list.Focused && bRemove.Enabled) Remove(); return true;
                }
            }
            if (keyData == (Keys.Control | Keys.S)) { if (bSave.Enabled) Save(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Dark scroll bars (Windows 10 1809+; elsewhere the call changes nothing).
            Native.SetWindowTheme(list.Handle, "DarkMode_Explorer", null);
            Native.SetWindowTheme(detail.Handle, "DarkMode_Explorer", null);
            Native.StyleTitleBar(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Rectangle area = Screen.FromControl(this).WorkingArea;
            MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
            if (Width > area.Width || Height > area.Height)
            {
                Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
                Location = area.Location;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                if (Busy) StopAll(MacroStopReason.Manual);
                if (!ConfirmDiscard())
                {
                    e.Cancel = true;
                    return;
                }
            }
            else StopAll(MacroStopReason.Manual);
            // The player's stop is never handled once the window is gone: log it and tell the main window now.
            if (EndRunLog(MacroStopReason.Manual)) host?.MacroRunEnded(MacroStopReason.Manual);
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                tips.Dispose();
                recorder?.Dispose();
                player.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
