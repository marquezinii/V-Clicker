using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using static VClicker.Tests.Program;

namespace VClicker.Tests
{
    internal static partial class MacroTests
    {
        public static void Run()
        {
            FileTests();
            BuilderTests();
            TargetTests();
            PlayerTests();
            StudioTests();
            ConditionTests();
            SmartTargetTests();
        }

        // ------------------------------------------------------------------ wait until (Conditional Click)

        private static MacroStep WaitForElement(int timeoutMs) => new MacroStep
        {
            Kind = StepKind.WaitFor, Condition = ConditionKind.Element, At = new Point(640, 520), ControlType = "Button",
            Text = "Baixar PDF", DurationMs = timeoutMs
        };

        /// <summary>Runs to the end; returns the reason and the ms from start to the first click (-1 = none).</summary>
        private static MacroStopReason PlayWatching(Macro macro, Func<MacroStep, ConditionRead> condition, out double clickMs,
                                                    Action<MacroPlayer> during = null)
        {
            var sink = new FakeInputSink();
            MacroStopReason? reason = null;
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            using (var player = new MacroPlayer(sink))
            {
                player.Stopped += r => reason = r;
                True(player.Start(macro, Ok, 0, (step, cancel) => condition(step)), "started");
                during?.Invoke(player);
                True(player.WaitForStop(10000), "finished in time");
            }
            string[] log = sink.Snapshot();
            int down = Array.IndexOf(log, "down Left");
            clickMs = down < 0 ? -1 : (sink.StampOf(down) - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            return reason.Value;
        }

        private static void ConditionTests()
        {
            Test("conditions: the three kinds round-trip through the file, original wait included", () =>
            {
                var macro = new Macro();
                macro.Steps.Add(new MacroStep
                {
                    Kind = StepKind.WaitFor, Condition = ConditionKind.Element, At = new Point(640, 520), ControlType = "Button",
                    Text = "Baixar \"PDF\"", DurationMs = 30000, WasMs = 1000, WasRecordedMs = 25000, Reverts = true
                });
                macro.Steps.Add(new MacroStep
                {
                    Kind = StepKind.WaitFor, Condition = ConditionKind.Color, At = new Point(1, 2), Rgb = 0x3A6BD1, DurationMs = 5000,
                    Window = new WindowRef { Class = "#32770", Title = "Salvar como" }
                });
                macro.Steps.Add(new MacroStep
                {
                    Kind = StepKind.WaitFor, Condition = ConditionKind.Window, DurationMs = 3600000,
                    Window = new WindowRef { Class = "#32770", Title = "Salvar como" }
                });
                string text = MacroFile.Write(macro);
                Macro loaded = MacroFile.Read(text);
                Equal(text, MacroFile.Write(loaded), "identical");
                True(!loaded.HasUnknown, "all known");
                Equal(25000, loaded.Steps[0].WasRecordedMs, "the recorded time of the original wait");
                Equal(0x3A6BD1, loaded.Steps[1].Rgb, "colour");
                Equal("Salvar como", loaded.Steps[1].Window.Title, "point anchored in a dialog");
                True(loaded.Steps[0].Describe().Contains("Baixar \"PDF\""), loaded.Steps[0].Describe());
                foreach (string bad in new[]
                {
                    "waitfor element 1 2 \"Button\" \"x\"", "waitfor color 1 2 GGGGGG timeout=1000", "waitfor window \"\" \"x\" timeout=1000",
                    "waitfor element 1 2 \"Button\" \"x\" timeout=1000 speed=2", "waitfor smell timeout=1000"
                })
                    True(MacroFile.Read("V-Clicker macro 1\n[steps]\n" + bad + "\n").HasUnknown, "should stay unknown: " + bad);
                Equal(MacroStep.MinTimeoutMs, MacroFile.Read("V-Clicker macro 1\n[steps]\nwaitfor window \"c\" \"t\" timeout=5\n").Steps[0].DurationMs, "timeout clamped");
            });

            Test("conditions: met twice in a row moves on at once; one flicker does not", () =>
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Macro macro = Steps(1, WaitForElement(5000), new MacroStep { Kind = StepKind.Click });
                Equal(MacroStopReason.Completed, PlayWatching(macro,
                    s => watch.ElapsedMilliseconds >= 300 ? ConditionRead.Met : ConditionRead.NotYet, out double clickMs), "met");
                True(clickMs >= 300 && clickMs < 800, "clicked " + clickMs.ToString("0") + " ms after start (met at 300 ms, not after the 5 s limit)");

                int reads = 0;
                Equal(MacroStopReason.ConditionTimeout, PlayWatching(Steps(1, WaitForElement(1000), new MacroStep { Kind = StepKind.Click }),
                    s => ++reads == 3 ? ConditionRead.Met : ConditionRead.NotYet, out double never), "a single positive read");
                Equal(-1.0, never, "the next step never ran");
            });

            Test("conditions: time away does not count toward the limit; stop and a closed window end the watch", () =>
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                MacroPhase seen = MacroPhase.Idle;
                Equal(MacroStopReason.ConditionTimeout, PlayWatching(Steps(1, WaitForElement(1000)),
                    s => watch.ElapsedMilliseconds < 1200 ? ConditionRead.Away : ConditionRead.NotYet, out _,
                    p => { System.Threading.Thread.Sleep(400); seen = p.Phase; }), "times out after all");
                Equal(MacroPhase.Paused, seen, "paused while another app was in front");
                True(watch.ElapsedMilliseconds >= 2100, "the 1 s limit only ran after the pause: " + watch.ElapsedMilliseconds + " ms");

                var stopWatch = new System.Diagnostics.Stopwatch();
                Equal(MacroStopReason.Manual, PlayWatching(Steps(1, WaitForElement(30000), new MacroStep { Kind = StepKind.Click }),
                    s => { System.Threading.Thread.Sleep(250); return ConditionRead.Met; }, out double late,
                    p => { System.Threading.Thread.Sleep(380); stopWatch.Start(); p.Stop(); }), "stopped");
                Equal(-1.0, late, "a read that came back after the stop did not move the macro on");

                Equal(MacroStopReason.TargetClosed, PlayWatching(Steps(1, WaitForElement(30000)), s => ConditionRead.Closed, out _), "closed");
                Equal(MacroStopReason.Manual, PlayWatching(Steps(1, WaitForElement(30000)), s => ConditionRead.NotYet, out _,
                    p =>
                    {
                        System.Threading.Thread.Sleep(150);
                        var t = System.Diagnostics.Stopwatch.StartNew();
                        p.Stop();
                        True(p.WaitForStop(1000) && t.ElapsedMilliseconds < 100, "F8 while watching: " + t.ElapsedMilliseconds + " ms");
                    }), "stopped while watching");
            });

