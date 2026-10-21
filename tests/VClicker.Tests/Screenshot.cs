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
