using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Linq;
using System.Threading;
using static VClicker.Tests.Program;

namespace VClicker.Tests
{
    /// <summary>Smart Target: the file, the image matcher and the player (see docs/superpowers/specs/2026-10-23-smart-target-design.md).</summary>
    internal static partial class MacroTests
    {
        private static void SmartTargetTests()
        {
            Test("smart target: element and image targets round-trip through the file", () =>
            {
                byte[] png;
                using (Bitmap screen = Buttons(96, new Btn("PDF", 0, 0)))
                using (Bitmap image = Crop(screen, new Point(55, 16), 32)) png = SmartTarget.EncodeImage(image);
                var macro = new Macro();
                macro.Steps.Add(new MacroStep
                {
                    Kind = StepKind.Click, At = new Point(640, 520), Window = new WindowRef { Class = "#32770", Title = "Salvar como" },
                    Target = new SmartTarget { Kind = TargetKind.Element, ControlType = "Button", Name = "Baixar \"PDF\"", AutomationId = "btn pdf", TimeoutMs = 10000 }
                });
                macro.Steps.Add(new MacroStep
                {
                    Kind = StepKind.DoubleClick, Button = ClickButton.Right, At = new Point(300, 210),
                    Target = new SmartTarget { Kind = TargetKind.Image, Png = png, TimeoutMs = 3600000 }
                });
                macro.Steps.Add(new MacroStep
                {
                    Kind = StepKind.WaitFor, Condition = ConditionKind.Element, At = new Point(640, 520), ControlType = "Button",
                    Text = "Baixar PDF", AutomationId = "btnPdf", DurationMs = 30000
                });
                macro.Steps.Add(new MacroStep { Kind = StepKind.Click, At = new Point(1, 2) });
                string text = MacroFile.Write(macro);
                Macro loaded = MacroFile.Read(text);
                Equal(text, MacroFile.Write(loaded), "identical");
                True(!loaded.HasUnknown, "all known");
                Equal("btn pdf", loaded.Steps[0].Target.AutomationId, "id");
                Equal("Salvar como", loaded.Steps[0].Window.Title, "searched in the dialog the click was recorded in");
                Equal(new Point(640, 520), loaded.Steps[0].At, "the recorded point is kept");
                Equal(TargetKind.Image, loaded.Steps[1].Target.Kind, "image");
                using (Bitmap back = loaded.Steps[1].Target.Decode()) Equal(32, back.Width, "the image itself");
                Equal("btnPdf", loaded.Steps[2].AutomationId, "condition id");
                True(loaded.Steps[3].Target == null, "a plain click stays a fixed point");
                True(loaded.Steps[0].Describe().Contains("Baixar \"PDF\""), loaded.Steps[0].Describe());

                MacroStep copy = loaded.Steps[0].Clone();
                copy.Target.Name = "other";
                Equal("Baixar \"PDF\"", loaded.Steps[0].Target.Name, "a copy has its own target");

                string notSquare;
                using (Bitmap screen = Buttons(96, new Btn("PDF", 0, 0)))
                using (Bitmap wide = screen.Clone(new Rectangle(0, 0, 32, 16), PixelFormat.Format32bppArgb)) notSquare = Convert.ToBase64String(SmartTarget.EncodeImage(wide));
                foreach (string bad in new[]
                {
                    "click left 1 2 find element \"Button\" \"x\"",
                    "click left 1 2 find element \"Button\" \"\" timeout=1000",
                    "click left 1 2 find element \"Button\" \"x\" id \"\" timeout=1000",
                    "click left 1 2 find element \"Button\" \"x\" timeout=1000 speed=2",
                    "click left 1 2 find image AAAA timeout=1000",
                    "click left 1 2 find image " + notSquare + " timeout=1000",
                    "click left 1 2 find smell timeout=1000",
                    "hover 1 2 find element \"Button\" \"x\" timeout=1000",
                    "drag left 1 2 3 4 find element \"Button\" \"x\" timeout=1000",
                    "waitfor element 1 2 \"Button\" \"x\" id timeout=1000"
                })
                    True(MacroFile.Read("V-Clicker macro 1\n[steps]\n" + bad + "\n").HasUnknown, "should stay unknown: " + bad);
            });

            Test("smart target: the image is found where it moved, also highlighted; never a look-alike or one of two", () =>
            {
                // The same check at three screen scales: text size changes what a region of a given size contains.
                foreach (float dpi in new[] { 96f, 120f, 144f })
                {
                    string at = " (" + dpi + " dpi)";
                    var centre = new Point(360 + Btn.W / 2, 100 + Btn.H / 2);
                    using (Bitmap before = Buttons(dpi, new Btn("CSV", 100, 100), new Btn("XLS", 230, 100), new Btn("PDF", 360, 100)))
                    {
                        List<ImageOption> options = ImageTarget.Options(before, centre);
                        ImageOption chosen = options.FirstOrDefault(o => o.Problem == ImageProblem.None);
                        True(chosen != null, "a usable region" + at + ": " + string.Join(", ", options.Select(o => o.Side + "=" + o.Problem)));
                        Bitmap image = chosen.Crop;
                        int half = chosen.Side / 2;
                        using (Bitmap moved = Buttons(dpi, new Btn("CSV", 100, 194), new Btn("XLS", 230, 194), new Btn("PDF", 360, 194) { Hover = true }))
                        {
                            ImageMatch match = ImageTarget.Search(moved, image);
                            True(match.Found.HasValue, "found after moving 94 px down, in its hover colour" + at + " (best " + match.Best.ToString("0.00") + ")");
                            var found = new Point(match.Found.Value.X + half, match.Found.Value.Y + half);
                            True(Math.Abs(found.X - centre.X) <= 1 && Math.Abs(found.Y - (centre.Y + 94)) <= 1, "at the button" + at + ": " + found);
                            True(ImageTarget.IsAt(moved, image, match.Found.Value.Location), "and the last look agrees" + at);
                            True(!ImageTarget.IsAt(moved, image, new Point(100 + Btn.W / 2 - half, 194 + Btn.H / 2 - half)), "not on the CSV button" + at);
                        }
                        using (Bitmap gone = Buttons(dpi, new Btn("CSV", 100, 100), new Btn("XLS", 230, 100), new Btn("DOC", 360, 100)))
                            True(!ImageTarget.Search(gone, image).Found.HasValue, "PDF became DOC: nothing is found" + at);
                        using (Bitmap two = Buttons(dpi, new Btn("PDF", 360, 100), new Btn("PDF", 900, 600)))
                        {
                            ImageMatch match = ImageTarget.Search(two, image);
                            True(!match.Found.HasValue && match.Count >= 2, "two PDF buttons: ambiguous" + at + " (" + match.Count + ")");
                        }
                        Equal(ImageProblem.Plain, ImageTarget.Options(before, new Point(1000, 800))[0].Problem, "a plain region cannot be recognized" + at);
                        Equal(ImageProblem.OutOfWindow, ImageTarget.Options(before, new Point(5, 5))[0].Problem, "a region must fit in the window" + at);

                        var timer = Stopwatch.StartNew();
                        foreach (ImageOption option in options.Where(o => o.Crop != null)) ImageTarget.Search(before, option.Crop);
                        True(timer.ElapsedMilliseconds < 3 * 300, "1920x1080 searches" + at + ": " + timer.ElapsedMilliseconds + " ms for 3");
                        foreach (ImageOption option in options) option.Crop?.Dispose();
                    }
                }
            });

            Test("smart target: the click goes where the target is found twice, never to the recorded point", () =>
            {
                var found = new TargetLook { Read = ConditionRead.Met, Status = TargetStatus.Found, Count = 1, Screen = new Point(700, 610) };
                Equal(MacroStopReason.Completed, PlayTarget(Steps(1, Targeted(StepKind.Click)), (s, c) => found, l => ConditionRead.Met, out string[] log), "clicked");
                Equal("move 700,610|down Left|up Left", string.Join("|", log), "one click at the target");

                Equal(MacroStopReason.Completed, PlayTarget(Steps(1, Targeted(StepKind.DoubleClick)), (s, c) => found, l => ConditionRead.Met, out log), "double");
                Equal("move 700,610|down Left|up Left|down Left|up Left", string.Join("|", log), "a double click at the target");

                int reads = 0;
                Equal(MacroStopReason.Completed, PlayTarget(Steps(1, Targeted(StepKind.Click)),
                    (s, c) => new TargetLook { Read = ConditionRead.Met, Status = TargetStatus.Found, Count = 1, Screen = new Point(700, ++reads < 4 ? 500 + reads * 30 : 640) },
                    l => ConditionRead.Met, out log), "clicked once it stopped moving");
                Equal("move 700,640", log[0], "where it stopped");
                True(reads >= 5, "only after two finds at the same place: " + reads + " reads");
            });

            Test("smart target: ambiguous, never found or never confirmed: no input at all, and the reason is kept", () =>
            {
                var two = new TargetLook { Status = TargetStatus.Ambiguous, Count = 2 };
                MacroPlayer seen = null;
                Equal(MacroStopReason.TargetTimeout, PlayTarget(Steps(1, Targeted(StepKind.Click), new MacroStep { Kind = StepKind.Click }),
                    (s, c) => two, l => ConditionRead.Met, out string[] log, p => seen = p), "ambiguous");
                Equal(0, log.Length, "nothing sent, not even the next step");
                Equal(TargetStatus.Ambiguous, seen.LastTarget.Status, "the reason");
                Equal(2, seen.LastTarget.Count, "how many");

                var found = new TargetLook { Read = ConditionRead.Met, Status = TargetStatus.Found, Count = 1, Screen = new Point(700, 610) };
                Equal(MacroStopReason.TargetTimeout, PlayTarget(Steps(1, Targeted(StepKind.Click)), (s, c) => found, l => ConditionRead.NotYet, out log,
                    p => seen = p), "covered by the page itself at the last look");
                Equal(0, log.Length, "not clicked");
                Equal(TargetStatus.Unconfirmed, seen.LastTarget.Status, "found but not confirmed");

                Equal(MacroStopReason.TargetTimeout, PlayTarget(Steps(1, Targeted(StepKind.Click)), (s, c) => null, l => ConditionRead.Met, out log), "no answer");
                Equal(0, log.Length, "not clicked");
            });

            Test("smart target: time away does not count; the window closing and F8 end the search", () =>
            {
                var watch = Stopwatch.StartNew();
                MacroPhase phase = MacroPhase.Idle;
                Equal(MacroStopReason.TargetTimeout, PlayTarget(Steps(1, Targeted(StepKind.Click)),
                    (s, c) => new TargetLook { Read = watch.ElapsedMilliseconds < 1200 ? ConditionRead.Away : ConditionRead.NotYet },
                    l => ConditionRead.Met, out _, p => { Thread.Sleep(400); phase = p.Phase; }), "times out after all");
                Equal(MacroPhase.Paused, phase, "paused while another app was in front");
                True(watch.ElapsedMilliseconds >= 2100, "the 1 s limit only ran after the pause: " + watch.ElapsedMilliseconds + " ms");

                Equal(MacroStopReason.TargetClosed, PlayTarget(Steps(1, Targeted(StepKind.Click)),
                    (s, c) => new TargetLook { Read = ConditionRead.Closed }, l => ConditionRead.Met, out _), "closed");

                // A search that hangs (an app that does not answer): F8 does not wait for it, and it does not pile up.
                var hang = new ManualResetEventSlim();
                long stopMs = -1;
                Equal(MacroStopReason.Manual, PlayTarget(Steps(1, Targeted(StepKind.Click)),
                    (s, c) => MacroConditions.WithTimeout(() => { hang.Wait(5000); return new TargetLook(); }, null, 2000, c),
                    l => ConditionRead.Met, out string[] log, p =>
                    {
                        Thread.Sleep(150);
                        var t = Stopwatch.StartNew();
                        p.Stop();
                        True(p.WaitForStop(3000), "stopped");
                        stopMs = t.ElapsedMilliseconds;
                    }), "stopped");
                Equal(0, log.Length, "not clicked");
                Equal("busy", MacroConditions.WithTimeout(() => "fresh", "busy", 1000, CancellationToken.None), "no second read while the first still hangs");
                hang.Set();
                var settle = Stopwatch.StartNew();
                while (MacroConditions.WithTimeout(() => "fresh", "busy", 1000, CancellationToken.None) != "fresh" && settle.ElapsedMilliseconds < 3000) Thread.Sleep(20);
                Equal("fresh", MacroConditions.WithTimeout(() => "fresh", "busy", 1000, CancellationToken.None), "reads work again once it ended");
                True(stopMs < 100, "F8 did not wait for the hanging search: " + stopMs + " ms");

                using (var token = new CancellationTokenSource())
                {
                    var t = Stopwatch.StartNew();
                    ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(100); token.Cancel(); });
                    Equal("stopped", MacroConditions.WithTimeout(() => { Thread.Sleep(1500); return "late"; }, "stopped", 2000, token.Token), "F8 during a read");
                    True(t.ElapsedMilliseconds < 400, "returned at once: " + t.ElapsedMilliseconds + " ms");
                    while (MacroConditions.WithTimeout(() => "fresh", "busy", 1000, CancellationToken.None) != "fresh" && t.ElapsedMilliseconds < 4000) Thread.Sleep(20);
                }
            });
            Test("smart target in the studio: a click becomes a target and back exactly, even after saving", () =>
            {
                string ini = StudioIni();
                using (var studio = new MacroStudio(null, new ClickSettings(), ini))
                {
                    Macro macro = Steps(1,
                        new MacroStep { Kind = StepKind.Click, At = new Point(640, 520), Window = new WindowRef { Class = "#32770", Title = "Salvar como" } },
                        new MacroStep { Kind = StepKind.Wait, DurationMs = 1000, RecordedMs = 1000 });
                    macro.ExeName = "chrome.exe";
                    studio.Open(macro, null);
                    string original = MacroFile.Write(studio.Current);

                    studio.ConvertToTarget(0, new SmartTarget { Kind = TargetKind.Element, ControlType = "Button", Name = "Baixar PDF" });
                    True(studio.Current.Steps[0].Describe().Contains("Baixar PDF"), studio.Current.Steps[0].Describe());
                    studio.ConvertToTarget(1, new SmartTarget { Kind = TargetKind.Element, ControlType = "Button", Name = "x" });
                    Equal(StepKind.Wait, studio.Current.Steps[1].Kind, "only clicks get a target");

                    studio.Open(MacroFile.Read(MacroFile.Write(studio.Current)), null); // saved and reopened
                    True(studio.Current.Steps[0].Target != null, "the target survives the file");
                    studio.RevertTarget(0);
                    Equal(original, MacroFile.Write(studio.Current), "back to exactly the recorded click");
                    studio.Undo();
                    True(studio.Current.Steps[0].Target != null, "undo brings the target back");
                }

                using (Bitmap screen = Buttons(96, new Btn("CSV", 100, 100), new Btn("PDF", 230, 100)))
                {
                    var full = new TargetProbe
                    {
                        At = new Point(230 + Btn.W / 2, 100 + Btn.H / 2), ElementPreview = (Bitmap)screen.Clone(),
                        Element = new ElementProbe { ControlType = "Button", Name = "Baixar PDF", Count = 1 }
                    };
                    full.Images = ImageTarget.Options(screen, full.At);
                    using (full)
                    using (var dialog = new MacroStudio.TargetDialog(full, 10000)) { dialog.CreateControl(); }
                    var many = new TargetProbe { Element = new ElementProbe { ControlType = "Button", Name = "Baixar PDF", Count = 2 } };
                    using (many)
                    using (var dialog = new MacroStudio.TargetDialog(many, 10000)) { dialog.CreateControl(); }
                    using (var dialog = new MacroStudio.TargetDialog(new TargetProbe(), 10000)) { dialog.CreateControl(); }
                }

                MacroStep step = Targeted(StepKind.Click);
                True(MacroStudio.TargetStopText(step, new TargetLook { Status = TargetStatus.Ambiguous, Count = 2 }).Contains("2"), "how many");
                True(MacroStudio.TargetStopText(step, null).Contains("Baixar PDF"), "never found: names it");
                foreach (TargetStatus status in Enum.GetValues(typeof(TargetStatus)))
                {
                    string text = MacroStudio.TargetStopText(step, new TargetLook { Status = status, Count = 2 });
                    True(!text.StartsWith("mstop."), "a sentence for " + status + ": " + text);
                    step.Target.Kind = TargetKind.Image;
                    text = MacroStudio.TargetStopText(step, new TargetLook { Status = status, Count = 2 });
                    True(!text.StartsWith("mstop."), "a sentence for an image " + status + ": " + text);
                    step.Target.Kind = TargetKind.Element;
                }
            });
        }

        private static MacroStep Targeted(StepKind kind) => new MacroStep
        {
            Kind = kind, At = new Point(5, 5),
            Target = new SmartTarget { Kind = TargetKind.Element, ControlType = "Button", Name = "Baixar PDF", TimeoutMs = 1000 }
        };

        private static MacroStopReason PlayTarget(Macro macro, Func<MacroStep, CancellationToken, TargetLook> locate, Func<TargetLook, ConditionRead> confirm,
                                                  out string[] log, Action<MacroPlayer> during = null)
        {
            var sink = new FakeInputSink();
            MacroStopReason? reason = null;
            using (var player = new MacroPlayer(sink))
            {
                player.Stopped += r => reason = r;
                True(player.Start(macro, Ok, 0, null, (step, cancel) => locate(step, cancel), (step, look, cancel) => confirm(look)), "started");
                during?.Invoke(player);
                True(player.WaitForStop(10000), "finished in time");
            }
            log = sink.Snapshot();
            return reason.Value;
        }

        /// <summary>A web-style button: white text on a blue fill (darker when hovered).</summary>
        private sealed class Btn
        {
            public const int W = 110, H = 32;
            public readonly string Text;
            public readonly int X, Y;
            public bool Hover;

            public Btn(string text, int x, int y)
            {
                Text = "Baixar " + text;
                X = x;
                Y = y;
            }
        }

        private static Bitmap Buttons(float dpi, params Btn[] buttons)
        {
            var screen = new Bitmap(1920, 1080, PixelFormat.Format32bppArgb);
            screen.SetResolution(dpi, dpi); // text size follows it, as on a screen at that scale
            using (Graphics g = Graphics.FromImage(screen))
            using (var font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.Clear(Color.FromArgb(244, 246, 248));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                foreach (Btn b in buttons)
                {
                    using (var fill = new SolidBrush(b.Hover ? Color.FromArgb(31, 87, 179) : Color.FromArgb(47, 111, 214)))
                        g.FillRectangle(fill, b.X, b.Y, Btn.W, Btn.H);
                    g.DrawString(b.Text, font, Brushes.White, new RectangleF(b.X, b.Y, Btn.W, Btn.H), format);
                }
            }
            return screen;
        }

        private static Bitmap Crop(Bitmap source, Point centre, int side) =>
            source.Clone(new Rectangle(centre.X - side / 2, centre.Y - side / 2, side, side), PixelFormat.Format32bppArgb);
    }
}