            Test("conditions in the studio: a wait becomes a condition and back exactly, even after saving", () =>
            {
                string ini = Path.Combine(Path.GetTempPath(), "vclicker-studio-" + Guid.NewGuid().ToString("N"), "settings.ini");
                using (var studio = new MacroStudio(null, new ClickSettings(), ini))
                {
                    Macro macro = Steps(1, new MacroStep { Kind = StepKind.Click, At = new Point(1, 1) },
                        new MacroStep { Kind = StepKind.Wait, DurationMs = 1000, RecordedMs = 25000 },
                        new MacroStep { Kind = StepKind.Click, At = new Point(2, 2) });
                    macro.ExeName = "notepad.exe";
                    studio.Open(macro, null);
                    string original = Describe(studio.Current);

                    studio.ConvertToCondition(1, WaitForElement(30000));
                    MacroStep condition = studio.Current.Steps[1];
                    Equal(StepKind.WaitFor, condition.Kind, "converted");
                    Equal(1000, condition.WasMs, "the wait's duration is kept");
                    Equal(25000, condition.WasRecordedMs, "and its recorded time");

                    studio.Open(MacroFile.Read(MacroFile.Write(studio.Current)), null); // saved and reopened
                    studio.RevertCondition(1);
                    Equal(original, Describe(studio.Current), "back to exactly the original wait");
                    Equal(25000, studio.Current.Steps[1].RecordedMs, "recorded time included");

                    studio.InsertCondition(0, new MacroStep
                    {
                        Kind = StepKind.WaitFor, Condition = ConditionKind.Window, DurationMs = 5000, WasMs = 777, Reverts = true,
                        Window = new WindowRef { Class = "#32770", Title = "Salvar como" }
                    });
                    Equal(4, studio.Current.Steps.Count, "inserted");
                    True(!studio.Current.Steps[0].Reverts, "a condition inserted from scratch has no wait to go back to");
                    studio.RevertCondition(0);
                    Equal(StepKind.WaitFor, studio.Current.Steps[0].Kind, "so it stays a condition");
                    studio.Undo();
                    Equal(original, Describe(studio.Current), "undo");

                    // A wait shortened to 0 s (recorded 8 s) still turns back into exactly itself.
                    studio.Open(Steps(1, new MacroStep { Kind = StepKind.Wait, DurationMs = 0, RecordedMs = 8000 }), null);
                    studio.ConvertToCondition(0, WaitForElement(30000));
                    studio.Open(MacroFile.Read(MacroFile.Write(studio.Current)), null);
                    studio.RevertCondition(0);
                    Equal(StepKind.Wait, studio.Current.Steps[0].Kind, "0 s wait restored");
                    Equal(8000, studio.Current.Steps[0].RecordedMs, "with its recorded time");
                }

                var full = new ConditionProbe
                {
                    At = new Point(5, 5), ElementName = "Baixar PDF", ControlType = "Button", Rgb = 0x3A6BD1,
                    Dialog = new WindowRef { Class = "#32770", Title = "Salvar como" }
                };
                using (var dialog = new MacroStudio.ConditionDialog(full, 30000)) { dialog.CreateControl(); }
                using (var dialog = new MacroStudio.ConditionDialog(new ConditionProbe(), 30000)) { dialog.CreateControl(); }
                True(MacroStudio.ConditionDialog.TypeLabel("Button") == Loc.T("cond.type.Button"), "control types in plain words");
                Equal("Calendar", MacroStudio.ConditionDialog.TypeLabel("Calendar"), "unknown types as they are");
            });

            Test("conditions: colour tolerance", () =>
            {
                True(MacroConditions.Close(0x3A6BD1, 0x3A6BD1 + 0x101010), "within 24 per channel");
                True(!MacroConditions.Close(0x3A6BD1, 0x3A6BD1 + 0x190000), "red 25 off");
                True(!MacroConditions.Close(0x000000, 0x000019), "blue 25 off");
            });
        }

        // ------------------------------------------------------------------ studio window

        private static string Describe(Macro macro) => string.Join(" | ", macro.Steps.Select(s => s.Describe()));

