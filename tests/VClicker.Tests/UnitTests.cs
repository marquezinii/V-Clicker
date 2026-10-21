using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using static VClicker.Tests.Program;

namespace VClicker.Tests
{
    internal static class UnitTests
    {
        public static void Run()
        {
            HotkeyTests();
            IntervalTests();
            SettingsTests();
            EngineTests();
            MiscTests();
        }

        // ------------------------------------------------------------------ hotkeys

        private static void HotkeyTests()
        {
            Test("hotkey: parses F-keys, modifiers and round-trips", () =>
            {
                True(Hotkey.TryParse("F6", out Hotkey f6), "F6");
                Equal(Keys.F6, f6.Key, "key");
                True(Hotkey.TryParse("ctrl+shift+f9", out Hotkey combo), "ctrl+shift+f9");
                Equal("Ctrl+Shift+F9", combo.ToString(), "formatted");
                True(combo.Ctrl && combo.Shift && !combo.Alt, "modifier flags");
                Equal(2u | 4u, combo.Modifiers, "RegisterHotKey mask");
                True(Hotkey.TryParse("Ctrl+1", out Hotkey digit), "digit with modifier");
                Equal("Ctrl+1", digit.ToString(), "digit name");
                True(Hotkey.TryParse("PageUp", out Hotkey page), "PageUp");
                Equal("PageUp", page.ToString(), "PageUp name");
            });

            Test("hotkey: rejects keys that would hijack typing or are not keys", () =>
            {
                foreach (string bad in new[] { "", "   ", "A", "1", "Space", "Enter", "Ctrl", "Shift+A", "Alt+Foo", "Ctrl+", "112", "Foo+F6" })
                    True(!Hotkey.TryParse(bad, out _), "should reject '" + bad + "'");
                True(!new Hotkey(Keys.ControlKey).IsAcceptable, "modifier alone");
                True(!new Hotkey(Keys.LButton).IsAcceptable, "mouse button");
                True(!default(Hotkey).IsAcceptable, "empty");
                True(new Hotkey(Keys.A, ctrl: true).IsAcceptable, "Ctrl+A");
            });

            Test("hotkey: FromKeyData splits key and modifiers", () =>
            {
                Hotkey hotkey = Hotkey.FromKeyData(Keys.F6 | Keys.Control | Keys.Alt);
                Equal(Keys.F6, hotkey.Key, "key");
                True(hotkey.Ctrl && hotkey.Alt && !hotkey.Shift, "modifiers");
            });

            Test("hotkey registry: refuses a combination that is already registered, then frees it", () =>
            {
                var combo = new Hotkey(Keys.F23, ctrl: true, alt: true, shift: true);
                var first = new HotkeyRegistry(IntPtr.Zero);
                var second = new HotkeyRegistry(IntPtr.Zero);
                True(first.Register(901, combo), "first registration");
                try
                {
                    True(!second.Register(901, combo), "same id + combo must be refused");
                }
                finally
                {
                    first.UnregisterAll();
                }
                True(second.Register(901, combo), "free again after UnregisterAll");
                second.UnregisterAll();
            });
        }

        // ------------------------------------------------------------------ interval math

        private static void IntervalTests()
        {
            Test("interval: split/join round trip and carry-over", () =>
            {
                Interval.Split(3723045, out int h, out int m, out int s, out int ms);
                Equal(1, h, "hours"); Equal(2, m, "minutes"); Equal(3, s, "seconds"); Equal(45, ms, "ms");
                Equal(3723045, Interval.Join(h, m, s, ms), "join");
                Equal(90000, Interval.Join(0, 0, 90, 0), "90 s carries into minutes");
            });

            Test("interval: clamps to 1 ms .. 24 h and reports clicks per second", () =>
            {
                Equal(1, Interval.Clamp(0), "zero -> 1 ms");
                Equal(1, Interval.Clamp(-50), "negative -> 1 ms");
                Equal(Interval.MaxMs, Interval.Clamp(long.MaxValue), "huge -> max");
                Equal(Interval.MaxMs, Interval.Join(99999, 0, 0, 0), "join does not overflow");
                Equal(10.0, Interval.ClicksPerSecond(100), "100 ms = 10 cps");
                Equal(1000.0, Interval.ClicksPerSecond(1), "1 ms = 1000 cps");
            });
        }

        // ------------------------------------------------------------------ settings

        private static string TempFile() => Path.Combine(Path.GetTempPath(), "vclicker-test-" + Guid.NewGuid().ToString("N") + ".ini");

