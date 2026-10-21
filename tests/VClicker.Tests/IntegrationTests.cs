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
