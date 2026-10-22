using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace VClicker.Tests
{
    /// <summary>
    /// Renders the real window to a PNG: <c>--screenshot out.png [pt|en] [running] [clean]</c>.
    /// Uses a no-op click sink, so it never clicks anything.
    /// </summary>
    internal static class Screenshot
    {
        public static int Take(string output, string[] options)
        {
            string language = Array.IndexOf(options, "en") >= 0 ? "en" : "pt";
            bool running = Array.IndexOf(options, "running") >= 0;

            string ini = Path.Combine(Path.GetTempPath(), "vclicker-shot-" + Guid.NewGuid().ToString("N") + ".ini");
            if (Array.IndexOf(options, "studio") >= 0) return TakeStudio(output, language, ini, Array.IndexOf(options, "smart") >= 0);
            if (Array.IndexOf(options, "target") >= 0) return TakeTarget(output, language, Array.IndexOf(options, "look") >= 0);
            if (Array.IndexOf(options, "condition") >= 0) return TakeCondition(output, language);
            if (Array.IndexOf(options, "simulation") >= 0) return TakeSimulation(output, language);
            if (Array.IndexOf(options, "review") >= 0) return TakeReview(output, language);
            if (Array.IndexOf(options, "profiles") >= 0) return TakeProfiles(output, language);
            if (Array.IndexOf(options, "ai") >= 0) return TakeAi(output, language);
            if (Array.IndexOf(options, "support") >= 0) return TakeDialog(output, language, new SupportDialog());
            try
            {
                new ClickSettings { Language = language, StartDelaySeconds = 0, Sounds = false, IntervalMs = 100, Advanced = Array.IndexOf(options, "simple") < 0 }.Save(ini);
                using (var form = new MainForm(ini, new Program.NullSink()))
                {
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(60, 40);
                    form.TopMost = true;
                    form.Show();
                    // "small" simulates a 1366x768 laptop: a window too tall for the screen must scroll.
                    if (Array.IndexOf(options, "small") >= 0) form.FitToScreen(new Rectangle(0, 0, 1366, 600));
                    form.Activate();
                    if (running) form.Toggle(false);
                    Pump(running ? 1200 : 500);

                    if (Array.IndexOf(options, "refresh") >= 0) { form.Refresh(); Pump(300); }
                    Rectangle bounds = form.Bounds;
                    using (var picture = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
                    {
                        if (Array.IndexOf(options, "dtb") >= 0)
                            form.DrawToBitmap(picture, new Rectangle(Point.Empty, bounds.Size)); // paints controls only, no desktop involved
                        else
                            using (Graphics g = Graphics.FromImage(picture))
                                g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                        picture.Save(output, ImageFormat.Png);
                    }
                    Console.WriteLine("saved " + output + " (" + bounds.Width + "x" + bounds.Height + ", scale " + Theme.Scale + "x)");
                    form.Engine.Stop();
                }
                return 0;
            }
            finally
            {
                File.Delete(ini);
            }
        }

        /// <summary>The Macro Studio with the 60-reports example (<c>--screenshot out.png pt studio</c>).</summary>
        private static int TakeStudio(string output, string language, string ini, bool smart)
        {
            Loc.SetLanguage(language);
            var macro = new Macro { Name = language == "en" ? "Friday reports" : "Relatórios de sexta", ExeName = "msedge.exe", Repeat = 60 };
            macro.Steps.AddRange(new[]
            {
                new MacroStep { Kind = StepKind.Key, Key = Keys.Enter },
                new MacroStep { Kind = StepKind.Wait, DurationMs = 1500, RecordedMs = 1500 },
                new MacroStep { Kind = StepKind.Click, At = new Point(812, 440) },
                new MacroStep { Kind = StepKind.Wait, DurationMs = 8000, RecordedMs = 8000 },
                new MacroStep { Kind = StepKind.Wait, DurationMs = 25000, RecordedMs = 25000 },
                new MacroStep
                {
                    Kind = StepKind.Click, At = new Point(640, 520),
                    Target = smart ? new SmartTarget { Kind = TargetKind.Element, ControlType = "Button", Name = language == "en" ? "Download PDF" : "Baixar PDF" } : null
                },
                new MacroStep { Kind = StepKind.Wait, DurationMs = 1300, RecordedMs = 1300 },
                new MacroStep { Kind = StepKind.Click, At = new Point(1210, 96), Window = new WindowRef { Class = "#32770", Title = language == "en" ? "Save as" : "Salvar como" } },
                new MacroStep { Kind = StepKind.Type, Text = language == "en" ? "Monthly report" : "Relatório mensal" },
                new MacroStep { Kind = StepKind.Key, Key = Keys.Enter },
                new MacroStep { Kind = StepKind.Wait, DurationMs = 900, RecordedMs = 900 },
                new MacroStep { Kind = StepKind.Shortcut, Key = Keys.Left | Keys.Alt },
                new MacroStep { Kind = StepKind.Key, Key = Keys.Down },
                new MacroStep { Kind = StepKind.Wait, DurationMs = 1000, RecordedMs = 1000 }
            });
            using (var studio = new MacroStudio(null, new ClickSettings { Language = language }, ini))
            {
                studio.Open(macro, null);
                studio.StartPosition = FormStartPosition.Manual;
                studio.Location = new Point(60, 40);
                studio.Show();
                studio.Selected = smart ? 5 : 4;
                Pump(600);
                Rectangle bounds = studio.Bounds;
                using (var picture = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
                {
                    studio.DrawToBitmap(picture, new Rectangle(Point.Empty, bounds.Size));
                    picture.Save(output, ImageFormat.Png);
                }
                Console.WriteLine("saved " + output + " (" + bounds.Width + "x" + bounds.Height + ", scale " + Theme.Scale + "x)");
            }
            return 0;
        }

        /// <summary>The "wait until" dialog after F7 on a button (<c>--screenshot out.png pt condition</c>).</summary>
        private static int TakeCondition(string output, string language)
        {
            Loc.SetLanguage(language);
            var probe = new ConditionProbe
            {
                At = new Point(640, 520), ElementName = language == "en" ? "Download PDF" : "Baixar PDF", ControlType = "Button", Rgb = 0x3A6BD1
            };
            using (var dialog = new MacroStudio.ConditionDialog(probe, 30000))
            {
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(60, 40);
                dialog.Show();
                Pump(500);
                Rectangle bounds = dialog.Bounds;
                using (var picture = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
                {
                    dialog.DrawToBitmap(picture, new Rectangle(Point.Empty, bounds.Size));
                    picture.Save(output, ImageFormat.Png);
                }
            }
            return 0;
        }

        /// <summary>SafeClick's simulation on a fabricated look at a reports page (<c>--screenshot out.png pt simulation</c>).</summary>
        private static int TakeSimulation(string output, string language)
        {
            Loc.SetLanguage(language);
            bool en = language == "en";
            var page = new Bitmap(800, 480, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(page))
            using (var font = new Font("Segoe UI", 11f))
            using (var bold = new Font("Segoe UI Semibold", 14f))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.Clear(Color.FromArgb(240, 242, 245));
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.FillRectangle(Brushes.White, 40, 40, 720, 400);
                g.DrawString(en ? "Monthly report" : "Relatório mensal", bold, Brushes.Black, 70, 70);
                g.DrawRectangle(Pens.Silver, 70, 130, 660, 44);
                g.DrawString(en ? "Record #001" : "Registro #001", font, Brushes.Gray, 82, 142);
                using (var fill = new SolidBrush(Color.FromArgb(230, 234, 240))) g.FillRectangle(fill, 70, 360, 170, 44);
                g.DrawString(en ? "Generate report" : "Gerar relatório", font, Brushes.Black, new RectangleF(70, 360, 170, 44), format);
                using (var fill = new SolidBrush(Color.FromArgb(47, 111, 214))) g.FillRectangle(fill, 590, 360, 140, 44);
                g.DrawString(en ? "Download PDF" : "Baixar PDF", font, Brushes.White, new RectangleF(590, 360, 140, 44), format);
            }
            string pdf = en ? "Download PDF" : "Baixar PDF";
            var macro = new Macro { Name = en ? "Friday reports" : "Relatórios de sexta", ExeName = "chrome.exe", Repeat = 60 };
            macro.Steps.Add(new MacroStep { Kind = StepKind.Click, At = new Point(400, 152) });
            macro.Steps.Add(new MacroStep { Kind = StepKind.Type, Text = en ? "Record #001" : "Registro #001" });
            macro.Steps.Add(new MacroStep { Kind = StepKind.Click, At = new Point(155, 382) });
            macro.Steps.Add(new MacroStep { Kind = StepKind.WaitFor, Condition = ConditionKind.Element, ControlType = "Button", Text = pdf, DurationMs = 30000 });
            macro.Steps.Add(new MacroStep { Kind = StepKind.Click, Target = new SmartTarget { Kind = TargetKind.Element, ControlType = "Button", Name = pdf } });
            macro.Steps.Add(new MacroStep { Kind = StepKind.Shortcut, Key = Keys.Delete | Keys.Shift });
            macro.Steps.Add(new MacroStep { Kind = StepKind.Shortcut, Key = Keys.F4 | Keys.Alt });
            var window = new IntPtr(1);
            Func<System.Threading.CancellationToken, MacroPreviewResult> evaluate = cancel =>
            {
                var result = new MacroPreviewResult { WindowFound = true, Impactful = SafeClick.Impactful(macro) };
                result.Shots[window] = new WindowShot((Bitmap)page.Clone(), new Rectangle(0, 0, 800, 480), ShotProblem.None);
                var pdfBounds = new Rectangle(590, 360, 140, 44);
                result.Steps.Add(new StepPreview { Index = 0, Kind = StepKind.Click, State = PreviewState.InCapture, Window = window, At = new Point(400, 152) });
                result.Steps.Add(new StepPreview { Index = 1, Kind = StepKind.Type, State = PreviewState.NoPosition });
                result.Steps.Add(new StepPreview { Index = 2, Kind = StepKind.Click, State = PreviewState.InCapture, Window = window, At = new Point(155, 382) });
                result.Steps.Add(new StepPreview { Index = 3, Kind = StepKind.WaitFor, State = PreviewState.Verified, Window = window, At = new Point(660, 382), Bounds = pdfBounds, Met = true });
                result.Steps.Add(new StepPreview { Index = 4, Kind = StepKind.Click, State = PreviewState.Verified, Window = window, At = new Point(660, 382), Bounds = pdfBounds });
                result.Steps.Add(new StepPreview { Index = 5, Kind = StepKind.Shortcut, State = PreviewState.NoPosition });
                result.Steps.Add(new StepPreview { Index = 6, Kind = StepKind.Shortcut, State = PreviewState.NoPosition });
                return result;
            };
            using (var dialog = new SimulationDialog(macro, evaluate, 4))
            {
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(60, 40);
                dialog.Show();
                Pump(800);
                Rectangle bounds = dialog.Bounds;
                using (var picture = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
                {
                    dialog.DrawToBitmap(picture, new Rectangle(Point.Empty, bounds.Size));
                    picture.Save(output, ImageFormat.Png);
                }
            }
            page.Dispose();
            return 0;
        }

        /// <summary>SafeClick's safety review before a run (<c>--screenshot out.png pt review</c>).</summary>
        private static int TakeReview(string output, string language)
        {
            Loc.SetLanguage(language);
            var actions = new System.Collections.Generic.List<ImpactfulAction>
            {
                new ImpactfulAction { Step = 7, Key = Keys.Delete | Keys.Shift, Deletes = true },
                new ImpactfulAction { Step = 11, Key = Keys.F4 | Keys.Alt }
            };
            using (var dialog = new SafetyReview(actions))
            {
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(60, 40);
                dialog.Show();
                Pump(500);
                Rectangle bounds = dialog.Bounds;
                using (var picture = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
                {
                    dialog.DrawToBitmap(picture, new Rectangle(Point.Empty, bounds.Size));
                    picture.Save(output, ImageFormat.Png);
                }
            }
            return 0;
        }

        /// <summary>Any dialog as it opens (<c>--screenshot out.png pt support</c>).</summary>
        private static int TakeDialog(string output, string language, Form dialog)
        {
            Loc.SetLanguage(language);
            using (dialog)
            {
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(60, 40);
                dialog.Show();
                Pump(500);
                Rectangle bounds = dialog.Bounds;
                using (var picture = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
                {
                    dialog.DrawToBitmap(picture, new Rectangle(Point.Empty, bounds.Size));
                    picture.Save(output, ImageFormat.Png);
                }
            }
            return 0;
        }

        /// <summary>The AI Macro Builder window, first use (<c>--screenshot out.png pt ai</c>).</summary>
        private static int TakeAi(string output, string language)
        {
            Loc.SetLanguage(language);
            using (var dialog = new AiDialog(new ClickSettings(), null, "Roblox", new Macro { ExeName = "RobloxPlayerBeta.exe" }))
            {
                dialog.Description = language == "en"
                    ? "Click this button every two seconds, but only while this window is open."
                    : "Clique neste botão a cada dois segundos, mas só enquanto esta janela estiver aberta.";
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(60, 40);
                dialog.Show();
                Pump(500);
                Rectangle bounds = dialog.Bounds;
                using (var picture = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
                {
                    dialog.DrawToBitmap(picture, new Rectangle(Point.Empty, bounds.Size));
                    picture.Save(output, ImageFormat.Png);
                }
            }
            return 0;
        }

        /// <summary>The per-app profiles window (<c>--screenshot out.png pt profiles</c>).</summary>
        private static int TakeProfiles(string output, string language)
        {
            Loc.SetLanguage(language);
            string dir = Path.Combine(Path.GetTempPath(), "vclicker-shot-" + Guid.NewGuid().ToString("N"));
            try
            {
                string macros = Path.Combine(dir, "Macros");
                var examples = new[] { Tuple.Create("RobloxPlayerBeta.exe", "Roblox", 1000.0 / 15, ClickButton.Left, 2),
                                       Tuple.Create("Cookie Clicker.exe", "Cookie Clicker", 20.0, ClickButton.Left, 0),
                                       Tuple.Create("javaw.exe", "Minecraft", 100.0, ClickButton.Right, 1) };
                int day = 0;
                foreach (var example in examples)
                {
                    var profile = new AppProfile { ExeName = example.Item1, Name = example.Item2, UsedUtc = DateTime.UtcNow.AddDays(-day++) };
                    profile.Settings.IntervalMs = example.Item3;
                    profile.Settings.Button = example.Item4;
                    Profiles.Save(dir, profile);
                    for (int i = 0; i < example.Item5; i++)
                        MacroFile.Save(new Macro { Name = example.Item2 + " " + i, ExeName = example.Item1 }, Path.Combine(macros, example.Item2 + i + MacroFile.Extension));
                }
                using (var dialog = new ProfilesDialog(dir, macros, null))
                {
                    dialog.StartPosition = FormStartPosition.Manual;
                    dialog.Location = new Point(60, 40);
                    dialog.Show();
                    Pump(500);
                    Rectangle bounds = dialog.Bounds;
                    using (var picture = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
                    {
                        dialog.DrawToBitmap(picture, new Rectangle(Point.Empty, bounds.Size));
                        picture.Save(output, ImageFormat.Png);
                    }
                }
                return 0;
            }
            finally { Directory.Delete(dir, true); }
        }

        /// <summary>The "smart target" dialog after F7 on a button (<c>--screenshot out.png pt target [look]</c>).</summary>
        private static int TakeTarget(string output, string language, bool look)
        {
            Loc.SetLanguage(language);
            string word = language == "en" ? "Download" : "Baixar";
            var screen = new Bitmap(400, 120, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(screen))
            using (var font = new Font("Segoe UI", 10f))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.Clear(Color.FromArgb(244, 246, 248));
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                foreach (var b in new[] { Tuple.Create("CSV", 40), Tuple.Create("PDF", 200) })
                {
                    using (var fill = new SolidBrush(Color.FromArgb(47, 111, 214))) g.FillRectangle(fill, b.Item2, 44, 120, 32);
                    g.DrawString(word + " " + b.Item1, font, Brushes.White, new RectangleF(b.Item2, 44, 120, 32), format);
                }
            }
            var probe = new TargetProbe
            {
                At = new Point(260, 60), ElementPreview = screen.Clone(new Rectangle(200, 44, 120, 32), PixelFormat.Format32bppArgb),
                Element = look ? null : new ElementProbe { ControlType = "Button", Name = word + " PDF", Count = 1 }
            };
            probe.Images = ImageTarget.Options(screen, probe.At);
            using (screen)
            using (probe)
            using (var dialog = new MacroStudio.TargetDialog(probe, SmartTarget.DefaultTimeoutMs))
            {
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Location = new Point(60, 40);
                dialog.Show();
                Pump(500);
                Rectangle bounds = dialog.Bounds;
                using (var picture = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
                {
                    dialog.DrawToBitmap(picture, new Rectangle(Point.Empty, bounds.Size));
                    picture.Save(output, ImageFormat.Png);
                }
            }
            return 0;
        }

        private static void Pump(int milliseconds)
        {
            var end = DateTime.UtcNow.AddMilliseconds(milliseconds);
            while (DateTime.UtcNow < end)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
        }
    }
}