        private static void SettingsTests()
        {
            Test("settings: Validate clamps every range", () =>
            {
                var s = new ClickSettings
                {
                    IntervalMs = -5, JitterPercent = 99, RepeatCount = -1, X = 99999, Y = -99999,
                    StartDelaySeconds = 99, Language = "xx", Button = (ClickButton)42
                };
                s.Validate();
                Equal(1, s.IntervalMs, "interval"); Equal(50, s.JitterPercent, "jitter"); Equal(0, s.RepeatCount, "repeat");
                Equal(32767, s.X, "x"); Equal(-32768, s.Y, "y"); Equal(30, s.StartDelaySeconds, "delay");
                Equal("auto", s.Language, "language"); Equal(ClickButton.Left, s.Button, "button");
            });

            Test("settings: clashing hotkeys fall back to defaults", () =>
            {
                var s = new ClickSettings { ToggleKey = new Hotkey(Keys.F9), StopKey = new Hotkey(Keys.F9) };
                s.Validate();
                Equal(new Hotkey(Keys.F9), s.ToggleKey, "customised toggle is kept");
                Equal(ClickSettings.DefaultStop, s.StopKey, "duplicate stop reset");

                var all = new ClickSettings
                {
                    ToggleKey = new Hotkey(Keys.F8), StopKey = new Hotkey(Keys.F7), CaptureKey = new Hotkey(Keys.F8)
                };
                all.Validate();
                True(!all.ToggleKey.Equals(all.StopKey) && !all.ToggleKey.Equals(all.CaptureKey) && !all.StopKey.Equals(all.CaptureKey),
                    "hotkeys must be pairwise distinct after Validate");

                var invalid = new ClickSettings { ToggleKey = new Hotkey(Keys.A) };
                invalid.Validate();
                Equal(ClickSettings.DefaultToggle, invalid.ToggleKey, "unacceptable key reset");
            });

            Test("settings: save then load keeps every field", () =>
            {
                string path = TempFile();
                try
                {
                    var saved = new ClickSettings
                    {
                        IntervalMs = 1500, JitterPercent = 15, Button = ClickButton.Right, DoubleClick = true,
                        RepeatCount = 250, UseFixedPosition = true, X = -120, Y = 840, StartDelaySeconds = 3, HoldMode = true,
                        ToggleKey = new Hotkey(Keys.F10, ctrl: true), StopKey = new Hotkey(Keys.Pause), CaptureKey = new Hotkey(Keys.F11),
                        Sounds = false, AlwaysOnTop = true, MinimizeToTray = true, Language = "en"
                    };
                    True(saved.Save(path), "save");
                    ClickSettings loaded = ClickSettings.Load(path);
                    Equal(string.Join("|", saved.Serialize()), string.Join("|", loaded.Serialize()), "round trip");
                    Equal(-120, loaded.X, "negative coordinate");
                    Equal(new Hotkey(Keys.F10, ctrl: true), loaded.ToggleKey, "hotkey with modifier");
                }
                finally { File.Delete(path); File.Delete(path + ".tmp"); }
            });

            Test("settings: overwriting an existing file works and leaves no temp file", () =>
            {
                string path = TempFile();
                try
                {
                    new ClickSettings { IntervalMs = 10 }.Save(path);
                    new ClickSettings { IntervalMs = 20 }.Save(path);
                    Equal(20, ClickSettings.Load(path).IntervalMs, "second save wins");
                    True(!File.Exists(path + ".tmp"), "temp file cleaned up");
                }
                finally { File.Delete(path); File.Delete(path + ".tmp"); }
            });

            Test("settings: missing, corrupt or partial files never throw", () =>
            {
                Equal(100, ClickSettings.Load(TempFile()).IntervalMs, "missing file -> defaults");

                string path = TempFile();
                try
                {
                    File.WriteAllLines(path, new[]
                    {
                        "; comment", "# another", "garbage line", "=novalue", "IntervalMs=abc", "JitterPercent=7",
                        "Button=Sideways", "ToggleKey=Q", "Unknown=1", "StopKey = F9 "
                    });
                    ClickSettings s = ClickSettings.Load(path);
                    Equal(100, s.IntervalMs, "unparsable number keeps default");
                    Equal(7, s.JitterPercent, "valid line still applied");
                    Equal(ClickButton.Left, s.Button, "unknown enum keeps default");
                    Equal(ClickSettings.DefaultToggle, s.ToggleKey, "unacceptable hotkey keeps default");
                    Equal(new Hotkey(Keys.F9), s.StopKey, "whitespace around key/value is trimmed");
                }
                finally { File.Delete(path); }
            });

            Test("settings: saving to an impossible location reports failure instead of throwing", () =>
            {
                string file = TempFile();
                File.WriteAllText(file, "x");
                try
                {
                    True(!new ClickSettings().Save(Path.Combine(file, "nested", "settings.ini")), "save must return false");
                }
                finally { File.Delete(file); }
            });

            Test("settings: ToPlan maps every field", () =>
            {
                ClickPlan plan = new ClickSettings
                {
                    IntervalMs = 250, JitterPercent = 10, Button = ClickButton.Middle, DoubleClick = true,
                    RepeatCount = 7, UseFixedPosition = true, X = 5, Y = 6, StartDelaySeconds = 2
                }.ToPlan();
                Equal(250, plan.IntervalMs, "interval"); Equal(10, plan.JitterPercent, "jitter");
                Equal(ClickButton.Middle, plan.Button, "button"); True(plan.DoubleClick, "double");
                Equal(7L, plan.MaxClicks, "max clicks"); Equal(new Point(5, 6), plan.FixedPosition.Value, "position");
                Equal(2000, plan.StartDelayMs, "delay ms");
                Equal(0, plan.HoldKey, "toggle mode by default");
                True(!new ClickSettings().ToPlan().FixedPosition.HasValue, "no fixed position unless enabled");
            });
        }