        private static void StudioTests()
        {
            Test("studio: builds empty and with a macro, and fits a small screen", () =>
            {
                string ini = Path.Combine(Path.GetTempPath(), "vclicker-studio-" + Guid.NewGuid().ToString("N"), "settings.ini");
                using (var studio = new MacroStudio(null, new ClickSettings(), ini))
                {
                    Equal(0, studio.Current.Steps.Count, "starts with an empty macro when none is saved");
                    True(!studio.Busy && studio.Indicator == null, "idle");
                    studio.Open(Sample(), null);
                    studio.StartPosition = FormStartPosition.Manual;
                    studio.Location = new Point(-20000, -20000);
                    studio.Show();
                    Application.DoEvents();
                    Rectangle area = Screen.FromControl(studio).WorkingArea;
                    True(studio.Width <= Math.Max(area.Width, 1) && studio.Height <= Math.Max(area.Height, 1), "fits the screen: " + studio.Size);
                    studio.Selected = 5; // a Type step: the detail panel shows its text box
                    Application.DoEvents();
                    studio.Selected = 10; // a shortened wait: the restore button
                    Application.DoEvents();
                }
            });

            Test("studio: edits, pause optimization and restore can all be undone", () =>
            {
                string ini = Path.Combine(Path.GetTempPath(), "vclicker-studio-" + Guid.NewGuid().ToString("N"), "settings.ini");
                using (var studio = new MacroStudio(null, new ClickSettings(), ini))
                {
                    Macro macro = Steps(1,
                        new MacroStep { Kind = StepKind.Click, At = new Point(1, 1) },
                        new MacroStep { Kind = StepKind.Wait, DurationMs = 8000, RecordedMs = 8000 },
                        new MacroStep { Kind = StepKind.Click, At = new Point(2, 2) },
                        new MacroStep { Kind = StepKind.Wait, DurationMs = 25000, RecordedMs = 25000 });
                    studio.Open(macro, null);
                    string original = Describe(studio.Current);
                    Equal(2, studio.Current.LongWaits().Count(), "two long waits are offered");

                    studio.ApplyOptimization(new Dictionary<int, int> { { 3, 1000 } }); // only the 25 s one was ticked
                    Equal(8000, studio.Current.Steps[1].DurationMs, "an unticked wait is never changed");
                    Equal(1000, studio.Current.Steps[3].DurationMs, "the ticked wait is shortened");
                    Equal(25000, studio.Current.Steps[3].RecordedMs, "and remembers what was recorded");
                    True(studio.Dirty, "dirty");
                    studio.RestoreWait(3);
                    Equal(25000, studio.Current.Steps[3].DurationMs, "restored");
                    studio.Undo();
                    Equal(1000, studio.Current.Steps[3].DurationMs, "undo the restore");
                    studio.Undo();
                    Equal(original, Describe(studio.Current), "undo the optimization");
                    studio.Redo();
                    Equal(1000, studio.Current.Steps[3].DurationMs, "redo");
                    studio.Undo();

                    studio.Selected = 0;
                    studio.MoveStep(1);
                    Equal(StepKind.Wait, studio.Current.Steps[0].Kind, "moved down");
                    Equal(1, studio.Selected, "the selection follows the step");
                    studio.Duplicate();
                    studio.Remove();
                    studio.InsertWait();
                    Equal(5, studio.Current.Steps.Count, "a 1 s wait was inserted");
                    Equal(1000, studio.Current.Steps[studio.Selected].DurationMs, "inserted wait");
                    for (int i = 0; i < 5; i++) studio.Undo();
                    Equal(original, Describe(studio.Current), "every edit undone, in order");

                    // A step pressing one of V-Clicker's own hotkeys (F8 would stop the macro) is refused before running.
                    Macro withF8 = Steps(1, new MacroStep { Kind = StepKind.Click }, new MacroStep { Kind = StepKind.Key, Key = Keys.F8 });
                    withF8.ExeName = "notepad.exe";
                    studio.Open(withF8, null);
                    studio.BeginRun();
                    True(!studio.Busy, "not started");
                    Equal(1, studio.Selected, "the offending step is selected");
                }
            });
        }

        // ------------------------------------------------------------------ player

        private sealed class FakeInputSink : IInputSink
        {
            private readonly object gate = new object();
            private readonly List<string> log = new List<string>();
            private readonly List<long> stamps = new List<long>();
            /// <summary>1-based call number to refuse; 0 = never.</summary>
            public int FailOnCall;
            private int calls;

            private bool Add(string entry)
            {
                lock (gate)
                {
                    log.Add(entry);
                    stamps.Add(System.Diagnostics.Stopwatch.GetTimestamp());
                    return ++calls != FailOnCall;
                }
            }

            public bool MoveTo(Point screen) => Add("move " + screen.X + "," + screen.Y);
            public bool Button(ClickButton button, bool down) => Add((down ? "down " : "up ") + button);
            public bool Wheel(int notches) => Add("wheel " + notches);
            public bool Key(Keys code, bool down) => Add((down ? "key+ " : "key- ") + code);
            public bool Char(char c) => Add("char " + c);

            public string[] Snapshot() { lock (gate) return log.ToArray(); }

            public double MsOf(int index) { lock (gate) return (stamps[index] - stamps[0]) * 1000.0 / System.Diagnostics.Stopwatch.Frequency; }

            public long StampOf(int index) { lock (gate) return stamps[index]; }
        }

        private static MacroCheckResult Ok(MacroStep step) => new MacroCheckResult
        {
            Status = MacroCheck.Ok, Screen = new Point(step.At.X + 100, step.At.Y + 100), Origin = new Point(100, 100)
        };

        private static Macro Steps(int repeat, params MacroStep[] steps)
        {
            var macro = new Macro { Repeat = repeat };
            macro.Steps.AddRange(steps);
            return macro;
        }

        /// <summary>Runs to the end (or 10 s) and returns why it stopped.</summary>
        private static MacroStopReason Play(MacroPlayer player, Macro macro, Func<MacroStep, MacroCheckResult> guard)
        {
            MacroStopReason? reason = null;
            Action<MacroStopReason> handler = r => reason = r;
            player.Stopped += handler;
            try
            {
                True(player.Start(macro, guard, 0), "started");
                True(player.WaitForStop(10000), "finished in time");
                return reason.Value;
            }
            finally { player.Stopped -= handler; }
        }

