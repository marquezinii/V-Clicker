using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using static VClicker.Tests.Program;

namespace VClicker.Tests
{
    /// <summary>SafeClick (docs/superpowers/specs/2026-10-21-safeclick-design.md).</summary>
    internal static partial class MacroTests
    {
        private static T Find<T>(Control root, Func<T, bool> match) where T : Control => All(root).OfType<T>().FirstOrDefault(match);

        private static void SafeClickTests()
        {
            Test("limit: round-trips, written only when set, clamped", () =>
            {
                var macro = new Macro { Name = "x", ExeName = "app.exe" };
                macro.Steps.Add(new MacroStep { Kind = StepKind.Click, At = new Point(1, 2) });
                True(!MacroFile.Write(macro).Contains("limit="), "no limit = no line (old signatures stay the same)");
                macro.LimitMinutes = 90;
                string text = MacroFile.Write(macro);
                True(text.Contains("limit=90\n"), "written: " + text);
                Equal(90, MacroFile.Read(text).LimitMinutes, "read back");
                Equal(Macro.MaxLimitMinutes, MacroFile.Read(text.Replace("limit=90", "limit=999999")).LimitMinutes, "clamped");
                Equal(0, MacroFile.Read(text.Replace("limit=90", "limit=x")).LimitMinutes, "garbage = no limit");
                Equal(0, MacroFile.Read(text).ExtraHeader.Count, "a known key, not an unknown header line");
                Equal("45 min|2 h|1 h 30 min", Macro.LimitText(45) + "|" + Macro.LimitText(120) + "|" + Macro.LimitText(90), "shown as");
            });

            Test("player: the duration limit stops a long wait and a paused run, never mid-drag", () =>
            {
                var sink = new FakeInputSink();
                using (var player = new MacroPlayer(sink) { MinuteMs = 100 })
                {
                    Macro waiting = Steps(1, new MacroStep { Kind = StepKind.Wait, DurationMs = 60000 }, new MacroStep { Kind = StepKind.Click });
                    waiting.LimitMinutes = 3;
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    Equal(MacroStopReason.TimeLimit, Play(player, waiting, Ok), "a long wait");
                    True(watch.ElapsedMilliseconds >= 280 && watch.ElapsedMilliseconds < 1300, "stopped after " + watch.ElapsedMilliseconds + " ms");
                    Equal(0, sink.Snapshot().Length, "the click after the wait never happened");

                    Macro paused = Steps(0, new MacroStep { Kind = StepKind.Click });
                    paused.LimitMinutes = 3;
                    Equal(MacroStopReason.TimeLimit, Play(player, paused, s => new MacroCheckResult { Status = MacroCheck.NotFocused }), "paused time counts");

                    // The drag starts at about 290 ms and holds the button for 200 ms past the limit: it is never cut short.
                    var dragSink = new FakeInputSink();
                    using (var dragger = new MacroPlayer(dragSink) { MinuteMs = 100 })
                    {
                        Macro drag = Steps(1, new MacroStep { Kind = StepKind.Wait, DurationMs = 240 },
                                           new MacroStep { Kind = StepKind.Drag, At = new Point(0, 0), To = new Point(100, 0) },
                                           new MacroStep { Kind = StepKind.Click });
                        drag.LimitMinutes = 3;
                        Equal(MacroStopReason.TimeLimit, Play(dragger, drag, Ok), "a drag across the limit");
                    }
                    string[] log = dragSink.Snapshot();
                    Equal(1, log.Count(e => e == "down Left"), "only the drag pressed the button");
                    Equal("up Left", log.Last(), "released by the drag itself");
                    Equal(10, log.Count(e => e.StartsWith("move ")) - 1, "every move of the drag happened");
                }
            });

            Test("safeclick: impactful actions are the exact five combinations", () =>
            {
                Macro macro = Steps(1,
                    new MacroStep { Kind = StepKind.Key, Key = Keys.Delete, Count = 3 },
                    new MacroStep { Kind = StepKind.Shortcut, Key = Keys.Delete | Keys.Shift },
                    new MacroStep { Kind = StepKind.Shortcut, Key = Keys.F4 | Keys.Alt },
                    new MacroStep { Kind = StepKind.Shortcut, Key = Keys.W | Keys.Control },
                    new MacroStep { Kind = StepKind.Shortcut, Key = Keys.Escape | Keys.Control | Keys.Shift },
                    new MacroStep { Kind = StepKind.Shortcut, Key = Keys.Delete | Keys.Control },
                    new MacroStep { Kind = StepKind.Shortcut, Key = Keys.F5 | Keys.Alt },
                    new MacroStep { Kind = StepKind.Type, Text = "Delete" },
                    new MacroStep { Kind = StepKind.Click });
                var found = SafeClick.Impactful(macro);
                Equal("0,1,2,3,4", string.Join(",", found.Select(a => a.Step)), "flagged steps");
                Equal("True,True,False,False,False", string.Join(",", found.Select(a => a.Deletes)), "the two that can delete");
                True(found.All(a => a.Explain().Length > 0), "every one is explained");
            });

            Test("safeclick: the signature ignores the name and changes with anything else", () =>
            {
                Macro Make()
                {
                    Macro m = Steps(2, new MacroStep { Kind = StepKind.Shortcut, Key = Keys.F4 | Keys.Alt });
                    m.Name = "Relatórios";
                    m.ExeName = "app.exe";
                    return m;
                }
                string original = SafeClick.Signature(Make());
                Equal(64, original.Length, "SHA-256 in hex");
                Macro renamed = Make();
                renamed.Name = "Outro nome";
                Equal(original, SafeClick.Signature(renamed), "renaming keeps it");
                Equal(original, SafeClick.Signature(MacroFile.Read(MacroFile.Write(Make()))), "through the file");
                var changes = new Action<Macro>[]
                {
                    m => m.Steps[0].Key = Keys.F4, m => m.Repeat = 3, m => m.LimitMinutes = 10, m => m.ExeName = "other.exe",
                    m => m.Steps.Add(new MacroStep { Kind = StepKind.Click })
                };
                foreach (Action<Macro> change in changes)
                {
                    Macro changed = Make();
                    change(changed);
                    True(SafeClick.Signature(changed) != original, "a relevant change invalidates it");
                }
            });

            Test("safeclick: approval is per local file, a copy with identical content is not approved", () =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "vc-approval-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                try
                {
                    Macro macro = Steps(1, new MacroStep { Kind = StepKind.Key, Key = Keys.Delete });
                    macro.ExeName = "app.exe";
                    string a = Path.Combine(dir, "a.vcmacro"), b = Path.Combine(dir, "b.vcmacro"), c = Path.Combine(dir, "c.vcmacro");
                    Equal(null, SafeClick.FileInstance(a), "no file, no instance");
                    MacroFile.Save(macro, a);
                    string key = SafeClick.ApprovalKey(SafeClick.Signature(macro), SafeClick.FileInstance(a));
                    System.Threading.Thread.Sleep(20);
                    File.Copy(a, b);
                    True(SafeClick.ApprovalKey(SafeClick.Signature(MacroFile.Load(b)), SafeClick.FileInstance(b)) != key, "a copy is another instance");
                    macro.Name = "renamed";
                    MacroFile.Save(macro, a);
                    File.Move(a, c);
                    Equal(key, SafeClick.ApprovalKey(SafeClick.Signature(MacroFile.Load(c)), SafeClick.FileInstance(c)), "saved and moved by the Studio: the same");
                }
                finally { Directory.Delete(dir, true); }
            });

