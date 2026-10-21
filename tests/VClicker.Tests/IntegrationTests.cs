using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using static VClicker.Tests.Program;

namespace VClicker.Tests
{
    /// <summary>
    /// End-to-end: real window, real global hotkeys (sent with SendKeys), real mouse clicks (SendInput)
    /// received by a throw-away target window. Moves the cursor for a few seconds; do not touch the mouse.
    /// </summary>
    internal static class IntegrationTests
    {
        /// <summary>A topmost window that never takes the focus (to cover a point without changing the foreground).</summary>
        private sealed class NoActivateForm : Form
        {
            public NoActivateForm()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                BackColor = Color.White;
            }

            protected override bool ShowWithoutActivation => true;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Native.POINT point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr window, uint flags);

        private const uint GA_ROOT = 2;

        /// <summary>True while the cursor is over the throw-away target window, so no click can land anywhere else.</summary>
        private static bool CursorOverTarget(Form target)
        {
            Point cursor = Cursor.Position;
            IntPtr hit = WindowFromPoint(new Native.POINT { X = cursor.X, Y = cursor.Y });
            return hit != IntPtr.Zero && GetAncestor(hit, GA_ROOT) == target.Handle;
        }

        private static void Pump(int milliseconds, Form target, Button button)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < milliseconds)
            {
                Rectangle area = button.RectangleToScreen(button.ClientRectangle);
                True(CursorOverTarget(target) && area.Contains(Cursor.Position),
                    "test interrupted: the cursor left the target window (did you touch the mouse, or did another window cover it?) " +
                    "cursor=" + Cursor.Position + " button=" + area + " overTarget=" + CursorOverTarget(target));
                Application.DoEvents();
                Thread.Sleep(5);
            }
        }

        private static void Spin(int milliseconds)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < milliseconds)
            {
                Application.DoEvents();
                Thread.Sleep(5);
            }
        }

        private static IEnumerable<T> FindAll<T>(Control root) where T : Control
        {
            foreach (Control child in root.Controls)
            {
                if (child is T match) yield return match;
                foreach (T nested in FindAll<T>(child)) yield return nested;
            }
        }

        public static void Run()
        {
            Point originalCursor = Cursor.Position;
            string ini = Path.Combine(Path.GetTempPath(), "vclicker-it-" + Guid.NewGuid().ToString("N") + ".ini");
            MainForm app = null;
            Form target = null;
            try
            {
                new ClickSettings { IntervalMs = 20, StartDelaySeconds = 1, Sounds = false }.Save(ini);

                Test("real clicks: F6 starts after the countdown, F8 stops, F6 toggles, works minimized", () =>
                {
                    Rectangle screen = Screen.PrimaryScreen.WorkingArea;
                    app = new MainForm(ini)
                    {
                        StartPosition = FormStartPosition.Manual,
                        Location = new Point(screen.Left + 30, screen.Top + 30)
                    };
                    app.Show();
                    Thread.Sleep(300);
                    Application.DoEvents();

                    int clicks = 0;
                    target = new Form
                    {
                        Text = "V-Clicker test target", Size = new Size(350, 260), StartPosition = FormStartPosition.Manual,
                        TopMost = true, Location = new Point(screen.Left + 560, screen.Top + 30)
                    };
                    var button = new Button { Dock = DockStyle.Fill, Text = "Safe target" };
                    button.Click += delegate { clicks++; };
                    target.Controls.Add(button);
                    target.Show();
                    target.Activate();
                    Application.DoEvents();
                    Rectangle bounds = button.RectangleToScreen(button.ClientRectangle);
                    Cursor.Position = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);

                    SendKeys.SendWait("{F6}");
                    Pump(600, target, button);
                    Equal(EnginePhase.Countdown, app.Engine.Phase, "countdown phase");
                    Equal(0, clicks, "no clicks during the 1 s countdown");
                    Pump(900, target, button);
                    True(clicks >= 5, "expected real clicks after the countdown, got " + clicks);

                    SendKeys.SendWait("{F8}");
                    Pump(150, target, button);
                    int stopped = clicks;
                    Pump(250, target, button);
                    Equal(stopped, clicks, "F8 must stop the clicks");
                    Equal(EnginePhase.Idle, app.Engine.Phase, "idle after F8");

                    SendKeys.SendWait("{F6}");
                    Pump(100, target, button);
                    SendKeys.SendWait("{F8}");
                    Pump(1100, target, button);
                    Equal(stopped, clicks, "F8 must also cancel the countdown");

                    app.WindowState = FormWindowState.Minimized;
                    SendKeys.SendWait("{F6}");
                    Pump(1100, target, button); // 1 s start delay
                    True(clicks > stopped, "hotkeys must work while minimized");
                    // Minimized windows must not slow the loop down (Windows 11 throttles background processes).
                    int before = clicks;
                    Pump(1000, target, button);
                    int rate = clicks - before; // 20 ms interval: ~50 per second
                    True(rate >= 35, "only " + rate + " clicks in 1 s at 20 ms while minimized (expected ~50)");
                    SendKeys.SendWait("{F6}");
                    Pump(150, target, button);
                    stopped = clicks;
                    Pump(250, target, button);
                    Equal(stopped, clicks, "F6 must toggle clicking off");
                });

                Test("hotkey capture: the box receives an already-registered key instead of running its action", () =>
                {
                    app.WindowState = FormWindowState.Normal;
                    app.Activate();
                    Spin(400);
                    HotkeyBox box = FindAll<HotkeyBox>(app).First(b => b.Value.Equals(new Hotkey(Keys.F6)));
                    Rectangle area = box.RectangleToScreen(box.ClientRectangle);
                    Cursor.Position = new Point(area.Left + area.Width / 2, area.Top + area.Height / 2);
                    Spin(150);
                    new SendInputSink().Click(ClickButton.Left, false, null); // a real click on the box
                    Spin(200);
                    True(box.Capturing, "clicking the box should start listening");

                    SendKeys.SendWait("{F6}");
                    Spin(300);
                    Equal(EnginePhase.Idle, app.Engine.Phase, "F6 must not start the clicker while the box is listening");
                    True(!box.Capturing, "the box stops listening after a key");
                    Equal(new Hotkey(Keys.F6), box.Value, "the key was captured");

                    // The global hotkeys are live again afterwards (stopped inside the start delay: no click is sent).
                    SendKeys.SendWait("{F6}");
                    Spin(200);
                    Equal(EnginePhase.Countdown, app.Engine.Phase, "F6 works again after capturing");
                    SendKeys.SendWait("{F8}");
                    Spin(200);
                    Equal(EnginePhase.Idle, app.Engine.Phase, "F8 works again after capturing");
                });

                Test("hotkeys are released when the window closes", () =>
                {
                    app.Close();
                    app = null;
                    var probe = new HotkeyRegistry(IntPtr.Zero);
                    try
                    {
                        True(probe.Register(950, new Hotkey(Keys.F6)), "F6 must be free again");
                        True(probe.Register(951, new Hotkey(Keys.F8)), "F8 must be free again");
                    }
                    finally { probe.UnregisterAll(); }
                });

                Test("window lock: Alt+Tab to another window pauses with no stray click; coming back resumes", () =>
                {
                    string lockIni = Path.Combine(Path.GetTempPath(), "vclicker-it-" + Guid.NewGuid().ToString("N") + ".ini");
                    MainForm locked = null;
                    Form other = null;
                    try
                    {
                        // The throw-away target lives in this test process, so the "app" to lock is the test runner itself.
                        new ClickSettings
                        {
                            IntervalMs = 20, StartDelaySeconds = 0, Sounds = false, Hud = false,
                            WindowLock = true, LockExe = "V-Clicker.Tests.exe", LockName = "Test target"
                        }.Save(lockIni);
                        Rectangle screen = Screen.PrimaryScreen.WorkingArea;
                        locked = new MainForm(lockIni)
                        {
                            StartPosition = FormStartPosition.Manual, Location = new Point(screen.Left + 30, screen.Top + 30)
                        };
                        locked.Show();
                        Spin(300);

                        target?.Dispose();
                        int clicks = 0, strayClicks = 0;
                        target = new Form
                        {
                            Text = "V-Clicker lock target", Size = new Size(350, 260), StartPosition = FormStartPosition.Manual,
                            TopMost = true, Location = new Point(screen.Left + 560, screen.Top + 30)
                        };
                        var button = new Button { Dock = DockStyle.Fill, Text = "Locked target" };
                        button.Click += delegate { clicks++; };
                        target.Controls.Add(button);
                        other = new Form
                        {
                            Text = "V-Clicker other app", Size = new Size(350, 200), StartPosition = FormStartPosition.Manual,
                            TopMost = true, Location = new Point(screen.Left + 560, screen.Top + 330)
                        };
                        var otherButton = new Button { Dock = DockStyle.Fill, Text = "Must never be clicked" };
                        otherButton.Click += delegate { strayClicks++; };
                        other.Controls.Add(otherButton);
                        other.Show();
                        target.Show();
                        target.Activate();
                        Spin(200);
                        Rectangle bounds = button.RectangleToScreen(button.ClientRectangle);
                        Cursor.Position = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);

                        SendKeys.SendWait("{F6}");
                        Pump(400, target, button);
                        True(clicks >= 5, "clicks while the locked window is in front, got " + clicks);

                        // The "Alt+Tab": the cursor stays over the locked window. A click Windows had already queued
                        // when the focus moved can still land and bring the target back (the accepted check-to-send
                        // race), so switch again until the other window keeps the focus.
                        for (int attempt = 0; attempt < 5; attempt++)
                        {
                            other.Activate();
                            Pump(150, target, button);
                            if (WindowLock.Foreground() == other.Handle) break;
                        }
                        Equal(EnginePhase.Paused, locked.Engine.Phase, "paused while another window is in front");
                        int paused = clicks;
                        Pump(500, target, button);
                        Equal(paused, clicks, "no click while paused (a click would also have re-activated the target)");
                        Equal(0, strayClicks, "nothing reached the other window");
                        True(WindowLock.Foreground() == other.Handle, "the other window kept the focus");

                        target.Activate();
                        Pump(400, target, button);
                        Equal(EnginePhase.Clicking, locked.Engine.Phase, "resumed by itself");
                        True(clicks > paused + 5, "clicking again after coming back: " + (clicks - paused));

                        SendKeys.SendWait("{F8}");
                        Pump(200, target, button);
                        Equal(EnginePhase.Idle, locked.Engine.Phase, "F8 stops");

                        // F6 while the locked app is not in front: refused, and nothing is armed for later.
                        // (Every window here belongs to the test runner, so lock a different, absent app instead.)
                        locked.Close();
                        locked.Dispose();
                        new ClickSettings
                        {
                            IntervalMs = 20, StartDelaySeconds = 0, Sounds = false, Hud = false,
                            WindowLock = true, LockExe = "not-running-app.exe", LockName = "Absent app"
                        }.Save(lockIni);
                        locked = new MainForm(lockIni)
                        {
                            StartPosition = FormStartPosition.Manual, Location = new Point(screen.Left + 30, screen.Top + 30)
                        };
                        locked.Show();
                        target.Activate();
                        Spin(300);
                        int before = clicks;
                        SendKeys.SendWait("{F6}");
                        Pump(400, target, button);
                        Equal(EnginePhase.Idle, locked.Engine.Phase, "F6 with another app in front is refused");
                        Equal(before, clicks, "no click after a refused start");
                        Equal(0, strayClicks, "nothing reached the other window");
                    }
                    finally
                    {
                        if (locked != null && !locked.IsDisposed) { locked.Engine.Stop(); locked.Close(); locked.Dispose(); }
                        other?.Dispose();
                        File.Delete(lockIni);
                    }
                });

                Test("macro: records clicks and text in one window only, then replays them after the window moved", () =>
                {
                    Rectangle screen = Screen.PrimaryScreen.WorkingArea;
                    using (var form = new Form
                    {
                        Text = "V-Clicker macro target", Size = new Size(360, 300), StartPosition = FormStartPosition.Manual,
                        TopMost = true, Location = new Point(screen.Left + 520, screen.Top + 40)
                    })
                    using (var other = new Form
                    {
                        Text = "V-Clicker other window", Size = new Size(360, 160), StartPosition = FormStartPosition.Manual,
                        TopMost = true, Location = new Point(screen.Left + 520, screen.Top + 400)
                    })
                    {
                        int clicks = 0, strayClicks = 0;
                        var button = new Button { Dock = DockStyle.Top, Height = 120, Text = "Gerar relatório" };
                        button.Click += delegate { clicks++; };
                        var box = new TextBox { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true };
                        form.Controls.Add(box);
                        form.Controls.Add(button);
                        var otherButton = new Button { Dock = DockStyle.Fill, Text = "Outro app" };
                        otherButton.Click += delegate { strayClicks++; };
                        other.Controls.Add(otherButton);
                        other.Show();
                        form.Show();
                        form.Activate();
                        Spin(300);

                        Point Center(Control c)
                        {
                            Rectangle r = c.RectangleToScreen(c.ClientRectangle);
                            return new Point(r.Left + r.Width / 2, r.Top + r.Height / 2);
                        }

                        var input = new SendInputInputSink();
                        List<MacroStep> steps;
                        using (var recorder = new MacroRecorder(form.Handle, new[] { new Hotkey(Keys.F8) }, acceptInjected: true, isPassword: () => false))
                        {
                            recorder.Start();
                            Spin(100);
                            new SendInputSink().Click(ClickButton.Left, false, Center(button));
                            Spin(350);
                            new SendInputSink().Click(ClickButton.Left, false, Center(otherButton)); // another window: not recorded
                            Spin(350);
                            True(recorder.Paused, "recording pauses while another window is in front");
                            form.Activate();
                            Spin(300);
                            new SendInputSink().Click(ClickButton.Left, false, Center(box));
                            Spin(350);
                            foreach (char c in "Olá") { input.Char(c); Spin(40); }
                            Spin(300);
                            input.Key(Keys.Enter, true);
                            input.Key(Keys.Enter, false);
                            Spin(300);
                            input.Key(Keys.F8, true); // V-Clicker's own hotkey: never recorded
                            input.Key(Keys.F8, false);
                            Spin(200);
                            steps = recorder.Stop();
                        }
                        string actions = string.Join(" | ", steps.Where(s => s.Kind != StepKind.Wait).Select(s => s.Kind + (s.Kind == StepKind.Type ? " " + s.Text : "")));
                        Equal("Click | Click | Type Olá | Key", actions, "recorded steps");
                        Equal(Keys.Enter, steps.Last(s => s.Kind == StepKind.Key).Key, "the Enter key");
                        True(steps.All(s => s.Window == null), "every step is in the bound window");
                        Equal(1, clicks, "one real click while recording");
                        Equal(1, strayClicks, "the other window got its own click");
                        Equal("Olá\r\n", box.Text, "typed while recording");

                        // Replay somewhere else on the screen: the steps are relative to the window.
                        form.Location = new Point(form.Left + 60, form.Top + 30);
                        form.Activate();
                        Spin(300);
                        var macro = new Macro { Repeat = 1, Steps = steps };
                        uint process = MacroTarget.ProcessOf(form.Handle);
                        IntPtr handle = form.Handle;
                        MacroStopReason? reason = null;
                        using (var player = new MacroPlayer(input))
                        {
                            player.Stopped += r => reason = r;
                            True(player.Start(macro, s => MacroTarget.Check(handle, process, s), 0), "started");
                            Stopwatch watch = Stopwatch.StartNew();
                            while (player.Phase != MacroPhase.Idle && watch.ElapsedMilliseconds < 15000) Spin(20);
                            Spin(200);
                        }
                        Equal(MacroStopReason.Completed, reason, "replayed to the end");
                        Equal(2, clicks, "the button was clicked again");
                        Equal("Olá\r\nOlá\r\n", box.Text, "the text was typed again");
                        Equal(1, strayClicks, "nothing reached the other window");
                    }
                });

                Test("conditions: element, colour and dialog read from real windows; disabled or covered is 'not yet'", () =>
                {
                    Rectangle screen = Screen.PrimaryScreen.WorkingArea;
                    using (var form = new Form
                    {
                        Text = "V-Clicker condition target", Size = new Size(360, 260), StartPosition = FormStartPosition.Manual,
                        TopMost = true, Location = new Point(screen.Right - 420, screen.Top + 40)
                    })
                    using (var dialog = new Form { Text = "Salvar como", Size = new Size(240, 140), StartPosition = FormStartPosition.Manual, Location = new Point(screen.Right - 400, screen.Top + 320) })
                    using (var cover = new NoActivateForm { Size = new Size(80, 80), StartPosition = FormStartPosition.Manual, TopMost = true })
                    {
                        var button = new Button { Text = "Baixar PDF", Dock = DockStyle.Top, Height = 80, Enabled = false };
                        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(0x3A, 0x6B, 0xD1) };
                        form.Controls.Add(panel);
                        form.Controls.Add(button);
                        form.Show();
                        form.Activate();
                        Spin(300);
                        IntPtr handle = form.Handle;
                        uint process = MacroTarget.ProcessOf(handle);

                        T OffThread<T>(Func<T> read) // UI Automation into our own window needs this thread pumping
                        {
                            var task = System.Threading.Tasks.Task.Run(read);
                            Stopwatch wait = Stopwatch.StartNew();
                            // Answer our window's messages at once, like a real app's message loop (each read sends several).
                            while (!task.IsCompleted && wait.ElapsedMilliseconds < 5000) { Application.DoEvents(); Thread.Sleep(0); }
                            True(task.IsCompleted, "read finished");
                            return task.Result;
                        }
                        Point Center(Control c)
                        {
                            Rectangle r = c.RectangleToScreen(c.ClientRectangle);
                            return new Point(r.Left + r.Width / 2, r.Top + r.Height / 2);
                        }
                        ConditionRead Read(MacroStep step) => OffThread(() =>
                        {
                            using (MacroTarget.PerMonitorDpi()) return MacroConditions.Read(handle, process, step, CancellationToken.None);
                        });

                        ConditionProbe onButton = OffThread(() => MacroConditions.Capture(handle, Center(button)));
                        True(onButton != null, "the point is in the bound window");
                        Equal("Button", onButton.ControlType, "the button itself, not its text");
                        Equal("Baixar PDF", onButton.ElementName, "its name");
                        var element = new MacroStep
                        {
                            Kind = StepKind.WaitFor, Condition = ConditionKind.Element, At = onButton.At, ControlType = onButton.ControlType,
                            Text = onButton.ElementName, DurationMs = 30000
                        };
                        Equal(ConditionRead.NotYet, Read(element), "disabled = not available yet");
                        button.Enabled = true;
                        Spin(100);
                        Equal(ConditionRead.Met, Read(element), "enabled = available");

                        ConditionProbe onPanel = OffThread(() => MacroConditions.Capture(handle, Center(panel)));
                        True(onPanel.Rgb.HasValue && MacroConditions.Close(onPanel.Rgb.Value, 0x3A6BD1), "panel colour read: " + onPanel.Rgb?.ToString("X6"));
                        var color = new MacroStep { Kind = StepKind.WaitFor, Condition = ConditionKind.Color, At = onPanel.At, Rgb = 0x3A6BD1, DurationMs = 30000 };
                        Equal(ConditionRead.Met, Read(color), "colour met");
                        panel.BackColor = Color.White;
                        Spin(100);
                        Equal(ConditionRead.NotYet, Read(color), "colour changed");
                        panel.BackColor = Color.FromArgb(0x3A, 0x6B, 0xD1);
                        cover.Location = new Point(Center(panel).X - 40, Center(panel).Y - 40);
                        cover.Show();
                        Spin(200);
                        Equal(ConditionRead.NotYet, Read(color), "a window of the same app on top: inconclusive, never met");
                        cover.Hide();
                        Spin(100);

                        var window = new MacroStep
                        {
                            Kind = StepKind.WaitFor, Condition = ConditionKind.Window, DurationMs = 30000,
                            Window = new WindowRef { Class = MacroTarget.ClassName(dialog.Handle), Title = "Salvar como" }
                        };
                        Equal(ConditionRead.NotYet, Read(window), "dialog not open");
                        dialog.Show(form);
                        Spin(200);
                        Equal(ConditionRead.Met, Read(window), "dialog open");
                    }
                });

                Test("macro studio: Record counts down, F8 stops the recording, Run replays it; F6 is refused meanwhile", () =>
                {
                    string studioIni = Path.Combine(Path.GetTempPath(), "vclicker-it-" + Guid.NewGuid().ToString("N"), "settings.ini");
                    Directory.CreateDirectory(Path.GetDirectoryName(studioIni));
                    new ClickSettings { Sounds = false, StartDelaySeconds = 0, IntervalMs = 20 }.Save(studioIni);
                    Rectangle screen = Screen.PrimaryScreen.WorkingArea;
                    MainForm host = null;
                    try
                    {
                        host = new MainForm(studioIni) { StartPosition = FormStartPosition.Manual, Location = new Point(screen.Left + 30, screen.Top + 30) };
                        host.Show();
                        Spin(300);
                        using (var form = new Form
                        {
                            Text = "V-Clicker studio target", Size = new Size(360, 300), StartPosition = FormStartPosition.Manual,
                            TopMost = true, Location = new Point(screen.Right - 420, screen.Top + 40)
                        })
                        {
                            int clicks = 0;
                            var button = new Button { Dock = DockStyle.Top, Height = 120, Text = "Baixar PDF" };
                            button.Click += delegate { clicks++; };
                            var box = new TextBox { Dock = DockStyle.Fill, Multiline = true };
                            form.Controls.Add(box);
                            form.Controls.Add(button);
                            form.Show();
                            Spin(200);

                            MacroStudio studio = host.OpenStudio();
                            studio.Location = new Point(screen.Left + 30, screen.Top + 30);
                            studio.ListWindows = exe => new List<AppWindow> { WindowLock.FromHandle(form.Handle) };
                            studio.PickWindow = (prompt, windows) => windows[0];
                            studio.AcceptInjectedInput = true;
                            Spin(300);

                            Point Center(Control c)
                            {
                                Rectangle r = c.RectangleToScreen(c.ClientRectangle);
                                return new Point(r.Left + r.Width / 2, r.Top + r.Height / 2);
                            }

                            studio.BeginRecording();
                            True(studio.Busy, "counting down to the recording");
                            Equal(form.Handle, WindowLock.Foreground(), "the chosen app was brought to the front");
                            Spin(3400);
                            new SendInputSink().Click(ClickButton.Left, false, Center(button));
                            Spin(300);
                            new SendInputSink().Click(ClickButton.Left, false, Center(box));
                            Spin(300);
                            var input = new SendInputInputSink();
                            foreach (char c in "Oi") { input.Char(c); Spin(40); }
                            Spin(300);
                            SendKeys.SendWait("{F8}"); // the real global hotkey
                            Spin(500);
                            True(!studio.Busy, "F8 ended the recording");
                            string actions = string.Join(" | ", studio.Current.Steps.Where(s => s.Kind != StepKind.Wait).Select(s => s.Kind.ToString()));
                            Equal("Click | Click | Type", actions, "recorded through the Studio");
                            Equal("V-Clicker.Tests.exe", studio.Current.ExeName, "bound to the app");
                            Equal(1, clicks, "one click while recording");

                            studio.BeginRun();
                            True(studio.Busy, "running");
                            Spin(300);
                            SendKeys.SendWait("{F6}");
                            Spin(300);
                            Equal(EnginePhase.Idle, host.Engine.Phase, "the autoclicker does not start while a macro runs");
                            Stopwatch watch = Stopwatch.StartNew();
                            while (studio.Busy && watch.ElapsedMilliseconds < 15000) Spin(20);
                            True(!studio.Busy, "the run ended");
                            Equal(2, clicks, "the macro clicked the button again");
                            Equal("OiOi", box.Text, "and typed the text again");
                            studio.Dispose(); // unsaved on purpose: closing would ask to save
                        }
                    }
                    finally
                    {
                        if (host != null && !host.IsDisposed) { host.Engine.Stop(); host.Close(); host.Dispose(); }
                        Directory.Delete(Path.GetDirectoryName(studioIni), true);
                    }
                });
            }
            finally
            {
                if (app != null && !app.IsDisposed) { app.Engine.Stop(); app.Dispose(); }
                target?.Dispose();
                Cursor.Position = originalCursor;
                File.Delete(ini);
            }
        }
    }
}