        private static void PlayerTests()
        {
            Test("player: every step kind sends the right input, in order", () =>
            {
                var sink = new FakeInputSink();
                using (var player = new MacroPlayer(sink))
                {
                    Macro macro = Steps(1,
                        new MacroStep { Kind = StepKind.Click, At = new Point(1, 2) },
                        new MacroStep { Kind = StepKind.DoubleClick, Button = ClickButton.Right, At = new Point(3, 4) },
                        new MacroStep { Kind = StepKind.Drag, At = new Point(0, 0), To = new Point(100, 50) },
                        new MacroStep { Kind = StepKind.Hover, At = new Point(5, 6) },
                        new MacroStep { Kind = StepKind.Scroll, Notches = -3, At = new Point(7, 8) },
                        new MacroStep { Kind = StepKind.Type, Text = "Oi" },
                        new MacroStep { Kind = StepKind.Key, Key = Keys.Tab | Keys.Shift, Count = 2 },
                        new MacroStep { Kind = StepKind.Shortcut, Key = Keys.C | Keys.Control });
                    Equal(MacroStopReason.Completed, Play(player, macro, Ok), "reason");
                }
                string log = string.Join(" | ", sink.Snapshot());
                string drag = "move 100,100 | down Left | " +
                              string.Join(" | ", Enumerable.Range(1, 10).Select(k => "move " + (100 + 10 * k) + "," + (100 + 5 * k))) + " | up Left";
                Equal("move 101,102 | down Left | up Left | move 103,104 | down Right | up Right | down Right | up Right | " + drag +
                      " | move 105,106 | move 107,108 | wheel -3 | char O | char i | " +
                      "key+ ShiftKey | key+ Tab | key- Tab | key- ShiftKey | key+ ShiftKey | key+ Tab | key- Tab | key- ShiftKey | " +
                      "key+ ControlKey | key+ C | key- C | key- ControlKey", log, "inputs");
            });

            Test("player: repeats N times on a schedule that does not drift", () =>
            {
                var sink = new FakeInputSink();
                using (var player = new MacroPlayer(sink))
                {
                    Macro macro = Steps(3,
                        new MacroStep { Kind = StepKind.Click }, new MacroStep { Kind = StepKind.Wait, DurationMs = 100 },
                        new MacroStep { Kind = StepKind.Click }, new MacroStep { Kind = StepKind.Wait, DurationMs = 100 });
                    Equal(MacroStopReason.Completed, Play(player, macro, Ok), "reason");
                    Equal(3, player.Iteration, "iterations");
                }
                string[] log = sink.Snapshot();
                var downs = Enumerable.Range(0, log.Length).Where(i => log[i] == "down Left").ToList();
                Equal(6, downs.Count, "clicks");
                // Click, 50 ms gap + 100 ms wait, click, 50 + 100: every click 150 ms after the previous one.
                for (int k = 0; k < downs.Count; k++)
                {
                    double late = sink.MsOf(downs[k]) - k * 150.0;
                    True(late >= -2 && late <= 50, "click " + k + " is " + late.ToString("0.0") + " ms off its slot");
                }
            });

            Test("player: Stop ends a long wait at once and never leaves a key or button down", () =>
            {
                var sink = new FakeInputSink();
                using (var player = new MacroPlayer(sink))
                {
                    MacroStopReason? reason = null;
                    player.Stopped += r => reason = r;
                    player.Start(Steps(1, new MacroStep { Kind = StepKind.Click }, new MacroStep { Kind = StepKind.Wait, DurationMs = 5000 },
                                       new MacroStep { Kind = StepKind.Click }), Ok, 0);
                    System.Threading.Thread.Sleep(150);
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    player.Stop();
                    True(player.WaitForStop(2000), "stopped");
                    True(watch.ElapsedMilliseconds < 100, "stopped in " + watch.ElapsedMilliseconds + " ms");
                    Equal(MacroStopReason.Manual, reason, "reason");
                    Equal(1, sink.Snapshot().Count(e => e == "down Left"), "the second click never happened");
                }

                foreach (int failAt in new[] { 2, 3, 4 }) // refuse Ctrl+C at: C down, C up, Ctrl up
                {
                    var refusing = new FakeInputSink { FailOnCall = failAt };
                    using (var player = new MacroPlayer(refusing))
                        Equal(MacroStopReason.Blocked, Play(player, Steps(1, new MacroStep { Kind = StepKind.Shortcut, Key = Keys.C | Keys.Control }), Ok), "reason");
                    string[] log = refusing.Snapshot();
                    foreach (string key in new[] { "ControlKey", "C" })
                        True(Array.LastIndexOf(log, "key- " + key) > Array.LastIndexOf(log, "key+ " + key),
                             key + " released (refused call " + failAt + "): " + string.Join(", ", log));
                }
                var button = new FakeInputSink { FailOnCall = 3 }; // the button release is refused
                using (var player = new MacroPlayer(button))
                    Equal(MacroStopReason.Blocked, Play(player, Steps(1, new MacroStep { Kind = StepKind.Click }), Ok), "reason");
                Equal("up Left", button.Snapshot().Last(), "the button is released again on the way out");
            });

            Test("player: pauses while the window is away, resumes after it settles; stops when it closes", () =>
            {
                var sink = new FakeInputSink();
                var status = MacroCheck.NotFocused;
                using (var player = new MacroPlayer(sink))
                {
                    MacroStopReason? reason = null;
                    player.Stopped += r => reason = r;
                    player.Start(Steps(1, new MacroStep { Kind = StepKind.Click }, new MacroStep { Kind = StepKind.Type, Text = "ab" }),
                                 s => status == MacroCheck.Ok ? Ok(s) : new MacroCheckResult { Status = status }, 0);
                    System.Threading.Thread.Sleep(150);
                    Equal(MacroPhase.Paused, player.Phase, "paused");
                    Equal(MacroCheck.NotFocused, player.PauseCause, "cause");
                    status = MacroCheck.Covered;
                    System.Threading.Thread.Sleep(80);
                    Equal(MacroCheck.Covered, player.PauseCause, "cause follows the guard");
                    Equal(0, sink.Snapshot().Length, "nothing sent while paused");
                    status = MacroCheck.Ok;
                    System.Threading.Thread.Sleep(ClickEngine.ResumeSettleMs - 80);
                    Equal(0, sink.Snapshot().Length, "nothing sent until the window has been back for a moment");
                    True(player.WaitForStop(3000), "finished");
                    Equal(MacroStopReason.Completed, reason, "reason");
                    Equal("move 100,100 | down Left | up Left | char a | char b", string.Join(" | ", sink.Snapshot()), "inputs");
                }

                var closing = new FakeInputSink();
                using (var player = new MacroPlayer(closing))
                    Equal(MacroStopReason.TargetClosed, Play(player, Steps(1, new MacroStep { Kind = StepKind.Click }),
                        s => new MacroCheckResult { Status = MacroCheck.Closed }), "closed");
                using (var player = new MacroPlayer(closing) { PauseLimitMs = 100 })
                    Equal(MacroStopReason.PauseTimeout, Play(player, Steps(1, new MacroStep { Kind = StepKind.Click }),
                        s => new MacroCheckResult { Status = MacroCheck.NotFocused }), "pause limit");
                Equal(0, closing.Snapshot().Length, "nothing sent");
            });

            Test("player: the window leaving mid-drag releases the button and ends the run", () =>
            {
                var sink = new FakeInputSink();
                int checks = 0;
                using (var player = new MacroPlayer(sink))
                    Equal(MacroStopReason.Interrupted, Play(player, Steps(1, new MacroStep { Kind = StepKind.Drag, To = new Point(100, 0) }),
                        s => ++checks <= 3 ? Ok(s) : new MacroCheckResult { Status = MacroCheck.NotFocused }), "reason");
                string[] log = sink.Snapshot();
                Equal("up Left", log.Last(), "the button is released");
                True(log.Count(e => e.StartsWith("move")) < 11, "the drag did not finish: " + string.Join(", ", log));
            });

            Test("player: a password step stops the run; unknown steps and faults never send anything", () =>
            {
                var sink = new FakeInputSink();
                using (var player = new MacroPlayer(sink))
                {
                    Equal(MacroStopReason.Password, Play(player, Steps(5,
                        new MacroStep { Kind = StepKind.Click }, new MacroStep { Kind = StepKind.Password },
                        new MacroStep { Kind = StepKind.Key, Key = Keys.Enter }), Ok), "password");
                    Equal(1, player.Iteration, "first repetition");
                    Equal(1, player.StepIndex, "stopped at the password step");
                    Equal("move 100,100 | down Left | up Left", string.Join(" | ", sink.Snapshot()), "only the click before it");
                }
                var none = new FakeInputSink();
                using (var player = new MacroPlayer(none))
                {
                    Equal(MacroStopReason.Unsupported, Play(player, Steps(1, new MacroStep { Kind = StepKind.Click },
                        new MacroStep { Kind = StepKind.Unknown, Raw = "waitfor x" }), Ok), "unknown");
                    Equal(MacroStopReason.Completed, Play(player, Steps(0), Ok), "empty macro, even 'until stopped'");
                    Equal(MacroStopReason.Blocked, Play(player, Steps(1, new MacroStep { Kind = StepKind.Click }),
                        s => { throw new InvalidOperationException("boom"); }), "a fault in the guard");
                }
                Equal(0, none.Snapshot().Length, "nothing sent");
            });
        }

