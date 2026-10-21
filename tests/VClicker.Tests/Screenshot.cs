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
        private sealed class NullSink : IClickSink
        {
            public bool Click(ClickButton button, bool doubleClick, Point? at) => true;
        }

        public static int Take(string output, string[] options)
        {
            string language = Array.IndexOf(options, "en") >= 0 ? "en" : "pt";
            bool running = Array.IndexOf(options, "running") >= 0;

            string ini = Path.Combine(Path.GetTempPath(), "vclicker-shot-" + Guid.NewGuid().ToString("N") + ".ini");
            if (Array.IndexOf(options, "studio") >= 0) return TakeStudio(output, language, ini, Array.IndexOf(options, "smart") >= 0);
            if (Array.IndexOf(options, "target") >= 0) return TakeTarget(output, language, Array.IndexOf(options, "look") >= 0);
            if (Array.IndexOf(options, "condition") >= 0) return TakeCondition(output, language);
            try
            {
                new ClickSettings { Language = language, StartDelaySeconds = 0, Sounds = false, IntervalMs = 100 }.Save(ini);
                using (var form = new MainForm(ini, new NullSink()))
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
