using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
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
                    Pump(1400, target, button);
                    True(clicks > stopped, "hotkeys must work while minimized");
                    SendKeys.SendWait("{F6}");
                    Pump(150, target, button);
                    stopped = clicks;
                    Pump(250, target, button);
                    Equal(stopped, clicks, "F6 must toggle clicking off");
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