            Test("smart target: the image search on a supplied capture finds it at the right screen place", () =>
            {
                var centre = new Point(360 + Btn.W / 2, 100 + Btn.H / 2);
                using (Bitmap before = Buttons(96f, new Btn("CSV", 100, 100), new Btn("PDF", 360, 100)))
                using (Bitmap after = Buttons(96f, new Btn("CSV", 100, 100), new Btn("PDF", 360, 180)))
                {
                    ImageOption chosen = ImageTarget.Options(before, centre).First(o => o.Problem == ImageProblem.None);
                    var target = new SmartTarget { Kind = TargetKind.Image, Png = SmartTarget.EncodeImage(chosen.Crop) };
                    var client = new Rectangle(1000, 500, after.Width, after.Height);
                    TargetLook look = SmartTargets.Look(IntPtr.Zero, 0, target, after, client, System.Threading.CancellationToken.None);
                    Equal(TargetStatus.Found, look.Status, "found in the capture");
                    Equal(ConditionRead.Met, look.Read, "inside the client area");
                    True(Math.Abs(look.Screen.X - (1000 + centre.X)) <= 1 && Math.Abs(look.Screen.Y - (500 + centre.Y + 80)) <= 1, "on the screen at " + look.Screen);
                    foreach (ImageOption o in ImageTarget.Options(before, centre)) o.Crop?.Dispose();
                }
            });