        // ------------------------------------------------------------------ engine

        private sealed class FakeSink : IClickSink
        {
            private readonly List<long> stamps = new List<long>();
            public int FailOnClick; // 1-based; 0 = never
            public ClickButton LastButton;
            public bool LastDouble;
            public Point? LastPosition;

            public int Count { get { lock (stamps) return stamps.Count; } }

            public bool Click(ClickButton button, bool doubleClick, Point? at)
            {
                LastButton = button; LastDouble = doubleClick; LastPosition = at;
                lock (stamps)
                {
                    if (FailOnClick != 0 && stamps.Count + 1 == FailOnClick) return false;
                    stamps.Add(System.Diagnostics.Stopwatch.GetTimestamp());
                    return true;
                }
            }
        }

        private static StopReason? RunToEnd(ClickEngine engine, ClickPlan plan, int timeoutMs = 5000)
        {
            StopReason? reason = null;
            var done = new ManualResetEventSlim();
            engine.Stopped += r => { reason = r; done.Set(); };
            True(engine.Start(plan), "Start() should accept the plan");
            True(done.Wait(timeoutMs), "engine did not finish within " + timeoutMs + " ms");
            return reason;
        }

        private static void EngineTests()
        {
            Test("engine: stops with Completed after exactly MaxClicks", () =>
            {
                var sink = new FakeSink();
                using (var engine = new ClickEngine(sink))
                {
                    Equal(StopReason.Completed, RunToEnd(engine, new ClickPlan { IntervalMs = 2, MaxClicks = 5 }).Value, "reason");
                    Equal(5, sink.Count, "sink clicks"); Equal(5L, engine.Clicks, "engine counter");
                    Equal(EnginePhase.Idle, engine.Phase, "phase");
                }
            });

            Test("engine: passes button, double-click and fixed position to the sink", () =>
            {
                var sink = new FakeSink();
                using (var engine = new ClickEngine(sink))
                {
                    RunToEnd(engine, new ClickPlan
                    {
                        IntervalMs = 1, MaxClicks = 1, Button = ClickButton.Right, DoubleClick = true,
                        FixedPosition = new Point(12, 34)
                    });
                    Equal(ClickButton.Right, sink.LastButton, "button"); True(sink.LastDouble, "double");
                    Equal(new Point(12, 34), sink.LastPosition.Value, "position");
                }
            });

            Test("engine: keeps an accurate average rate at 10 ms (absolute schedule, no drift)", () =>
            {
                var sink = new FakeSink();
                using (var engine = new ClickEngine(sink))
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    RunToEnd(engine, new ClickPlan { IntervalMs = 10, MaxClicks = 50 });
                    // 50 clicks = 49 gaps = 490 ms ideal.
                    True(watch.ElapsedMilliseconds >= 470 && watch.ElapsedMilliseconds <= 700,
                        "50 clicks at 10 ms took " + watch.ElapsedMilliseconds + " ms (expected ~490)");
                }
            });

            Test("engine: sustains 1 ms intervals (up to 1000 clicks/s)", () =>
            {
                var sink = new FakeSink();
                using (var engine = new ClickEngine(sink))
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    RunToEnd(engine, new ClickPlan { IntervalMs = 1, MaxClicks = 300 });
                    True(watch.ElapsedMilliseconds >= 290 && watch.ElapsedMilliseconds <= 800,
                        "300 clicks at 1 ms took " + watch.ElapsedMilliseconds + " ms (expected ~299)");
                }
            });

