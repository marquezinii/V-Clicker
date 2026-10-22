using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using static VClicker.Tests.Program;

namespace VClicker.Tests
{
    /// <summary>Profiles V2 (docs/superpowers/specs/2026-10-21-profiles-design.md).</summary>
    internal static class ProfileTests
    {
        private static string TempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "vclicker-profiles-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static Macro MacroFor(string exe, string name, int x)
        {
            var macro = new Macro { Name = name, ExeName = exe };
            macro.Steps.Add(new MacroStep { Kind = StepKind.Click, At = new Point(x, 20) });
            return macro;
        }

        private static IEnumerable<Control> All(Control root) => root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(All(c)));

        public static void Run()
        {
            Test("profiles: a profile carries the click settings, never hotkeys or window options", () =>
            {
                var source = new ClickSettings
                {
                    IntervalMs = 1000.0 / 15, SpeedInCps = false, JitterPercent = 7, Button = ClickButton.Right, DoubleClick = true,
                    RepeatCount = 40, UseFixedPosition = true, X = 300, Y = -20, StartDelaySeconds = 4, HoldMode = true,
                    LockStopOnFocusLoss = true, ToggleKey = new Hotkey(Keys.F9), Sounds = false, AlwaysOnTop = true, Language = "en"
                };
                var target = new ClickSettings();
                Profiles.CopyFields(source, target);
                Equal(1000.0 / 15, target.IntervalMs, "exact speed");
                True(!target.SpeedInCps && target.JitterPercent == 7 && target.Button == ClickButton.Right && target.DoubleClick, "click");
                True(target.RepeatCount == 40 && target.UseFixedPosition && target.X == 300 && target.Y == -20, "repeat and position");
                True(target.StartDelaySeconds == 4 && target.HoldMode && target.LockStopOnFocusLoss, "conditions");
                Equal(ClickSettings.DefaultToggle, target.ToggleKey, "hotkeys stay global");
                True(target.Sounds && !target.AlwaysOnTop && target.Language == "auto", "window options stay global");
            });

            Test("profiles: saved per app, listed most recent first, loaded by exe", () =>
            {
                string dir = TempDir();
                try
                {
                    var older = new AppProfile { ExeName = "Game.exe", Name = "Game", UsedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
                    older.Settings.IntervalMs = 50;
                    var newer = new AppProfile { ExeName = "Other.exe", Name = "Other", UsedUtc = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc) };
                    True(Profiles.Save(dir, older) && Profiles.Save(dir, newer), "saved");
                    True(File.Exists(Path.Combine(dir, "Game.exe" + Profiles.Extension)), "one file per executable");
                    Equal("Other.exe|Game.exe", string.Join("|", Profiles.List(dir).Select(p => p.ExeName)), "most recent first");
                    AppProfile loaded = Profiles.Load(dir, "game.EXE");
                    True(loaded != null, "found regardless of case");
                    Equal(50.0, loaded.Settings.IntervalMs, "speed");
                    Equal("Game", loaded.Name, "name");
                    Equal(older.UsedUtc, loaded.UsedUtc, "last use");
                    True(Profiles.Load(dir, "none.exe") == null, "no profile");
                    True(Profiles.Load(Path.Combine(dir, "missing"), "Game.exe") == null, "no folder");
                    True(Profiles.Delete(dir, "Game.exe") && Profiles.Load(dir, "Game.exe") == null, "deleted");
                }
                finally { Directory.Delete(dir, true); }
            });

            Test("profiles: reading clamps values, ignores global keys and rejects what is not a profile", () =>
            {
                string text = "V-Clicker profile 1\nexe=Game.exe\nname=Game\nIntervalMs=-5\nJitterPercent=900\nToggleKey=F2\nSounds=False\n";
                AppProfile profile = Profiles.Read(text, new List<Macro>(), out int skipped);
                Equal(Interval.Clamp(-5), profile.Settings.IntervalMs, "clamped speed");
                Equal(50, profile.Settings.JitterPercent, "clamped jitter");
                Equal(ClickSettings.DefaultToggle, profile.Settings.ToggleKey, "a hotkey in the file is ignored");
                True(profile.Settings.Sounds, "a window option in the file is ignored");
                Equal(0, skipped, "nothing skipped");
                foreach (string bad in new[] { "", "hello", "V-Clicker macro 1\nexe=Game.exe", "V-Clicker profile 1\nexe=..\\evil", "V-Clicker profile 1\nname=x" })
                {
                    bool threw = false;
                    try { Profiles.Read(bad, new List<Macro>(), out _); }
                    catch (FormatException) { threw = true; }
                    True(threw, "rejected: " + bad.Replace("\n", "\\n"));
                }
            });

            Test("profiles: export carries the app's macros; import adds them once", () =>
            {
                string home = TempDir(), away = TempDir();
                try
                {
                    string macros = Path.Combine(home, "Macros");
                    MacroFile.Save(MacroFor("Game.exe", "Farm", 10), Path.Combine(macros, "Farm.vcmacro"));
                    MacroFile.Save(MacroFor("game.exe", "Collect", 11), Path.Combine(macros, "Collect.vcmacro"));
                    MacroFile.Save(MacroFor("Other.exe", "Mail", 12), Path.Combine(macros, "Mail.vcmacro"));
                    File.WriteAllText(Path.Combine(macros, "Broken.vcmacro"), "not a macro");
                    var profile = new AppProfile { ExeName = "Game.exe", Name = "Game", UsedUtc = DateTime.UtcNow };
                    profile.Settings.IntervalMs = 40;
                    Equal("Collect|Farm", string.Join("|", Profiles.MacrosOf(macros, "Game.exe").Select(m => m.Name).OrderBy(n => n)), "the app's macros only");

                    string file = Path.Combine(away, "Game" + Profiles.Extension);
                    Profiles.Export(profile, macros, file);
                    AppProfile read = Profiles.ReadFile(file, out List<Macro> carried, out int skipped);
                    Equal("Game.exe", read.ExeName, "exe");
                    Equal(40.0, read.Settings.IntervalMs, "speed");
                    Equal(2, carried.Count, "two macros carried");
                    Equal(0, skipped, "none skipped");

                    string target = Path.Combine(away, "Macros");
                    Equal(2, Profiles.AddMacros(target, carried), "both added");
                    Equal(0, Profiles.AddMacros(target, carried), "importing again adds no duplicates");
                    Macro changed = MacroFor("Game.exe", "Farm", 99);
                    Equal(1, Profiles.AddMacros(target, new List<Macro> { changed }), "same name, other steps: added");
                    True(File.Exists(Path.Combine(target, "Farm (2).vcmacro")), "under a new name");
                    Equal(99, MacroFile.Load(Path.Combine(target, "Farm (2).vcmacro")).Steps[0].At.X, "its own content");
                }
                finally
                {
                    Directory.Delete(home, true);
                    Directory.Delete(away, true);
                }
            });

            Test("profiles: an import skips unreadable macros and macros of another app", () =>
            {
                string good = MacroFile.Write(MacroFor("Game.exe", "Farm", 10));
                string other = MacroFile.Write(MacroFor("Other.exe", "Mail", 10));
                string text = "V-Clicker profile 1\nexe=Game.exe\nname=Game\nIntervalMs=100\n[macro]\n" + good + "[macro]\ngarbage\n[macro]\n" + other;
                var macros = new List<Macro>();
                Profiles.Read(text, macros, out int skipped);
                Equal(1, macros.Count, "the good one");
                Equal(2, skipped, "the broken one and the other app's");
            });

            Test("profiles: a file over the size limit is refused", () =>
            {
                string dir = TempDir();
                try
                {
                    string file = Path.Combine(dir, "big" + Profiles.Extension);
                    using (FileStream stream = File.Create(file)) stream.SetLength(Profiles.MaxFileBytes + 1);
                    bool threw = false;
                    try { Profiles.ReadFile(file, out _, out _); }
                    catch (FormatException) { threw = true; }
                    True(threw, "too big");
                }
                finally { Directory.Delete(dir, true); }
            });

            Test("main window: choosing an app loads its profile, and changes update it", () =>
            {
                string dir = TempDir();
                string ini = Path.Combine(dir, "settings.ini");
                try
                {
                    new ClickSettings { IntervalMs = 100, Language = "en" }.Save(ini);
                    string folder = Profiles.Folder(ini);
                    var saved = new AppProfile { ExeName = "Game.exe", Name = "Game", UsedUtc = DateTime.UtcNow };
                    saved.Settings.IntervalMs = 50;
                    saved.Settings.Button = ClickButton.Right;
                    Profiles.Save(folder, saved);
                    using (var form = new MainForm(ini, new FakeProfileSink()))
                    {
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new Point(-20000, -20000);
                        IntPtr handle = form.Handle;
                        form.UseLockApp(new AppWindow { ExeName = "Game.exe", DisplayName = "Game" });
                        Equal(50.0, form.Settings.IntervalMs, "the app's speed");
                        Equal(ClickButton.Right, form.Settings.Button, "the app's button");
                        True(form.Settings.WindowLock, "choosing an app turns the lock on");
                        NumberField cps = All(form).OfType<NumberField>().First(f => f.FieldName == "Clicks per second");
                        Equal(20.0, cps.Number, "the window shows it");

                        ((TextBox)cps.Controls[0]).Text = "25";
                        cps.Commit();
                        Equal(40.0, Profiles.Load(folder, "Game.exe").Settings.IntervalMs, "a change updates the profile");

                        form.UseLockApp(new AppWindow { ExeName = "New.exe", DisplayName = "New" });
                        Equal(40.0, form.Settings.IntervalMs, "an app without a profile starts from the current settings");
                        True(Profiles.Load(folder, "New.exe") != null, "and gets one");
                        form.UseLockApp(new AppWindow { ExeName = "Game.exe", DisplayName = "Game" });
                        Equal(ClickButton.Right, form.Settings.Button, "back to the first app's profile");
                    }
                    Equal(40.0, ClickSettings.Load(ini).IntervalMs, "the global settings are the last used");
                }
                finally { Directory.Delete(dir, true); }
            });

            Test("main window: a profile is never loaded during a run", () =>
            {
                string dir = TempDir();
                string ini = Path.Combine(dir, "settings.ini");
                try
                {
                    new ClickSettings { IntervalMs = 100, StartDelaySeconds = 0, Sounds = false }.Save(ini);
                    var saved = new AppProfile { ExeName = "Game.exe", Name = "Game", UsedUtc = DateTime.UtcNow };
                    saved.Settings.IntervalMs = 50;
                    Profiles.Save(Profiles.Folder(ini), saved);
                    using (var form = new MainForm(ini, new FakeProfileSink()))
                    {
                        form.StartPosition = FormStartPosition.Manual;
                        form.Location = new Point(-20000, -20000);
                        IntPtr handle = form.Handle;
                        form.Toggle(false);
                        True(form.ClickerBusy, "running");
                        form.UseLockApp(new AppWindow { ExeName = "Game.exe", DisplayName = "Game" });
                        Equal(100.0, form.Settings.IntervalMs, "unchanged while running");
                        form.Engine.Stop();
                    }
                }
                finally { Directory.Delete(dir, true); }
            });

            Test("profiles window: lists the profiles with a summary and deletes one", () =>
            {
                string dir = TempDir();
                try
                {
                    Loc.SetLanguage("en");
                    var game = new AppProfile { ExeName = "Game.exe", Name = "Game", UsedUtc = DateTime.UtcNow };
                    game.Settings.IntervalMs = 1000.0 / 15;
                    Profiles.Save(dir, game);
                    string macros = Path.Combine(dir, "Macros");
                    MacroFile.Save(MacroFor("Game.exe", "Farm", 10), Path.Combine(macros, "Farm.vcmacro"));
                    using (var dialog = new ProfilesDialog(dir, macros, null) { Confirm = text => true })
                    {
                        IntPtr handle = dialog.Handle;
                        Equal(1, dialog.Items.Count, "one profile");
                        True(dialog.Items[0].Contains("Game") && dialog.Items[0].Contains("15 CPS") && dialog.Items[0].Contains("left") &&
                             dialog.Items[0].Contains("1 macro"), "summary: " + dialog.Items[0]);
                        dialog.DeleteSelected();
                        Equal(0, dialog.Items.Count, "deleted");
                        True(Profiles.Load(dir, "Game.exe") == null, "file gone");
                        True(File.Exists(Path.Combine(macros, "Farm.vcmacro")), "the app's macros stay");
                    }
                }
                finally
                {
                    Loc.SetLanguage("auto");
                    Directory.Delete(dir, true);
                }
            });
        }

        private sealed class FakeProfileSink : IClickSink
        {
            public bool Click(ClickButton button, bool doubleClick, Point? at) => true;
        }
    }
}
