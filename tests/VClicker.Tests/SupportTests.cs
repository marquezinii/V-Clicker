using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using static VClicker.Tests.Program;

namespace VClicker.Tests
{
    /// <summary>The "Support ♥" link: Ko-fi and Pix, nothing that nags.</summary>
    internal static class SupportTests
    {
        private static IEnumerable<Control> All(Control root) => root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(All(c)));

        private sealed class NullSink : IClickSink
        {
            public bool Click(ClickButton button, bool doubleClick, Point? at) => true;
        }

        public static void Run()
        {
            Test("support: the footer has a Support link in both levels", () =>
            {
                string ini = Path.Combine(Path.GetTempPath(), "vclicker-support-" + Guid.NewGuid().ToString("N") + ".ini");
                try
                {
                    foreach (bool advanced in new[] { false, true })
                    {
                        new ClickSettings { Advanced = advanced, Language = "en" }.Save(ini);
                        using (var form = new MainForm(ini, new NullSink()) { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) })
                        {
                            form.Show();
                            LinkLabel credits = All(form).OfType<LinkLabel>().First(l => l.Text.Contains("GitHub"));
                            True(credits.Visible && credits.Links.Cast<LinkLabel.Link>().Any(l => (l.LinkData as string) == MainForm.SupportLink),
                                 (advanced ? "advanced" : "simple") + ": " + credits.Text);
                            True(credits.Text.Contains(Loc.T("support.link")), credits.Text);
                            True(credits.Text.Contains("  v") == advanced, "the version only where the footer has room: " + credits.Text);
                        }
                    }
                }
                finally { File.Delete(ini); }
            });

            Test("support: the window offers Ko-fi and copies the Pix key", () =>
            {
                using (var dialog = new SupportDialog())
                {
                    IntPtr handle = dialog.Handle;
                    Equal("https://ko-fi.com/vemryx", SupportDialog.KofiUrl, "Ko-fi page");
                    True(All(dialog).OfType<TextBox>().Any(t => t.Text == SupportDialog.PixKey && t.ReadOnly), "the Pix key, read-only");
                    dialog.CopyPix();
                    True(dialog.StatusText == Loc.T("support.copied") || dialog.StatusText == SupportDialog.PixKey, "copied (or shown when the clipboard is busy)");
                }
            });
        }
    }
}