            Test("conditions: colour from a supplied capture", () =>
            {
                using (var shot = new Bitmap(50, 40))
                {
                    using (Graphics g = Graphics.FromImage(shot)) g.Clear(Color.FromArgb(0x33, 0x66, 0x99));
                    Equal((int?)0x336699, MacroConditions.AverageColor(shot, new Point(20, 20)), "the 5x5 average");
                    Equal((int?)null, MacroConditions.AverageColor(shot, new Point(1, 1)), "the area leaves the capture");
                    Equal((int?)null, MacroConditions.AverageColor(shot, new Point(48, 20)), "on the right edge too");
                }
            });

            Test("preview: a blank capture is a failure; states and the \"may depend\" rule", () =>
            {
                using (var blank = new Bitmap(64, 48))
                using (var drawn = new Bitmap(64, 48))
                {
                    using (Graphics g = Graphics.FromImage(blank)) g.Clear(Color.Black);
                    using (Graphics g = Graphics.FromImage(drawn)) { g.Clear(Color.Black); g.FillRectangle(Brushes.White, 30, 20, 4, 4); }
                    True(WindowShot.IsBlank(blank), "one colour = not trustworthy");
                    True(!WindowShot.IsBlank(drawn), "anything drawn = a picture");
                }
                using (var form = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000), Size = new Size(300, 200) })
                {
                    form.Show();
                    uint process = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                    var missing = new WindowRef { Class = "#32770", Title = "Não existe" };
                    Macro macro = Steps(1,
                        new MacroStep { Kind = StepKind.Click, At = new Point(10, 10), Window = missing },
                        new MacroStep { Kind = StepKind.Click, At = new Point(10, 10) },
                        new MacroStep { Kind = StepKind.Type, Text = "Oi" },
                        new MacroStep { Kind = StepKind.Click, At = new Point(10, 10), Window = missing },
                        new MacroStep { Kind = StepKind.Shortcut, Key = Keys.F4 | Keys.Alt });
                    int shots = 0;
                    Func<IntPtr, WindowShot> shoot = window =>
                    {
                        shots++;
                        return new WindowShot(new Bitmap(300, 200), WindowLock.ClientArea(window), ShotProblem.Blank);
                    };
                    using (MacroPreviewResult result = MacroPreview.Evaluate(macro, form.Handle, process, shoot, System.Threading.CancellationToken.None))
                    {
                        Equal("NotFound,Inconclusive,NoPosition,Unavailable,NoPosition", string.Join(",", result.Steps.Select(s => s.State)), "states");
                        True(result.WindowFound, "the bound window was found");
                        Equal(1, shots, "one capture per window");
                        Equal(1, result.Impactful.Count, "Alt+F4 listed");
                    }
                    using (MacroPreviewResult none = MacroPreview.Evaluate(macro, IntPtr.Zero, 0, shoot, System.Threading.CancellationToken.None))
                        Equal("NoApp,NoApp,NoPosition,NoApp,NoPosition", string.Join(",", none.Steps.Select(s => s.State)), "no app open");
                }
            });

            Test("simulation: marker layout scales and merges", () =>
            {
                RectangleF fit = MarkerLayout.Fit(new Size(2000, 1000), new Rectangle(10, 20, 500, 400));
                Equal(new RectangleF(10, 95, 500, 250), fit, "uniform scale, centred");
                Equal(new PointF(260, 220), MarkerLayout.ToView(new Point(1000, 500), new Size(2000, 1000), new Rectangle(10, 20, 500, 400)), "a point");
                Equal(new RectangleF(210, 195, 100, 50), MarkerLayout.Fit(new Size(100, 50), new Rectangle(10, 20, 500, 400)), "never enlarged, centred");
                var groups = MarkerLayout.Merge(new[]
                {
                    new System.Collections.Generic.KeyValuePair<int, PointF>(1, new PointF(100, 100)),
                    new System.Collections.Generic.KeyValuePair<int, PointF>(4, new PointF(103, 102)),
                    new System.Collections.Generic.KeyValuePair<int, PointF>(6, new PointF(112, 100))
                }, 6);
                Equal("2, 5|7", string.Join("|", groups.Select(g => g.Label)), "close points share one label (numbers are 1-based)");
            });

            Test("simulation: the dialog builds, navigates and fits a small screen", () =>
            {
                Macro macro = Steps(1, new MacroStep { Kind = StepKind.Click, At = new Point(10, 10) },
                                    new MacroStep { Kind = StepKind.Type, Text = "Oi" },
                                    new MacroStep { Kind = StepKind.Shortcut, Key = Keys.F4 | Keys.Alt });
                Func<System.Threading.CancellationToken, MacroPreviewResult> evaluate = cancel =>
                {
                    var result = new MacroPreviewResult { WindowFound = true, Impactful = SafeClick.Impactful(macro) };
                    var shot = new Bitmap(400, 300);
                    using (Graphics g = Graphics.FromImage(shot)) { g.Clear(Color.White); g.FillRectangle(Brushes.Navy, 0, 0, 40, 40); }
                    result.Shots[new IntPtr(1)] = new WindowShot(shot, new Rectangle(0, 0, 400, 300), ShotProblem.None);
                    result.Steps.Add(new StepPreview { Index = 0, State = PreviewState.InCapture, Window = new IntPtr(1), At = new Point(10, 10) });
                    result.Steps.Add(new StepPreview { Index = 1, State = PreviewState.NoPosition });
                    result.Steps.Add(new StepPreview { Index = 2, State = PreviewState.NoPosition });
                    return result;
                };
                using (var dialog = new SimulationDialog(macro, evaluate, 0))
                {
                    dialog.Show();
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    while (dialog.Loading && watch.ElapsedMilliseconds < 5000) { Application.DoEvents(); System.Threading.Thread.Sleep(5); }
                    True(!dialog.Loading, "evaluated");
                    Equal(0, dialog.Step, "starts at the chosen step");
                    dialog.Next(); dialog.Next(); dialog.Next();
                    Equal(2, dialog.Step, "never past the end");
                    dialog.Previous(); dialog.Previous(); dialog.Previous();
                    Equal(0, dialog.Step, "never before the start");
                    True(dialog.Height <= Theme.Dp(720), "fits a laptop screen: " + dialog.Height);
                    dialog.Recapture();
                    watch.Restart();
                    while (dialog.Loading && watch.ElapsedMilliseconds < 5000) { Application.DoEvents(); System.Threading.Thread.Sleep(5); }
                    Equal(0, dialog.Step, "a new capture keeps the step");
                    dialog.Close();
                }
            });

            Test("studio: the duration limit field writes LimitMinutes and every bar fits the narrowest window", () =>
            {
                string ini = StudioIni();
                using (var studio = new MacroStudio(null, new ClickSettings(), ini))
                {
                    Macro sample = Sample();
                    sample.LimitMinutes = 30;
                    studio.Open(sample, null);
                    studio.StartPosition = FormStartPosition.Manual;
                    studio.Location = new Point(-20000, -20000);
                    studio.Show();
                    studio.Size = studio.MinimumSize;
                    Application.DoEvents();
                    LedCheck limit = Find<LedCheck>(studio, c => c.Text == Loc.T("studio.limit"));
                    NumberField minutes = Find<NumberField>(studio, f => f.FieldName == "Duration limit");
                    True(limit.Checked && minutes.Value == 30 && minutes.Enabled, "shows the macro's limit");
                    minutes.Controls[0].Text = "45"; // typed, then committed as when the field loses focus
                    minutes.Commit();
                    Equal(45, studio.Current.LimitMinutes, "edited");
                    limit.Checked = false;
                    Equal(0, studio.Current.LimitMinutes, "no limit");
                    True(!minutes.Enabled, "the field is off without the tick");
                    True(Find<KeyButton>(studio, b => b.Text == Loc.T("studio.simulate")) != null, "Simulate is there");
                    Rectangle client = studio.RectangleToScreen(studio.ClientRectangle);
                    var runBar = new[] { "studio.record", "studio.run", "studio.simulate", "studio.stop" }.Select(Loc.T).ToList();
                    foreach (Control c in All(studio).Where(c => (c is KeyButton && runBar.Contains(c.Text)) || c is LedCheck || c is NumberField))
                        if (c.Visible && c.Width > 0)
                            True(c.RectangleToScreen(c.ClientRectangle).Right <= client.Right, c.Text + " fits at " + studio.Width + " px");
                }
            });

            Test("review fix: closing the simulation cancels the evaluation in progress", () =>
            {
                Macro macro = Steps(1, new MacroStep { Kind = StepKind.Click, At = new Point(10, 10) });
                var seen = new System.Threading.ManualResetEventSlim();
                System.Threading.CancellationToken token = default;
                Func<System.Threading.CancellationToken, MacroPreviewResult> evaluate = cancel =>
                {
                    token = cancel;
                    seen.Set();
                    cancel.WaitHandle.WaitOne(5000);
                    return new MacroPreviewResult();
                };
                using (var dialog = new SimulationDialog(macro, evaluate, 0))
                {
                    dialog.Show();
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    while (!seen.IsSet && watch.ElapsedMilliseconds < 5000) { Application.DoEvents(); System.Threading.Thread.Sleep(5); }
                    True(seen.IsSet, "evaluating");
                    dialog.Close();
                    True(token.IsCancellationRequested, "closing cancels the look in progress");
                }
            });

            Test("review fix: the duration limit is checked right after a slow look, before any click", () =>
            {
                var sink = new FakeInputSink();
                using (var player = new MacroPlayer(sink) { MinuteMs = 100 })
                {
                    Macro macro = Steps(1, new MacroStep { Kind = StepKind.Click, Target = new SmartTarget { Kind = TargetKind.Element, ControlType = "Button", Name = "x" } });
                    macro.LimitMinutes = 1;
                    MacroStopReason? reason = null;
                    player.Stopped += r => reason = r;
                    player.Start(macro, Ok, 0, null,
                        (step, cancel) => { System.Threading.Thread.Sleep(80); return new TargetLook { Read = ConditionRead.Met, Status = TargetStatus.Found, Screen = new Point(5, 5) }; },
                        (step, look, cancel) => { System.Threading.Thread.Sleep(80); return ConditionRead.Met; });
                    True(player.WaitForStop(5000), "stopped");
                    Equal(MacroStopReason.TimeLimit, reason, "the limit, not a late click");
                    Equal(0, sink.Snapshot().Count(e => e.StartsWith("down")), "nothing clicked after the limit");
                }
            });

            Test("review fix: every unsaved macro is its own copy for the safety approval", () =>
            {
                string ini = StudioIni();
                var settings = new ClickSettings();
                using (var studio = new MacroStudio(null, settings, ini))
                {
                    int asked = 0;
                    studio.ReviewSafety = actions => { asked++; return ReviewChoice.Run; };
                    studio.ListWindows = exe => new System.Collections.Generic.List<AppWindow>(); // stops right after the review
                    Macro Make()
                    {
                        Macro m = Steps(1, new MacroStep { Kind = StepKind.Key, Key = Keys.Delete });
                        m.ExeName = "app.exe";
                        return m;
                    }
                    studio.Open(Make(), null);
                    studio.BeginRun();
                    studio.BeginRun();
                    Equal(1, asked, "approved once for this unsaved macro");
                    studio.Open(Make(), null); // another unsaved macro, identical content
                    studio.BeginRun();
                    Equal(2, asked, "a new unsaved macro is asked again");
                }
            });

            Test("review fix: Left and Right move between steps even with a button focused", () =>
            {
                Macro macro = Steps(1, new MacroStep { Kind = StepKind.Type, Text = "a" }, new MacroStep { Kind = StepKind.Type, Text = "b" });
                using (var dialog = new SimulationDialog(macro, cancel => new MacroPreviewResult(), 0))
                {
                    dialog.Show();
                    Application.DoEvents();
                    KeyButton next = Find<KeyButton>(dialog, b => b.Text == Loc.T("sim.next"));
                    Message key = Message.Create(next.Handle, 0x100, (IntPtr)Keys.Right, IntPtr.Zero); // WM_KEYDOWN
                    next.PreProcessMessage(ref key);
                    Equal(1, dialog.Step, "Right: next step");
                    key = Message.Create(next.Handle, 0x100, (IntPtr)Keys.Left, IntPtr.Zero);
                    next.PreProcessMessage(ref key);
                    Equal(0, dialog.Step, "Left: previous step");
                    dialog.Close();
                }
            });

            Test("review fix: a fixed point is marked where it is in the capture, even if the window moved since", () =>
            {
                using (var form = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000), Size = new Size(300, 200) })
                {
                    form.Show();
                    uint process = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                    Macro macro = Steps(1, new MacroStep { Kind = StepKind.Click, At = new Point(10, 10) });
                    Func<IntPtr, WindowShot> shoot = window =>
                    {
                        var image = new Bitmap(300, 200);
                        using (Graphics g = Graphics.FromImage(image)) { g.Clear(Color.White); g.FillRectangle(Brushes.Black, 0, 0, 20, 20); }
                        Rectangle then = WindowLock.ClientArea(window);
                        then.Offset(50, 50); // where the window was when captured
                        return new WindowShot(image, then, ShotProblem.None);
                    };
                    using (MacroPreviewResult result = MacroPreview.Evaluate(macro, form.Handle, process, shoot, System.Threading.CancellationToken.None))
                    {
                        Equal(PreviewState.InCapture, result.Steps[0].State, "in the capture");
                        Equal(new Point(10, 10), result.Steps[0].At.Value, "the client point is the point in the capture");
                    }
                }
            });

            Test("settings: approvals round-trip, are capped and survive garbage", () =>
            {
                string Key(int n) => new string('a', 63) + (n % 10) + "@" + n;
                var settings = new ClickSettings();
                for (int n = 0; n < ClickSettings.MaxApprovals + 5; n++) settings.Approve(Key(n));
                Equal(ClickSettings.MaxApprovals, settings.ApprovedMacros.Count, "capped");
                True(!settings.IsApproved(Key(4)) && settings.IsApproved(Key(5)) && settings.IsApproved(Key(204)), "the oldest go first");
                settings.Approve(Key(5));
                Equal(Key(5), settings.ApprovedMacros.Last(), "approving again makes it the newest");

                var loaded = new ClickSettings();
                loaded.Apply(ClickSettings.ReadPairs(settings.Serialize()));
                loaded.Validate();
                Equal(string.Join(",", settings.ApprovedMacros), string.Join(",", loaded.ApprovedMacros), "round trip");

                var garbage = new ClickSettings();
                garbage.Apply(ClickSettings.ReadPairs(new[] { "ApprovedMacros=zz,," + Key(1) + ",../x@1, " + Key(2).ToUpperInvariant() }));
                garbage.Validate();
                Equal(Key(1), string.Join(",", garbage.ApprovedMacros), "only well-formed entries");
            });
        }
    }
}
