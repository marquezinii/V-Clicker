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
            WindowLockTests();
            MiscTests();
            MacroTests.Run();
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

            Test("hotkey: rejects enum names that are not real keys (hand-edited .ini)", () =>
            {
                foreach (string bad in new[] { "Ctrl+Control", "Alt+Shift", "KeyCode", "Ctrl+Modifiers", "Ctrl+Alt+Alt" })
                    True(!Hotkey.TryParse(bad, out _), "should reject '" + bad + "'");
                True(!new Hotkey((Keys)0x20000, ctrl: true).IsAcceptable, "key value above the virtual-key range");
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
                Interval.Split(3723045, out int h, out int m, out int s, out double ms);
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
                Equal(Interval.DefaultMs, Interval.Clamp(double.NaN), "NaN -> the default, never the fastest");
            });

            Test("interval: fractional milliseconds split and join without loss", () =>
            {
                Interval.Split(1000.0 / 15, out int h, out int m, out int s, out double ms);
                Equal(0, h + m + s, "whole units");
                True(Math.Abs(ms - 66.6666666) < 1e-6, "ms keeps the fraction, got " + ms);
                Equal(1000.0 / 15, Interval.Join(h, m, s, ms), "join gives the exact value back");
                Equal(1000.5, Interval.Join(0, 0, 1, 0.5), "join with a fraction");
                Interval.Split(61000.25, out h, out m, out s, out ms);
                Equal(1, m, "minutes"); Equal(1, s, "seconds"); Equal(0.25, ms, "fraction of 61000.25");
            });

            Test("interval: clicks per second converts both ways and stays in range", () =>
            {
                Equal(1000.0 / 15, Interval.FromCps(15), "15 cps is 66.667 ms, not 67");
                True(Math.Abs(Interval.ClicksPerSecond(Interval.FromCps(15)) - 15) < 1e-9, "round trip");
                Equal(1.0, Interval.FromCps(5000), "above 1000 cps is capped at 1 ms");
                Equal(10000.0, Interval.FromCps(0.01), "below 0.1 cps is raised to 0.1");
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

            Test("settings: negative numbers survive cultures with a Unicode minus sign", () =>
            {
                var original = Thread.CurrentThread.CurrentCulture;
                string path = TempFile();
                try
                {
                    Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("sv-SE");
                    True(new ClickSettings { X = -120, Y = -7 }.Save(path), "save");
                    Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
                    ClickSettings loaded = ClickSettings.Load(path);
                    Equal(-120, loaded.X, "x"); Equal(-7, loaded.Y, "y");
                }
                finally { Thread.CurrentThread.CurrentCulture = original; File.Delete(path); File.Delete(path + ".tmp"); }
            });

            Test("settings: an existing file that cannot be read is never overwritten with defaults", () =>
            {
                string path = TempFile();
                try
                {
                    new ClickSettings { IntervalMs = 77 }.Save(path);
                    ClickSettings loaded;
                    using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) // locked, like an antivirus scan
                        loaded = ClickSettings.Load(path);
                    True(!loaded.Save(path), "Save must refuse while the original could not be read");
                    Equal(77, ClickSettings.Load(path).IntervalMs, "the user's file is untouched");
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

            Test("settings: a fractional interval round-trips exactly, whatever the culture", () =>
            {
                var original = Thread.CurrentThread.CurrentCulture;
                string path = TempFile();
                try
                {
                    Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
                    True(new ClickSettings { IntervalMs = 1000.0 / 15, SpeedInCps = false }.Save(path), "save");
                    True(File.ReadAllText(path).Contains("IntervalMs=66.666"), "invariant decimal point in the file");
                    ClickSettings loaded = ClickSettings.Load(path);
                    Equal(1000.0 / 15, loaded.IntervalMs, "exact value");
                    True(!loaded.SpeedInCps, "speed mode");
                }
                finally { Thread.CurrentThread.CurrentCulture = original; File.Delete(path); File.Delete(path + ".tmp"); }
            });

            Test("settings: old whole-number and bad interval values load safely", () =>
            {
                string path = TempFile();
                try
                {
                    foreach (var pair in new[]
                    {
                        Tuple.Create("100", 100.0), Tuple.Create("NaN", 100.0), Tuple.Create("abc", 100.0),
                        Tuple.Create("66,667", 100.0), Tuple.Create("-5", 1.0), Tuple.Create("Infinity", Interval.MaxMs)
                    })
                    {
                        File.WriteAllLines(path, new[] { "IntervalMs=" + pair.Item1 });
                        Equal(pair.Item2, ClickSettings.Load(path).IntervalMs, "IntervalMs=" + pair.Item1);
                    }
                    True(ClickSettings.Load(path).SpeedInCps, "an old file opens in CPS mode");
                }
                finally { File.Delete(path); }
            });

            Test("settings: Window Lock and indicator fields round-trip; bad values are repaired", () =>
            {
                string path = TempFile();
                try
                {
                    var saved = new ClickSettings
                    {
                        WindowLock = true, LockExe = "RobloxPlayerBeta.exe", LockName = "Roblox", LockStopOnFocusLoss = true,
                        Hud = false, HudCorner = HudCorner.BottomLeft
                    };
                    True(saved.Save(path), "save");
                    ClickSettings loaded = ClickSettings.Load(path);
                    Equal(string.Join("|", saved.Serialize()), string.Join("|", loaded.Serialize()), "round trip");

                    var defaults = new ClickSettings();
                    True(!defaults.WindowLock && defaults.Hud && defaults.HudCorner == HudCorner.TopRight && !defaults.LockStopOnFocusLoss,
                        "defaults: lock off, pause and resume, indicator on, top right");

                    foreach (string bad in new[] { "..\\evil.exe", "a/b.exe", "c:\\x.exe", "notes.txt", ".exe", "  ", "bad|name.exe" })
                    {
                        var s = new ClickSettings { LockExe = bad, LockName = "x" };
                        s.Validate();
                        Equal("", s.LockExe, "rejected exe '" + bad + "'");
                    }
                    var named = new ClickSettings { LockExe = "Game.exe", LockName = "  " };
                    named.Validate();
                    Equal("Game", named.LockName, "missing name falls back to the exe name");
                    var corner = new ClickSettings { HudCorner = (HudCorner)9 };
                    corner.Validate();
                    Equal(HudCorner.TopRight, corner.HudCorner, "unknown corner");
                }
                finally { File.Delete(path); File.Delete(path + ".tmp"); }
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

            Test("buttons: UI segment order matches the enum and each button maps to its own Win32 flags", () =>
            {
                // MainForm casts the Segmented index (Left, Middle, Right) straight to ClickButton.
                Equal(0, (int)ClickButton.Left, "Left"); Equal(1, (int)ClickButton.Middle, "Middle"); Equal(2, (int)ClickButton.Right, "Right");
                SendInputSink.Flags(ClickButton.Left, out uint down, out uint up);
                Equal(Native.MOUSEEVENTF_LEFTDOWN, down, "left down"); Equal(Native.MOUSEEVENTF_LEFTUP, up, "left up");
                SendInputSink.Flags(ClickButton.Middle, out down, out up);
                Equal(Native.MOUSEEVENTF_MIDDLEDOWN, down, "middle down"); Equal(Native.MOUSEEVENTF_MIDDLEUP, up, "middle up");
                SendInputSink.Flags(ClickButton.Right, out down, out up);
                Equal(Native.MOUSEEVENTF_RIGHTDOWN, down, "right down"); Equal(Native.MOUSEEVENTF_RIGHTUP, up, "right up");
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

            Test("engine: Start() right after Stopped is raised always succeeds (no lost keypress)", () =>
            {
                using (var engine = new ClickEngine(new FakeSink()))
                {
                    var stopped = new AutoResetEvent(false);
                    // A slow Stopped handler keeps the worker thread alive for a moment after the phase turned Idle.
                    engine.Stopped += r => { stopped.Set(); Thread.Sleep(20); };
                    for (int i = 0; i < 20; i++)
                    {
                        True(engine.Start(new ClickPlan { IntervalMs = 1, MaxClicks = 1 }), "Start #" + i + " was refused");
                        True(stopped.WaitOne(2000), "run #" + i + " never reported Stopped");
                    }
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

            Test("engine: Window Lock pauses, then resumes without a burst", () =>
            {
                var sink = new FakeSink();
                var status = LockStatus.Ok;
                using (var engine = new ClickEngine(sink))
                {
                    engine.Start(new ClickPlan { IntervalMs = 10, Lock = at => status });
                    Thread.Sleep(80);
                    True(sink.Count > 0, "clicks while the window is in front");
                    status = LockStatus.NotFocused;
                    Thread.Sleep(60);
                    Equal(EnginePhase.Paused, engine.Phase, "paused");
                    Equal(LockStatus.NotFocused, engine.PauseCause, "pause cause");
                    int paused = sink.Count;
                    Thread.Sleep(300);
                    Equal(paused, sink.Count, "no click lands while another window is in front");
                    status = LockStatus.Ok;
                    Thread.Sleep(ClickEngine.ResumeSettleMs - 60);
                    Equal(paused, sink.Count, "no click until the window has been back for a moment (Alt may still be held)");
                    Thread.Sleep(160);
                    Equal(EnginePhase.Clicking, engine.Phase, "resumed by itself");
                    int resumed = sink.Count - paused;
                    // ~100 ms of clicking at 10 ms = ~10 clicks; replaying the 300 ms pause would add ~30 more.
                    True(resumed >= 3 && resumed <= 16, resumed + " clicks after resuming (missed clicks must be dropped)");
                    engine.Stop();
                    True(engine.WaitForStop(1000), "stop");
                }
            });

            Test("engine: a short focus blip never makes a click come early or ends a strict run", () =>
            {
                var sink = new FakeSink();
                var status = LockStatus.Ok;
                using (var engine = new ClickEngine(sink))
                {
                    StopReason? reason = null;
                    engine.Stopped += r => reason = r;
                    // One click, then a 400 ms gap. A 50 ms blip in the gap must not pull the next click forward,
                    // and in strict mode it is too short to count as losing the focus.
                    engine.Start(new ClickPlan { IntervalMs = 400, StopOnFocusLoss = true, Lock = at => status });
                    Thread.Sleep(60);
                    Equal(1, sink.Count, "first click");
                    status = LockStatus.NotFocused;
                    Thread.Sleep(50);
                    status = LockStatus.Ok;
                    Thread.Sleep(ClickEngine.ResumeSettleMs + 60);
                    Equal(1, sink.Count, "the second click still waits for its 400 ms slot");
                    True(engine.Phase != EnginePhase.Idle, "a blip shorter than the grace does not stop a strict run (" + reason + ")");
                    Thread.Sleep(250);
                    Equal(2, sink.Count, "second click on schedule");
                    engine.Stop();
                    True(engine.WaitForStop(1000), "stop");
                }
            });

            Test("engine: a fault in the lock check stops the run instead of crashing the app", () =>
            {
                using (var engine = new ClickEngine(new FakeSink()))
                    Equal(StopReason.Blocked, RunToEnd(engine, new ClickPlan
                    {
                        IntervalMs = 5, Lock = at => { throw new InvalidOperationException("boom"); }
                    }).Value, "reason");
            });

            Test("engine: an invalid interval is never treated as 'as fast as possible'", () =>
            {
                double freq = System.Diagnostics.Stopwatch.Frequency / 1000.0;
                var random = new Random(1);
                Equal(1.0, Math.Round(ClickEngine.NextIntervalTicks(new ClickPlan { IntervalMs = 0 }, random) / freq, 3), "0 -> 1 ms");
                Equal(1.0, Math.Round(ClickEngine.NextIntervalTicks(new ClickPlan { IntervalMs = -50 }, random) / freq, 3), "negative -> 1 ms");
                Equal(Interval.DefaultMs, Math.Round(ClickEngine.NextIntervalTicks(new ClickPlan { IntervalMs = double.NaN }, random) / freq, 3), "NaN -> default");
            });

            Test("sent rate: exact for steady clicks from 0.1 to 1000 CPS, unknown until two clicks", () =>
            {
                long f = System.Diagnostics.Stopwatch.Frequency;
                foreach (double rate in new[] { 0.1, 1, 15, 1000 })
                {
                    var meter = new RateMeter();
                    double interval = f / rate;
                    double window = Math.Max(2, 3 / rate);
                    True(double.IsNaN(meter.Sample(0, 0, window)), "no clicks yet");
                    True(double.IsNaN(meter.Sample(1, 1000, window)), "one click is not a rate");
                    double last = double.NaN;
                    // UI ticks every 100 ms; the engine reports its click count and the time of its latest click.
                    for (int tick = 1; tick <= 400; tick++)
                    {
                        double now = tick * 0.1 * f;
                        long clicks = 1 + (long)Math.Floor(now / interval);
                        long stamp = 1000 + (long)((clicks - 1) * interval);
                        last = meter.Sample(clicks, stamp, window);
                    }
                    True(Math.Abs(last - rate) / rate < 0.01, rate + " CPS measured as " + last);
                }
                var reset = new RateMeter();
                reset.Sample(5, 5 * f, 2);
                reset.Sample(10, 6 * f, 2);
                reset.Reset();
                True(double.IsNaN(reset.Sample(11, 7 * f, 2)), "after a pause, measure the resumed clicks only");
            });

            Test("engine: Window Lock stop rules (closed window, strict mode, spot outside, pause limit)", () =>
            {
                using (var engine = new ClickEngine(new FakeSink()))
                {
                    Equal(StopReason.TargetClosed, RunToEnd(engine, new ClickPlan { IntervalMs = 5, Lock = at => LockStatus.Closed }).Value, "closed");
                    Equal(StopReason.FocusLost, RunToEnd(engine, new ClickPlan
                    {
                        IntervalMs = 5, StopOnFocusLoss = true, Lock = at => LockStatus.NotFocused
                    }).Value, "strict mode");
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    Equal(StopReason.PauseTimeout, RunToEnd(engine, new ClickPlan
                    {
                        IntervalMs = 5, StopOnFocusLoss = true, PauseLimitMs = 150, Lock = at => LockStatus.PointOutside
                    }).Value, "a spot outside the window pauses even in strict mode, until the limit");
                    True(watch.ElapsedMilliseconds >= 140, "pause limit respected, ended after " + watch.ElapsedMilliseconds + " ms");
                    Equal(0L, engine.Clicks, "never clicked");
                }
            });

            Test("engine: the Window Lock check receives the fixed position", () =>
            {
                Point? seen = null;
                using (var engine = new ClickEngine(new FakeSink()))
                    RunToEnd(engine, new ClickPlan { IntervalMs = 1, MaxClicks = 1, FixedPosition = new Point(7, 8), Lock = at => { seen = at; return LockStatus.Ok; } });
                Equal(new Point(7, 8), seen.Value, "position passed to the lock");
            });

            Test("engine: Stop() during a pause ends at once with the requested reason", () =>
            {
                var sink = new FakeSink();
                using (var engine = new ClickEngine(sink))
                {
                    foreach (StopReason wanted in new[] { StopReason.Manual, StopReason.Suspended })
                    {
                        StopReason? reason = null;
                        var done = new ManualResetEventSlim();
                        Action<StopReason> handler = r => { reason = r; done.Set(); };
                        engine.Stopped += handler;
                        engine.Start(new ClickPlan { IntervalMs = 5, Lock = at => LockStatus.NotFocused });
                        Thread.Sleep(60);
                        Equal(EnginePhase.Paused, engine.Phase, "paused before stopping");
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        engine.Stop(wanted);
                        True(done.Wait(1000), "did not stop");
                        True(watch.ElapsedMilliseconds < 200, "stop took " + watch.ElapsedMilliseconds + " ms");
                        Equal(wanted, reason.Value, "reason");
                        engine.Stopped -= handler;
                    }
                    Equal(0, sink.Count, "no clicks at all");
                }
            });

            Test("engine: the start countdown freezes while the locked window is away", () =>
            {
                var sink = new FakeSink();
                var status = LockStatus.Ok;
                using (var engine = new ClickEngine(sink))
                {
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    StopReason? reason = null;
                    var done = new ManualResetEventSlim();
                    engine.Stopped += r => { reason = r; done.Set(); };
                    engine.Start(new ClickPlan { IntervalMs = 5, MaxClicks = 1, StartDelayMs = 150, Lock = at => status });
                    Thread.Sleep(50);
                    status = LockStatus.NotFocused;
                    Thread.Sleep(60);
                    Equal(EnginePhase.Paused, engine.Phase, "countdown paused");
                    Thread.Sleep(200);
                    Equal(0, sink.Count, "no click while away");
                    status = LockStatus.Ok;
                    True(done.Wait(2000), "finished");
                    Equal(StopReason.Completed, reason.Value, "reason");
                    // 150 ms of countdown + ~200 ms away: the first click cannot come before ~330 ms.
                    True(watch.ElapsedMilliseconds >= 330, "first click after only " + watch.ElapsedMilliseconds + " ms");
                }
            });

            Test("engine: clicking time excludes pauses; session time includes them", () =>
            {
                var status = LockStatus.Ok;
                using (var engine = new ClickEngine(new FakeSink()))
                {
                    engine.Start(new ClickPlan { IntervalMs = 5, Lock = at => status });
                    Thread.Sleep(100);
                    status = LockStatus.NotFocused;
                    Thread.Sleep(300);
                    TimeSpan frozen = engine.Elapsed;
                    Thread.Sleep(100);
                    True(engine.Elapsed - frozen < TimeSpan.FromMilliseconds(30), "clicking time stands still while paused");
                    status = LockStatus.Ok;
                    Thread.Sleep(100);
                    engine.Stop();
                    True(engine.WaitForStop(1000), "stop");
                    True(engine.SessionElapsed >= TimeSpan.FromMilliseconds(580), "session " + engine.SessionElapsed.TotalMilliseconds);
                    True(engine.Elapsed <= engine.SessionElapsed - TimeSpan.FromMilliseconds(300),
                        "clicking " + engine.Elapsed.TotalMilliseconds + " vs session " + engine.SessionElapsed.TotalMilliseconds);
                }
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

        // ------------------------------------------------------------------ window lock

        private static void WindowLockTests()
        {
            Test("window lock: a missing, destroyed or reused window handle counts as closed", () =>
            {
                Equal(LockStatus.Closed, WindowLock.Check(IntPtr.Zero, 1, null), "zero handle");
                IntPtr handle;
                uint self = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                using (var form = new Form { ShowInTaskbar = false })
                {
                    handle = form.Handle;
                    // Windows recycles handles: the same handle owned by another process is not "our" window.
                    Equal(LockStatus.Closed, WindowLock.Check(handle, self + 1, null), "another process id");
                    True(WindowLock.Check(handle, self, null) != LockStatus.Closed, "live window of the right process");
                }
                Equal(LockStatus.Closed, WindowLock.Check(handle, self, null), "destroyed window");
            });

            Test("window lock: describes a window with its exe; never lists V-Clicker's own windows", () =>
            {
                using (var form = new Form { Text = "V-Clicker lock probe", ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) })
                {
                    form.Show();
                    Application.DoEvents();
                    AppWindow self = WindowLock.FromHandle(form.Handle);
                    True(self != null, "described");
                    Equal("V-Clicker.Tests.exe", self.ExeName, "exe file name only");
                    Equal("V-Clicker lock probe", self.Title, "title");
                    True(self.DisplayName.Length > 0, "friendly name");
                    True(!WindowLock.List().Any(w => w.Handle == form.Handle), "own process is filtered out");
                    True(WindowLock.List("no-such-app.exe").Count == 0, "exe filter");
                }
                True(WindowLock.FromHandle(IntPtr.Zero) == null, "no window");
            });

            Test("window lock: a dialog in front resolves to the window that owns it", () =>
            {
                using (var owner = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) })
                using (var dialog = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) })
                {
                    owner.Show();
                    dialog.Show(owner);
                    Equal(owner.Handle, WindowLock.RootOwner(dialog.Handle), "owned dialog -> owner");
                    Equal(owner.Handle, WindowLock.RootOwner(owner.Handle), "a top-level window is its own root");
                    Equal(IntPtr.Zero, WindowLock.RootOwner(IntPtr.Zero), "no window");
                    True(!WindowLock.FromHandle(owner.Handle).Restricted, "our own process can be inspected");
                }
            });

            Test("indicator: a long text is capped and never grows past the screen corner", () =>
            {
                using (var hud = new Hud())
                {
                    hud.Display(Color.Lime, new string('W', 200), new Rectangle(-20000, -20000, 1920, 1080), HudCorner.TopLeft);
                    True(hud.Width <= Theme.Dp(360), "width " + hud.Width);
                }
                var settings = new ClickSettings { LockExe = "Game.exe", LockName = new string('x', 100) };
                settings.Validate();
                Equal(40, settings.LockName.Length, "saved app names are kept short");
            });

            Test("segmented: screen readers get the selected text as the value", () =>
            {
                using (var segmented = new Segmented { Items = new[] { "CPS", "Intervalo" } })
                {
                    segmented.SelectedIndex = 1;
                    Equal("Intervalo", segmented.AccessibilityObject.Value, "value");
                    Equal(AccessibleRole.ComboBox, segmented.AccessibilityObject.Role, "role");
                }
            });

            Test("indicator: never activates, lets clicks through and stays out of Alt+Tab", () =>
            {
                const int Transparent = 0x20, NoActivate = 0x8000000, ToolWindow = 0x80, TopMost = 0x8;
                foreach (int style in new[] { Transparent, NoActivate, ToolWindow, TopMost })
                    True((Hud.ExStyles & style) == style, "extended style 0x" + style.ToString("X"));
                using (var hud = new Hud())
                {
                    IntPtr before = WindowLock.Foreground();
                    hud.Display(Color.Lime, "15 CPS", new Rectangle(-20000, -20000, 800, 600), HudCorner.TopRight);
                    Application.DoEvents();
                    True(hud.Visible, "shown");
                    Equal(before, WindowLock.Foreground(), "showing it did not take the foreground");
                    True(hud.Right == -20000 + 800 - Theme.Dp(16), "top-right corner of the given screen");
                }
            });

            Test("tray: state icons are tray-sized and distinct from the app icon", () =>
            {
                using (var form = new MainForm(TempFile(), new FakeSink()))
                using (Icon green = MainForm.WithDot(form.Icon, Color.Lime))
                {
                    Equal(SystemInformation.SmallIconSize, green.Size, "size");
                    using (Bitmap bitmap = green.ToBitmap())
                    {
                        Color corner = bitmap.GetPixel(bitmap.Width - 4, bitmap.Height - 4);
                        True(corner.G > 200 && corner.R < 100, "the status dot is drawn in the corner: " + corner);
                    }
                }
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

            Test("number field: decimals with either separator; a stand-in text never hides a typed value", () =>
            {
                using (var field = new NumberField { Minimum = 0.1, Maximum = 1000, Decimals = 1 })
                {
                    var box = (TextBox)field.Controls[0];
                    int changes = 0;
                    field.ValueChanged += delegate { changes++; };
                    box.Text = "12,34";
                    field.Commit();
                    Equal(12.3, field.Number, "comma, rounded to one decimal");
                    box.Text = "7.5";
                    field.Commit();
                    Equal(7.5, field.Number, "point");
                    Equal(2, changes, "two edits");

                    field.Number = 5;
                    field.ShowText("< 0,1"); // the speed is below the field's range
                    field.Commit();
                    Equal(2, changes, "leaving the stand-in text untouched is not an edit");
                    box.Text = "5";
                    field.Commit();
                    Equal(3, changes, "typing the stale value still counts as an edit");
                    Equal(5.0, field.Number, "value");
                }
            });

            Test("number field: a stand-in text is never replaced by a stale value", () =>
            {
                using (var field = new NumberField { Minimum = 0.1, Maximum = 1000, Decimals = 1 })
                {
                    var box = (TextBox)field.Controls[0];
                    int changes = 0;
                    field.ValueChanged += delegate { changes++; };
                    field.Number = 15;
                    field.ShowText("< 0,1");
                    box.Text = "";
                    field.Commit();
                    Equal(0, changes, "clearing the box and leaving applies nothing");
                    Equal("< 0,1", box.Text, "the stand-in text comes back");
                    field.ShowText("< 0,1");
                    box.Focus();
                    SendKeys(box, Keys.Down);
                    Equal(1, changes, "stepping from a stand-in always applies what is now shown");
                    Equal(0.1, field.Number, "steps from the bottom of the range");
                }
            });

            Test("speed text: a very slow interval never reads as 0 CPS", () =>
            {
                True(!MainForm.FormatCps(1.0 / 300).StartsWith("0"), "5 min interval: " + MainForm.FormatCps(1.0 / 300));
                Equal(15.0.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture), MainForm.FormatCps(15), "15 CPS");
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
                        form.FitToScreen(area); // what OnShown does; the content may be taller than a 720 px CI screen
                        True(form.Height <= area.Height && form.Width <= area.Width,
                            "window " + form.Size + " does not fit in " + area.Size);
                    }
                }
                finally { File.Delete(path); }
            });
        }

        /// <summary>Delivers a key to a text box the way the keyboard would (KeyDown handlers run).</summary>
        private static void SendKeys(TextBox box, Keys key)
        {
            const int WM_KEYDOWN = 0x100;
            SendMessage(box.Handle, WM_KEYDOWN, (IntPtr)(int)key, IntPtr.Zero);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        private static string Placeholders(string text)
        {
            var found = new SortedSet<string>();
            for (int i = 0; i < 6; i++)
                if (text.Contains("{" + i + "}")) found.Add("{" + i + "}");
            return string.Join(",", found);
        }
    }
}
