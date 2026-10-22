using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using static VClicker.Tests.Program;

namespace VClicker.Tests
{
    /// <summary>Adaptive interface (docs/superpowers/specs/2026-10-21-adaptive-interface-design.md).</summary>
    internal static class AdaptiveTests
    {
        private static IEnumerable<Control> All(Control root) => root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(All(c)));

        private static T Find<T>(Control root, Func<T, bool> match) where T : Control => All(root).OfType<T>().First(match);

        private sealed class NullSink : IClickSink
        {
            public bool Click(ClickButton button, bool doubleClick, Point? at) => true;
        }

        private static MainForm Open(string ini)
        {
            var form = new MainForm(ini, new NullSink()) { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false };
            form.Show();
            Application.DoEvents();
            return form;
        }

        private static string TempIni() => Path.Combine(Path.GetTempPath(), "vclicker-level-" + Guid.NewGuid().ToString("N") + ".ini");

        public static void Run()
        {
            Test("settings: a new install starts simple, an existing one stays advanced, the choice is saved", () =>
            {
                string ini = TempIni();
                try
                {
                    True(!ClickSettings.Load(ini).Advanced, "no settings file: simple");
                    File.WriteAllLines(ini, new[] { "IntervalMs=50" });
                    True(ClickSettings.Load(ini).Advanced, "a file from an older version: advanced, nothing disappears");
                    new ClickSettings { Advanced = false }.Save(ini);
                    True(!ClickSettings.Load(ini).Advanced, "simple, saved");
                    new ClickSettings { Advanced = true }.Save(ini);
                    True(ClickSettings.Load(ini).Advanced, "advanced, saved");
                }
                finally { File.Delete(ini); }
            });

            Test("simple mode: one narrow column with the essentials; advanced brings everything back", () =>
            {
                string ini = TempIni();
                try
                {
                    new ClickSettings { Advanced = false, Language = "en" }.Save(ini);
                    using (MainForm form = Open(ini))
                    {
                        Segmented level = Find<Segmented>(form, s => s.AccessibleName == "Interface level");
                        Equal(0, level.SelectedIndex, "simple selected");
                        True(Find<NumberField>(form, f => f.FieldName == "Clicks per second").Visible, "CPS");
                        True(Find<Segmented>(form, s => s.AccessibleName == "Mouse button (simple)").Visible, "mouse button");
                        True(Find<HotkeyBox>(form, h => h.AccessibleName == "Start or stop hotkey").Visible, "hotkeys");
                        True(!Find<LedCheck>(form, c => c.AccessibleName == "Window Lock").Visible, "no Window Lock");
                        True(!Find<NumberField>(form, f => f.FieldName == "Random variation percent").Visible, "no jitter");
                        True(!Find<Segmented>(form, s => s.AccessibleName == "Speed unit").Visible, "always CPS");
                        True(!Find<KeyButton>(form, b => b.Text == Loc.T("studio.open")).Visible, "no Macro Studio button");
                        int simpleWidth = form.Width;

                        level.SelectedIndex = 1;
                        Application.DoEvents();
                        True(form.Settings.Advanced, "advanced chosen");
                        True(ClickSettings.Load(ini).Advanced, "and saved");
                        True(Find<LedCheck>(form, c => c.AccessibleName == "Window Lock").Visible, "Window Lock back");
                        True(Find<KeyButton>(form, b => b.Text == Loc.T("studio.open")).Visible, "Macro Studio back");
                        True(!Find<Segmented>(form, s => s.AccessibleName == "Mouse button (simple)").Visible, "one mouse button choice only");
                        True(form.Width > simpleWidth + Theme.Dp(300), "the window widens: " + simpleWidth + " -> " + form.Width);

                        level.SelectedIndex = 0;
                        Application.DoEvents();
                        Equal(simpleWidth, form.Width, "and narrows again");
                    }
                }
                finally { File.Delete(ini); }
            });

            Test("simple mode: the mouse button choice is the same setting as the advanced one", () =>
            {
                string ini = TempIni();
                try
                {
                    new ClickSettings { Advanced = false }.Save(ini);
                    using (MainForm form = Open(ini))
                    {
                        Find<Segmented>(form, s => s.AccessibleName == "Mouse button (simple)").SelectedIndex = 2;
                        Equal(ClickButton.Right, form.Settings.Button, "right button");
                        Equal(ClickButton.Right, ClickSettings.Load(ini).Button, "saved");
                        Find<Segmented>(form, s => s.AccessibleName == "Interface level").SelectedIndex = 1;
                        Equal(2, Find<Segmented>(form, s => s.AccessibleName == "Mouse button").SelectedIndex, "the advanced choice agrees");
                    }
                }
                finally { File.Delete(ini); }
            });

            Test("simple mode: settings hidden from it still apply, and a line says which", () =>
            {
                string ini = TempIni();
                try
                {
                    new ClickSettings { Advanced = false, Language = "en" }.Save(ini);
                    using (MainForm form = Open(ini))
                        True(!All(form).OfType<Label>().Any(l => l.Visible && l.Text.StartsWith("Also in effect")), "nothing to say with the defaults");

                    new ClickSettings
                    {
                        Advanced = false, Language = "en", WindowLock = true, LockExe = "Game.exe", LockName = "Game",
                        UseFixedPosition = true, X = 812, Y = 440, RepeatCount = 100
                    }.Save(ini);
                    using (MainForm form = Open(ini))
                    {
                        Label note = All(form).OfType<Label>().FirstOrDefault(l => l.Visible && l.Text.StartsWith("Also in effect"));
                        True(note != null, "the line is shown");
                        True(note.Text.Contains("Window Lock (Game)") && note.Text.Contains("812, 440") && note.Text.Contains("100 clicks"), note.Text);
                        True(form.Settings.WindowLock && form.Settings.UseFixedPosition, "still in effect");
                    }
                }
                finally { File.Delete(ini); }
            });

            Test("simple mode: a Window Lock without an app is mentioned, and Restart as admin stays reachable", () =>
            {
                string ini = TempIni();
                try
                {
                    new ClickSettings { Advanced = false, Language = "en", WindowLock = true }.Save(ini);
                    using (MainForm form = Open(ini))
                    {
                        Label note = All(form).OfType<Label>().FirstOrDefault(l => l.Visible && l.Text.StartsWith("Also in effect"));
                        True(note != null && note.Text.Contains("Window Lock (no app)"), "Start is refused for it: say why: " + note?.Text);
                        if (!MainForm.IsElevated())
                            True(Find<LinkLabel>(form, l => l.Text == Loc.T("link.admin")).Visible, "messages name this link: it is shown in simple too");
                    }
                }
                finally { File.Delete(ini); }
            });

            Test("window: widening keeps it on the screen", () =>
            {
                string ini = TempIni();
                try
                {
                    new ClickSettings { Advanced = false }.Save(ini);
                    using (MainForm form = Open(ini))
                    {
                        Rectangle area = Screen.FromControl(form).WorkingArea;
                        form.Location = new Point(area.Right - form.Width, area.Top);
                        Find<Segmented>(form, s => s.AccessibleName == "Interface level").SelectedIndex = 1;
                        Application.DoEvents();
                        // A screen narrower than the window (1024 px CI runners): its left edge stays on the screen.
                        True(form.Width > area.Width ? form.Left == area.Left : form.Right <= area.Right,
                             "window " + form.Left + ".." + form.Right + " on screen " + area.Left + ".." + area.Right);
                    }
                }
                finally { File.Delete(ini); }
            });

            Test("simple mode: Start still clicks", () =>
            {
                string ini = TempIni();
                try
                {
                    new ClickSettings { Advanced = false, StartDelaySeconds = 0, Sounds = false }.Save(ini);
                    using (MainForm form = Open(ini))
                    {
                        form.Toggle(false);
                        True(form.ClickerBusy, "running");
                        Find<Segmented>(form, s => s.AccessibleName == "Interface level").SelectedIndex = 1;
                        True(form.ClickerBusy, "switching the level never stops a run");
                        form.Engine.Stop();
                    }
                }
                finally { File.Delete(ini); }
            });
        }
    }
}