        // ------------------------------------------------------------------ bound window

        private static Form OffScreen(string title) => new Form
        {
            Text = title, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000),
            Size = new Size(300, 200)
        };

        private static void TargetTests()
        {
            Test("macro target: a dialog belongs to the bound window; another window of the same app does not", () =>
            {
                using (Form bound = OffScreen("Sistema"))
                using (Form dialog = OffScreen("Salvar como"))
                using (Form other = OffScreen("Discord"))
                {
                    bound.Show();
                    dialog.Show(bound);
                    other.Show();
                    True(MacroTarget.Owns(bound.Handle, bound.Handle), "itself");
                    True(MacroTarget.Owns(bound.Handle, dialog.Handle), "its dialog");
                    True(!MacroTarget.Owns(bound.Handle, other.Handle), "an independent window of the same process");
                    True(!MacroTarget.Owns(bound.Handle, IntPtr.Zero), "no window");

                    Equal(null, MacroTarget.Describe(bound.Handle, bound.Handle), "the bound window needs no reference");
                    WindowRef reference = MacroTarget.Describe(bound.Handle, dialog.Handle);
                    Equal("Salvar como", reference.Title, "dialog title");
                    Equal(dialog.Handle, MacroTarget.FindAnchor(bound.Handle, reference), "found by class and title");
                    Equal(IntPtr.Zero, MacroTarget.FindAnchor(bound.Handle, new WindowRef { Class = reference.Class, Title = "Discord" }),
                          "a same-class window that does not belong to it is never the anchor");
                    Equal(bound.Handle, MacroTarget.FindAnchor(bound.Handle, null), "bound");
                }
            });

            Test("macro target: closed window, missing dialog and a point outside the window", () =>
            {
                IntPtr handle;
                uint process;
                using (Form bound = OffScreen("Sistema"))
                {
                    bound.Show();
                    handle = bound.Handle;
                    process = MacroTarget.ProcessOf(handle);
                    var outside = new MacroStep { Kind = StepKind.Click, At = new Point(5000, 10) };
                    Equal(MacroCheck.PointOutside, MacroTarget.Check(handle, process, outside).Status, "outside the client area");
                    var negative = new MacroStep { Kind = StepKind.Click, At = new Point(-1, 10) };
                    Equal(MacroCheck.PointOutside, MacroTarget.Check(handle, process, negative).Status, "left of the client area");
                    var drag = new MacroStep { Kind = StepKind.Drag, At = new Point(10, 10), To = new Point(5000, 10) };
                    Equal(MacroCheck.PointOutside, MacroTarget.Check(handle, process, drag).Status, "a drag released outside the window");
                    var inDialog = new MacroStep { Kind = StepKind.Click, At = new Point(1, 1), Window = new WindowRef { Class = "#32770", Title = "Salvar como" } };
                    Equal(MacroCheck.AnchorMissing, MacroTarget.Check(handle, process, inDialog).Status, "dialog not open");
                    Equal(MacroCheck.Closed, MacroTarget.Check(handle, process + 1, outside).Status, "another process behind the handle");
                }
                var key = new MacroStep { Kind = StepKind.Key, Key = Keys.Enter };
                Equal(MacroCheck.Closed, MacroTarget.Check(handle, process, key).Status, "closed");
                True(MacroTarget.IsBrowser("Chrome.exe") && MacroTarget.IsBrowser("msedge.exe") && !MacroTarget.IsBrowser("notepad.exe"), "browsers");
            });
        }

        // ------------------------------------------------------------------ recording rules

        private static RecordedEvent Mouse(EventKind kind, int time, int x, int y, ClickButton button = ClickButton.Left, bool inTarget = true) =>
            new RecordedEvent { Kind = kind, Time = time, At = new Point(x, y), Screen = new Point(x + 1000, y + 500), Button = button, InTarget = inTarget };

        private static RecordedEvent KeyEvent(int time, Keys key, string text = null, bool password = false, bool inTarget = true) =>
            new RecordedEvent { Kind = EventKind.KeyDown, Time = time, Key = key, Text = text, Password = password, InTarget = inTarget };

        private static IEnumerable<RecordedEvent> Click(int time, int x, int y, ClickButton button = ClickButton.Left) =>
            new[] { Mouse(EventKind.MouseDown, time, x, y, button), Mouse(EventKind.MouseUp, time + 80, x, y, button) };

        private static List<MacroStep> Build(int stop, params IEnumerable<RecordedEvent>[] groups)
        {
            var builder = new StepBuilder(500);
            foreach (IEnumerable<RecordedEvent> group in groups)
                foreach (RecordedEvent e in group) builder.Add(e);
            return builder.Finish(stop);
        }

        private static IEnumerable<RecordedEvent> One(RecordedEvent e) => new[] { e };

        /// <summary>"Key Enter | Wait 1500 | Click 100,50": kinds, with durations, points or texts.</summary>
        private static string Shape(List<MacroStep> steps) => string.Join(" | ", steps.Select(s =>
        {
            switch (s.Kind)
            {
                case StepKind.Wait: return "Wait " + s.DurationMs;
                case StepKind.Click: case StepKind.DoubleClick: case StepKind.Hover:
                    return s.Kind + " " + s.At.X + "," + s.At.Y;
                case StepKind.Drag: return "Drag " + s.At.X + "," + s.At.Y + ">" + s.To.X + "," + s.To.Y;
                case StepKind.Scroll: return "Scroll " + s.Notches;
                case StepKind.Type: return "Type " + s.Text;
                case StepKind.Key: return "Key " + MacroKeys.Name(s.Key) + (s.Count > 1 ? " x" + s.Count : "");
                case StepKind.Shortcut: return "Shortcut " + MacroKeys.Name(s.Key);
                default: return s.Kind.ToString();
            }
        }));

        private static void BuilderTests()
        {
            Test("recording: the 60-reports scenario becomes exactly the sequence the user did", () =>
            {
                List<MacroStep> steps = Build(12000,
                    One(KeyEvent(0, Keys.Enter)),
                    One(Mouse(EventKind.Move, 700, 300, 300)), One(Mouse(EventKind.Move, 900, 100, 50)), // just travelling
                    Click(1500, 100, 50),
                    One(Mouse(EventKind.Move, 5000, 200, 80)),
                    Click(9600, 200, 80),
                    One(KeyEvent(11000, Keys.Down)));
                Equal("Key Enter | Wait 1500 | Click 100,50 | Wait 8000 | Click 200,80 | Wait 1300 | Key Down | Wait 1000",
                      Shape(steps), "steps");
                Equal(8000, steps[3].RecordedMs, "waits remember the recorded time");
            });

            Test("recording: nothing recorded = no steps; time before the first action is not a wait", () =>
            {
                Equal("", Shape(Build(5000)), "empty");
                Equal("Click 1,1", Shape(Build(3100, Click(3000, 1, 1))), "no leading wait, short trailing gap ignored");
            });

            Test("recording: a hover only where the cursor rested 1 s and nothing happened there", () =>
            {
                Equal("Hover 300,210 | Wait 1500 | Click 500,500", Shape(Build(2600,
                    One(Mouse(EventKind.Move, 1000, 300, 210)), Click(2500, 500, 500))), "rest then click elsewhere");
                Equal("Click 300,212", Shape(Build(2600,
                    One(Mouse(EventKind.Move, 1000, 300, 210)), Click(2500, 300, 212))), "rest then click right there");
                Equal("Click 500,500", Shape(Build(2000,
                    One(Mouse(EventKind.Move, 1000, 300, 210)), Click(1900, 500, 500))), "rest shorter than 1 s");
                Equal("Hover 40,40 | Wait 1600 | Click 500,500", Shape(Build(2900,
                    One(Mouse(EventKind.Move, 0, 10, 10)), One(Mouse(EventKind.Move, 1200, 40, 40)), One(Mouse(EventKind.Move, 1300, 41, 41)),
                    Click(2800, 500, 500))), "two rests in a row keep the last; jitter does not restart it");
                Equal("Click 10,10 | Wait 1900 | Key Enter", Shape(Build(2000,
                    Click(0, 10, 10), One(Mouse(EventKind.Move, 100, 11, 10)), One(KeyEvent(2000, Keys.Enter)))), "resting where it just clicked");
                Equal("Click 1,1 | Wait 4900", Shape(Build(5000, Click(0, 1, 1), One(Mouse(EventKind.Move, 500, 300, 300)))), "a rest at the end leads nowhere");
            });

            Test("recording: double click, drag, and wheel merging", () =>
            {
                Equal("DoubleClick 5,5", Shape(Build(300, Click(0, 5, 5), Click(200, 6, 5))), "double click");
                Equal("Click 5,5 | Wait 500 | Click 5,5", Shape(Build(700, Click(0, 5, 5), Click(580, 5, 5))), "too slow for a double click");
                Equal("Click 5,5 | Click 50,5", Shape(Build(300, Click(0, 5, 5), Click(200, 50, 5))), "too far for a double click");
                Equal("", Shape(Build(400,
                    One(Mouse(EventKind.MouseDown, 0, 10, 10)), One(Mouse(EventKind.Move, 100, 30, 12)),
                    One(Mouse(EventKind.MouseUp, 300, 60, 15, inTarget: false)))), "a drag dropped on another app is not recorded");
                Equal("Drag 10,10>60,15", Shape(Build(400,
                    One(Mouse(EventKind.MouseDown, 0, 10, 10)), One(Mouse(EventKind.Move, 100, 30, 12)),
                    One(Mouse(EventKind.MouseUp, 300, 60, 15)))), "drag");
                var wheel = new List<RecordedEvent>();
                for (int i = 0; i < 3; i++) wheel.Add(new RecordedEvent { Kind = EventKind.Wheel, Time = i * 50, At = new Point(9, 9), Notches = -1, InTarget = true });
                wheel.Add(new RecordedEvent { Kind = EventKind.Wheel, Time = 200, At = new Point(9, 9), Notches = 1, InTarget = true });
                Equal("Scroll -3 | Scroll 1", Shape(Build(200, wheel)), "wheel notches merge; a direction change does not");
            });

            Test("recording: text, keys and shortcuts (Shift, AltGr, repeats, window switching)", () =>
            {
                Equal("Type Olá @ | Wait 1200 | Type x", Shape(Build(2400,
                    One(KeyEvent(0, Keys.O | Keys.Shift, "O")), One(KeyEvent(100, Keys.L, "l")), One(KeyEvent(900, Keys.A, "á")),
                    One(KeyEvent(1000, Keys.Space, " ")), One(KeyEvent(1100, Keys.D2 | Keys.Control | Keys.Alt, "@")),
                    One(KeyEvent(2300, Keys.X, "x")))), "typing merges; a 1 s pause splits; AltGr types");
                Equal("Shortcut Ctrl+C | Key Shift+Tab | Key Down x3", Shape(Build(300,
                    One(KeyEvent(0, Keys.ControlKey | Keys.Control)), One(KeyEvent(10, Keys.C | Keys.Control)),
                    One(KeyEvent(20, Keys.Tab | Keys.Shift, "\t")),
                    One(KeyEvent(30, Keys.Down)), One(KeyEvent(60, Keys.Down)), One(KeyEvent(90, Keys.Down)))),
                    "shortcut, Shift+special key, repeated key");
                Equal("", Shape(Build(300,
                    One(KeyEvent(0, Keys.Menu | Keys.Alt)), One(KeyEvent(10, Keys.Tab | Keys.Alt)), One(KeyEvent(20, Keys.Escape | Keys.Alt)),
                    One(KeyEvent(30, Keys.Tab | Keys.Alt | Keys.Shift)), One(KeyEvent(40, Keys.CapsLock)))),
                    "Alt alone, Alt+Tab, Alt+Esc, Caps Lock are not recorded");
                Equal("Shortcut Alt+F | Type f", Shape(Build(100,
                    One(KeyEvent(0, Keys.F | Keys.Alt, "f")), One(KeyEvent(30, Keys.F, "f")))), "Alt+letter is a shortcut, not text");
                Equal("Type a | Key Backspace | Type b", Shape(Build(100,
                    One(KeyEvent(0, Keys.A, "a")), One(KeyEvent(30, Keys.Back, "\b")), One(KeyEvent(60, Keys.B, "b")))), "Backspace is a key");
            });

            Test("recording: password fields become one 'password not recorded' step", () =>
            {
                List<MacroStep> steps = Build(400,
                    One(KeyEvent(0, Keys.S, "s", password: true)), One(KeyEvent(100, Keys.E, "e", password: true)),
                    One(KeyEvent(200, Keys.Enter)));
                Equal("Password | Key Enter", Shape(steps), "steps");
                True(!steps.Any(s => s.Text.Contains("s") || s.Text.Contains("e")), "the password characters are nowhere");
            });

            Test("recording: other windows record nothing, but their time is a wait; the end is when the user left", () =>
            {
                Equal("Click 1,1 | Wait 3000 | Click 2,2", Shape(Build(3200,
                    Click(0, 1, 1),
                    One(Mouse(EventKind.MouseDown, 1000, 50, 50, inTarget: false)), One(Mouse(EventKind.MouseUp, 1050, 50, 50, inTarget: false)),
                    One(KeyEvent(1100, Keys.A, "a", inTarget: false)),
                    Click(3080, 2, 2))), "outside events ignored");
                Equal("Click 1,1 | Wait 2000", Shape(Build(9000,
                    Click(0, 1, 1), One(new RecordedEvent { Kind = EventKind.Left, Time = 2080 }),
                    One(new RecordedEvent { Kind = EventKind.Back, Time = 5000 }), One(new RecordedEvent { Kind = EventKind.Left, Time = 6000 }))),
                    "trailing wait ends when the user first left the window, not at Stop");
            });

            Test("recording: dead keys compose accents (ABNT2 / US-International)", () =>
            {
                Equal("á", DeadKeys.Compose('´', 'a'), "acute");
                Equal("Ã", DeadKeys.Compose('~', 'A'), "tilde");
                Equal("ê", DeadKeys.Compose('^', 'e'), "circumflex");
                Equal("à", DeadKeys.Compose('`', 'a'), "grave");
                Equal("ü", DeadKeys.Compose('¨', 'u'), "diaeresis");
                Equal("~", DeadKeys.Compose('~', ' '), "accent + space = accent");
                Equal("~x", DeadKeys.Compose('~', 'x'), "no such letter: both characters");
            });
        }

        // ------------------------------------------------------------------ model and file format

        private static Macro Sample()
        {
            var macro = new Macro
            {
                Name = "Relatórios de sexta", ExeName = "chrome.exe", Title = "Sistema - Google Chrome",
                ClientSize = new Size(1280, 720), Repeat = 60
            };
            macro.Steps.AddRange(new[]
            {
                new MacroStep { Kind = StepKind.Click, Button = ClickButton.Left, At = new Point(812, 440) },
                new MacroStep
                {
                    Kind = StepKind.DoubleClick, Button = ClickButton.Right, At = new Point(10, -20),
                    Window = new WindowRef { Class = "#32770", Title = "Salvar \"como\" \\ já" }
                },
                new MacroStep { Kind = StepKind.Drag, Button = ClickButton.Middle, At = new Point(100, 200), To = new Point(400, 200) },
                new MacroStep { Kind = StepKind.Hover, At = new Point(300, 210), Window = new WindowRef { Class = WindowRef.MenuClass } },
                new MacroStep { Kind = StepKind.Scroll, Notches = -3, At = new Point(500, 400) },
                new MacroStep { Kind = StepKind.Type, Text = "Relatório \"mensal\" \\ a\tb\nfim" },
                new MacroStep { Kind = StepKind.Key, Key = Keys.Tab | Keys.Shift, Count = 2 },
                new MacroStep { Kind = StepKind.Key, Key = Keys.Down },
                new MacroStep { Kind = StepKind.Shortcut, Key = Keys.C | Keys.Control },
                new MacroStep { Kind = StepKind.Shortcut, Key = Keys.D1 | Keys.Control | Keys.Alt },
                new MacroStep { Kind = StepKind.Wait, DurationMs = 1000, RecordedMs = 25000 },
                new MacroStep { Kind = StepKind.Wait, DurationMs = 300 },
                new MacroStep { Kind = StepKind.Password }
            });
            return macro;
        }

        private static void FileTests()
        {
            Test("macro file: every step kind round-trips exactly", () =>
            {
                Macro original = Sample();
                string text = MacroFile.Write(original);
                Macro loaded = MacroFile.Read(text);
                Equal(text, MacroFile.Write(loaded), "write(read(write(m)))");
                Equal(original.Name, loaded.Name, "name");
                Equal(original.ExeName, loaded.ExeName, "exe");
                Equal(original.ClientSize, loaded.ClientSize, "client size");
                Equal(60, loaded.Repeat, "repeat");
                Equal(original.Steps.Count, loaded.Steps.Count, "step count");
                for (int i = 0; i < original.Steps.Count; i++)
                    Equal(original.Steps[i].Describe(), loaded.Steps[i].Describe(), "step " + i);
                Equal("Salvar \"como\" \\ já", loaded.Steps[1].Window.Title, "escaped window title");
                Equal("Relatório \"mensal\" \\ a\tb\nfim", loaded.Steps[5].Text, "escaped text");
                Equal(25000, loaded.Steps[10].RecordedMs, "recorded wait");
                True(!loaded.HasUnknown && loaded.HasText && loaded.HasPassword, "flags");
            });

            Test("macro file: saved to disk and loaded back (UTF-8, CRLF tolerated)", () =>
            {
                string folder = Path.Combine(Path.GetTempPath(), "vclicker-macro-" + Guid.NewGuid().ToString("N"));
                try
                {
                    string path = Path.Combine(folder, "a" + MacroFile.Extension);
                    MacroFile.Save(Sample(), path);
                    MacroFile.Save(Sample(), path); // replacing an existing file
                    Equal(MacroFile.Write(Sample()), MacroFile.Write(MacroFile.Load(path)), "disk round trip");
                    File.WriteAllText(path, MacroFile.Write(Sample()).Replace("\n", "\r\n"));
                    Equal(MacroFile.Write(Sample()), MacroFile.Write(MacroFile.Load(path)), "CRLF file");
                }
                finally
                {
                    if (Directory.Exists(folder)) Directory.Delete(folder, true);
                }
            });

            Test("macro file: a file that is not a macro is refused with FormatException", () =>
            {
                foreach (string bad in new[] { "", "hello", "name=x\n[steps]\nclick left 1 2" })
                {
                    bool refused = false;
                    try { MacroFile.Read(bad); }
                    catch (FormatException) { refused = true; }
                    True(refused, "should refuse '" + bad + "'");
                }
            });

            Test("macro file: unknown and malformed lines are kept verbatim and block nothing else", () =>
            {
                string text = "V-Clicker macro 1\nname=x\n[steps]\nclick left 1 2\nwaitfor \"Baixar PDF\" 10000\n" +
                              "click left 1\ntype \"open\nkey Ctrl+Nope\nwait 5 6\n";
                Macro macro = MacroFile.Read(text);
                Equal(6, macro.Steps.Count, "steps");
                Equal(StepKind.Click, macro.Steps[0].Kind, "known step");
                True(macro.Steps.Skip(1).All(s => s.Kind == StepKind.Unknown), "the rest are unknown");
                True(macro.HasUnknown, "HasUnknown");
                Equal("waitfor \"Baixar PDF\" 10000", macro.Steps[1].Raw, "raw kept");
                True(MacroFile.Write(macro).Contains("waitfor \"Baixar PDF\" 10000\nclick left 1\ntype \"open\n"), "written back unchanged");
            });

            Test("macro file: header lines from a newer version are written back unchanged", () =>
            {
                string text = "V-Clicker macro 1\nname=x\ntarget.tab=Relatórios\n[target]\nrepeat=2\n[steps]\nclick left 1 2\n";
                Macro macro = MacroFile.Read(text);
                Equal(2, macro.Repeat, "known keys still read");
                string written = MacroFile.Write(macro.Clone());
                True(written.Contains("target.tab=Relatórios\n") && written.Contains("[target]\n"), "kept: " + written);
            });

            Test("macro file: out-of-range numbers are clamped, bad exe names dropped", () =>
            {
                Macro macro = MacroFile.Read("V-Clicker macro 1\nexe=..\\evil.exe\nrepeat=-4\n[steps]\n" +
                                             "wait 99999999999\nclick left 999999999 -999999999\nkey Down x5000\nscroll -2000 1 1\n");
                Equal("", macro.ExeName, "exe with a folder");
                Equal(0, macro.Repeat, "repeat clamped");
                Equal(MacroStep.MaxWaitMs, macro.Steps[0].DurationMs, "wait clamped");
                Equal(new Point(MacroStep.MaxCoordinate, -MacroStep.MaxCoordinate), macro.Steps[1].At, "coordinates clamped");
                Equal(MacroStep.MaxCount, macro.Steps[2].Count, "count clamped");
                Equal(-MacroStep.MaxCount, macro.Steps[3].Notches, "notches clamped");
            });

            Test("macro file: safe file names", () =>
            {
                Equal("Relatórios de sexta", MacroFile.SafeFileName("  Relatórios de sexta. "), "trimmed");
                Equal("ab", MacroFile.SafeFileName("a/\\:*?\"<>|b"), "invalid characters removed");
                Equal("_CON", MacroFile.SafeFileName("CON"), "reserved name");
                Equal("_com1.txt", MacroFile.SafeFileName("com1.txt"), "reserved device with extension");
                Equal("macro", MacroFile.SafeFileName(" ..."), "never empty");
                True(MacroFile.SafeFileName(new string('x', 300)).Length <= 80, "length capped");
            });

            Test("macro keys: names parse back; modifier-only and numeric names are refused", () =>
            {
                foreach (Keys key in new[] { Keys.Enter, Keys.Tab | Keys.Shift, Keys.C | Keys.Control, Keys.F12 | Keys.Alt, Keys.D7 | Keys.Control, Keys.OemPeriod | Keys.Control, Keys.Escape })
                {
                    True(MacroKeys.TryParse(MacroKeys.Name(key), out Keys parsed), "parse " + MacroKeys.Name(key));
                    Equal(key, parsed, MacroKeys.Name(key));
                }
                var abntSlash = (Keys)0xC1 | Keys.Control;
                Equal("Ctrl+VKC1", MacroKeys.Name(abntSlash), "a key without a name");
                True(MacroKeys.TryParse("Ctrl+VKC1", out Keys slash) && slash == abntSlash, "and back");
                foreach (string bad in new[] { "", "Ctrl", "Ctrl+", "Win+R", "65", "Ctrl+ShiftKey", "Shift+Control", "KeyCode", "VK", "VKZZ", "VK10" })
                    True(!MacroKeys.TryParse(bad, out _), "should refuse '" + bad + "'");
                Equal("Shift+↑", MacroKeys.Display(Keys.Up | Keys.Shift), "arrow display");
            });

            Test("macro: long waits and clone are independent", () =>
            {
                Macro macro = Sample();
                macro.Steps.Add(new MacroStep { Kind = StepKind.Wait, DurationMs = 8000, RecordedMs = 8000 });
                Equal(1, macro.LongWaits().Count(), "only the 8 s wait (the 25 s one was shortened to 1 s)");
                Macro copy = macro.Clone();
                copy.Steps[1].Window.Title = "changed";
                copy.Steps.RemoveAt(0);
                Equal("Salvar \"como\" \\ já", macro.Steps[1].Window.Title, "deep copy");
                Equal(14, macro.Steps.Count, "original list untouched");
            });
        }
    }
}