            Test("engine: Stop() ends an unlimited run promptly with reason Manual", () =>
            {
                var sink = new FakeSink();
                using (var engine = new ClickEngine(sink))
                {
                    StopReason? reason = null;
                    var done = new ManualResetEventSlim();
                    engine.Stopped += r => { reason = r; done.Set(); };
                    engine.Start(new ClickPlan { IntervalMs = 5 });
                    Thread.Sleep(120);
                    Equal(EnginePhase.Clicking, engine.Phase, "phase while running");
                    True(engine.Clicks >= 5, "should have clicked a few times, got " + engine.Clicks);
                    True(engine.Elapsed > TimeSpan.Zero, "elapsed advances");
                    engine.Stop();
                    True(done.Wait(1000), "did not stop within 1 s");
                    Equal(StopReason.Manual, reason.Value, "reason");
                    int after = sink.Count;
                    Thread.Sleep(60);
                    Equal(after, sink.Count, "no clicks after stop");
                    True(engine.Elapsed > TimeSpan.Zero, "elapsed is kept after stop");
                }
            });

            Test("engine: a rejected click stops the run as Blocked without counting it", () =>
            {
                var sink = new FakeSink { FailOnClick = 3 };
                using (var engine = new ClickEngine(sink))
                {
                    Equal(StopReason.Blocked, RunToEnd(engine, new ClickPlan { IntervalMs = 2 }).Value, "reason");
                    Equal(2L, engine.Clicks, "only delivered clicks are counted");
                }
            });

            Test("engine: hold mode stops with KeyReleased when the key goes up", () =>
            {
                bool down = true;
                var sink = new FakeSink();
                using (var engine = new ClickEngine(sink, vk => down))
                {
                    StopReason? reason = null;
                    var done = new ManualResetEventSlim();
                    engine.Stopped += r => { reason = r; done.Set(); };
                    engine.Start(new ClickPlan { IntervalMs = 5, HoldKey = (int)Keys.F6 });
                    Thread.Sleep(80);
                    True(sink.Count > 0, "clicks while held");
                    down = false;
                    True(done.Wait(1000), "did not react to key release");
                    Equal(StopReason.KeyReleased, reason.Value, "reason");
                }
            });

            Test("engine: hold mode with the key already up never clicks", () =>
            {
                var sink = new FakeSink();
                using (var engine = new ClickEngine(sink, vk => false))
                {
                    Equal(StopReason.KeyReleased, RunToEnd(engine, new ClickPlan { IntervalMs = 5, HoldKey = (int)Keys.F6 }).Value, "reason");
                    Equal(0, sink.Count, "no clicks");
                }
            });

            Test("engine: start delay is a countdown that Stop() can cancel before any click", () =>
            {
                var sink = new FakeSink();
                using (var engine = new ClickEngine(sink))
                {
                    StopReason? reason = null;
                    var done = new ManualResetEventSlim();
                    engine.Stopped += r => { reason = r; done.Set(); };
                    engine.Start(new ClickPlan { IntervalMs = 5, StartDelayMs = 400 });
                    Thread.Sleep(100);
                    Equal(EnginePhase.Countdown, engine.Phase, "phase");
                    True(engine.CountdownRemaining > TimeSpan.Zero && engine.CountdownRemaining <= TimeSpan.FromMilliseconds(400), "remaining time");
                    Equal(0, sink.Count, "no clicks during countdown");
                    engine.Stop();
                    True(done.Wait(1000), "countdown not cancelled");
                    Equal(StopReason.Manual, reason.Value, "reason");
                    Equal(0, sink.Count, "still no clicks");
                    Equal(TimeSpan.Zero, engine.Elapsed, "no elapsed time without clicking");
                }
            });

            Test("engine: first click comes only after the start delay", () =>
            {
                var sink = new FakeSink();
                using (var engine = new ClickEngine(sink))
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    RunToEnd(engine, new ClickPlan { IntervalMs = 5, MaxClicks = 1, StartDelayMs = 200 });
                    True(watch.ElapsedMilliseconds >= 190, "finished after only " + watch.ElapsedMilliseconds + " ms");
                }
            });

            Test("engine: a second Start() while running is refused; a finished engine can be reused", () =>
            {
                var sink = new FakeSink();
                using (var engine = new ClickEngine(sink))
                {
                    engine.Start(new ClickPlan { IntervalMs = 5 });
                    True(!engine.Start(new ClickPlan { IntervalMs = 5 }), "second Start must be refused");
                    engine.Stop();
                    True(engine.WaitForStop(1000), "stop");
                    RunToEnd(engine, new ClickPlan { IntervalMs = 1, MaxClicks = 3 });
                    Equal(3L, engine.Clicks, "counter restarts on each run");
                }
            });

            Test("engine: jitter stays inside +/- percent and actually varies", () =>
            {
                var random = new Random(1234);
                double freq = System.Diagnostics.Stopwatch.Frequency / 1000.0;
                var plan = new ClickPlan { IntervalMs = 100, JitterPercent = 30 };
                var values = Enumerable.Range(0, 5000).Select(_ => ClickEngine.NextIntervalTicks(plan, random) / freq).ToList();
                True(values.Min() >= 69.9 && values.Max() <= 130.1, "range " + values.Min() + ".." + values.Max());
                True(values.Min() < 75 && values.Max() > 125, "spread covers the range");
                var steady = new ClickPlan { IntervalMs = 100 };
                True(Enumerable.Range(0, 100).All(_ => Math.Abs(ClickEngine.NextIntervalTicks(steady, random) / freq - 100) < 0.01),
                    "no jitter means exact interval");
            });

            Test("engine: dispose while running does not hang", () =>
            {
                var engine = new ClickEngine(new FakeSink());
                engine.Start(new ClickPlan { IntervalMs = 5 });
                var watch = System.Diagnostics.Stopwatch.StartNew();
                engine.Dispose();
                True(watch.ElapsedMilliseconds < 1500, "dispose took " + watch.ElapsedMilliseconds + " ms");
            });
        }

        // ------------------------------------------------------------------ misc

        private static void MiscTests()
        {
            Test("native: INPUT struct has the exact Win32 size for this bitness", () =>
                Equal(IntPtr.Size == 8 ? 40 : 28, Marshal.SizeOf(typeof(Native.INPUT)), "sizeof(INPUT)"));

            Test("localization: every key has both languages and matching placeholders", () =>
            {
                foreach (KeyValuePair<string, string[]> entry in Loc.All)
                {
                    True(entry.Value.Length == 2 && entry.Value.All(t => !string.IsNullOrWhiteSpace(t)), "missing text for " + entry.Key);
                    Equal(Placeholders(entry.Value[0]), Placeholders(entry.Value[1]), "placeholders of " + entry.Key);
                }
            });

            Test("localization: formatting and unknown keys", () =>
            {
                Loc.SetLanguage("en");
                Equal("Starting in 2 s", Loc.F("status.countdown", "2 s"), "english format");
                Loc.SetLanguage("pt");
                Equal("Iniciando em 2 s", Loc.F("status.countdown", "2 s"), "portuguese format");
                Equal("no.such.key", Loc.T("no.such.key"), "unknown key is returned as-is");
                Loc.SetLanguage("auto");
            });

            Test("window: a screen shorter than the window caps the height and scrolls", () =>
            {
                string path = TempFile();
                try
                {
                    using (var form = new MainForm(path, new FakeSink()))
                    {
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new Point(-20000, -20000);
                        IntPtr handle = form.Handle;
                        int tall = form.Height;
                        var shortScreen = new Rectangle(0, 0, 1366, tall - 100);
                        form.FitToScreen(shortScreen);
                        Equal(shortScreen.Height, form.Height, "height is capped to the screen");
                        True(form.AutoScroll, "content must scroll");
                        True(form.Width >= SystemInformation.VerticalScrollBarWidth, "width keeps room for the scroll bar");
                    }
                }
                finally { File.Delete(path); }
            });

            Test("window: builds, loads settings and fits on the screen", () =>
            {
                string path = TempFile();
                try
                {
                    new ClickSettings { IntervalMs = 1500, Language = "en" }.Save(path);
                    using (var form = new MainForm(path, new FakeSink()))
                    {
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new Point(-20000, -20000); // never visible
                        IntPtr handle = form.Handle;
                        True(handle != IntPtr.Zero, "handle");
                        Rectangle area = Screen.PrimaryScreen.WorkingArea;
                        True(form.Height <= area.Height && form.Width <= area.Width,
                            "window " + form.Size + " does not fit in " + area.Size);
                    }
                }
                finally { File.Delete(path); }
            });
        }

        private static string Placeholders(string text)
        {
            var found = new SortedSet<string>();
            for (int i = 0; i < 6; i++)
                if (text.Contains("{" + i + "}")) found.Add("{" + i + "}");
            return string.Join(",", found);
        }
    }
}
