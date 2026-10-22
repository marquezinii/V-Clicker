using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using static VClicker.Tests.Program;

namespace VClicker.Tests
{
    /// <summary>Developer Mode (docs/superpowers/specs/2026-10-21-developer-mode-design.md).</summary>
    internal static partial class MacroTests
    {
        private sealed class NullClickSink : IClickSink
        {
            public bool Click(ClickButton button, bool doubleClick, Point? at) => true;
        }

        private static void DeveloperTests()
        {
            Test("command line: macro, variables, log and exit", () =>
            {
                CommandLine cmd = CommandLine.Parse(new[] { "--macro", "Farm", "--var", "name=Ana", "--var", "eq=a=b", "--log", "run.jsonl", "--exit-when-done" });
                Equal(null, cmd.Error, "no error");
                Equal("Farm", cmd.Macro, "macro");
                Equal("Ana", cmd.Vars["name"], "variable");
                Equal("a=b", cmd.Vars["eq"], "a value may contain =");
                Equal("run.jsonl", cmd.LogPath, "log");
                True(cmd.ExitWhenDone && cmd.HasAction, "exit when done");

                Equal("Roblox", CommandLine.Parse(new[] { "--profile", "Roblox" }).Profile, "profile");
                True(CommandLine.Parse(new[] { "--help" }).Help, "help");
                True(CommandLine.Parse(new[] { "/?" }).Help, "help, Windows style");
                True(!CommandLine.Parse(new string[0]).HasAction, "no arguments: the normal window");
                True(!CommandLine.Parse(new[] { "--wait-previous" }).HasAction && CommandLine.Parse(new[] { "--wait-previous" }).Error == null, "the restart flag still works");
                foreach (string[] bad in new[]
                {
                    new[] { "--macro" }, new[] { "--var", "x" }, new[] { "--var", "=1" }, new[] { "--var", "bad name=1" },
                    new[] { "--macro", "a", "--profile", "b" }, new[] { "--frobnicate" }, new[] { "--exit-when-done" }, new[] { "--var", "a=1" }
                })
                    True(CommandLine.Parse(bad).Error != null, "rejected: " + string.Join(" ", bad));
            });

            Test("exit codes: done 0, stopped 1, other stops 10 and up", () =>
            {
                Equal(0, ExitCodes.For(MacroStopReason.Completed), "macro completed");
                Equal(1, ExitCodes.For(MacroStopReason.Manual), "macro stopped");
                Equal(10 + (int)MacroStopReason.TargetClosed, ExitCodes.For(MacroStopReason.TargetClosed), "window closed");
                Equal(0, ExitCodes.For(StopReason.Completed), "clicker completed");
                Equal(1, ExitCodes.For(StopReason.Manual), "clicker stopped");
                Equal(10 + (int)StopReason.Blocked, ExitCodes.For(StopReason.Blocked), "blocked");
            });

            Test("variables: found in typed text, defaults from the file, built-ins expanded", () =>
            {
                Macro macro = Steps(1,
                    new MacroStep { Kind = StepKind.Type, Text = "Hi {{name}}, run {{iteration}} on {{date}}" },
                    new MacroStep { Kind = StepKind.Type, Text = "{{city}} {{name}} {{not a var}}" });
                macro.ExeName = "app.exe";
                Equal("city|name", string.Join("|", MacroVariables.Used(macro)), "user variables, sorted, once each");
                macro.ExtraHeader.Add("var.city=Recife");
                Macro read = MacroFile.Read(MacroFile.Write(macro));
                Equal("Recife", MacroVariables.Defaults(read)["city"], "a default survives the file");
                Equal("name", string.Join("|", MacroVariables.Missing(read, new Dictionary<string, string>())), "only name lacks a value");
                Equal(0, MacroVariables.Missing(read, new Dictionary<string, string> { ["name"] = "Ana" }).Count, "all set");

                var values = new Dictionary<string, string> { ["name"] = "Ana", ["city"] = "Natal" };
                string typed = MacroVariables.Expand("Hi {{name}} #{{iteration}} {{city}} {{date}} {{time}} {{x y}}", values, 3);
                True(typed.StartsWith("Hi Ana #3 Natal " + DateTime.Now.ToString("yyyy-MM-dd")), typed);
                True(typed.EndsWith(" {{x y}}"), "not a variable name: left as written");
            });

            Test("player: typed text gets its variables, per repetition", () =>
            {
                var sink = new FakeInputSink();
                using (var player = new MacroPlayer(sink))
                {
                    Macro macro = Steps(2, new MacroStep { Kind = StepKind.Type, Text = "{{who}}{{iteration}}" });
                    True(player.Start(macro, Ok, 0, values: new Dictionary<string, string> { ["who"] = "Al" }), "started");
                    True(player.WaitForStop(10000), "finished");
                    Equal("Al1Al2", string.Concat(sink.Snapshot().Where(e => e.StartsWith("char ")).Select(e => e.Substring(5))), "typed");
                }
            });

            Test("run log: JSON lines with escaped strings, never typed text", () =>
            {
                string file = Path.Combine(Path.GetTempPath(), "vclicker-log-" + Guid.NewGuid().ToString("N"), "run.jsonl");
                try
                {
                    var log = new RunLog(file);
                    log.Write("run.start", "kind", "macro", "name", "Say \"hi\"\\now", "repeat", 2, "dry", false);
                    log.Write("run.stop", "reason", "Completed", "elapsedMs", 1500L);
                    string[] lines = File.ReadAllLines(file);
                    Equal(2, lines.Length, "two events");
                    True(lines[0].StartsWith("{\"time\":\"") && lines[0].Contains("\"event\":\"run.start\""), lines[0]);
                    True(lines[0].Contains("\"name\":\"Say \\\"hi\\\"\\\\now\"") && lines[0].Contains("\"repeat\":2") && lines[0].Contains("\"dry\":false"), lines[0]);
                    True(lines[1].EndsWith("\"elapsedMs\":1500}"), lines[1]);

                    var sink = new FakeInputSink();
                    using (var player = new MacroPlayer(sink))
                    {
                        Macro macro = Steps(1, new MacroStep { Kind = StepKind.Type, Text = "secret {{pin}}" });
                        player.StepStarted += (index, pass, step) => log.Write("step", "index", index + 1, "iteration", pass, "kind", step.Kind.ToString());
                        player.Start(macro, Ok, 0, values: new Dictionary<string, string> { ["pin"] = "1234" });
                        player.WaitForStop(10000);
                    }
                    string all = File.ReadAllText(file);
                    True(all.Contains("\"event\":\"step\"") && all.Contains("\"kind\":\"Type\""), "the step is logged");
                    True(!all.Contains("secret") && !all.Contains("1234"), "never what was typed");
                }
                finally { Directory.Delete(Path.GetDirectoryName(file), true); }
            });

            Test("developer mode: clicker runs go to the daily log", () =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "vclicker-dev-" + Guid.NewGuid().ToString("N"));
                string ini = Path.Combine(dir, "settings.ini");
                Directory.CreateDirectory(dir);
                try
                {
                    new ClickSettings { DeveloperMode = true, StartDelaySeconds = 0, Sounds = false, RepeatCount = 3, IntervalMs = 10 }.Save(ini);
                    True(ClickSettings.Load(ini).DeveloperMode, "saved");
                    using (var form = new MainForm(ini, new NullClickSink()) { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) })
                    {
                        IntPtr handle = form.Handle;
                        form.Toggle(false);
                        DateTime until = DateTime.UtcNow.AddSeconds(5);
                        while (form.ClickerBusy && DateTime.UtcNow < until) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                        Application.DoEvents();
                    }
                    string log = RunLog.DailyPath(ini);
                    True(File.Exists(log), "daily log in Logs: " + log);
                    string text = File.ReadAllText(log);
                    True(text.Contains("\"event\":\"run.start\"") && text.Contains("\"kind\":\"clicker\""), text);
                    True(text.Contains("\"event\":\"run.stop\"") && text.Contains("\"reason\":\"Completed\"") && text.Contains("\"clicks\":3"), text);
                }
                finally { Directory.Delete(dir, true); }
            });

            Test("command line: an empty or invalid --log is refused up front; a bad log path never throws", () =>
            {
                foreach (string bad in new[] { "", "   ", "a|b.jsonl" })
                    True(CommandLine.Parse(new[] { "--macro", "x", "--log", bad }).Error != null, "rejected: '" + bad + "'");
                new RunLog("a|b.jsonl").Write("run.start"); // no exception
                new RunLog("").Write("run.start");
            });

            Test("command line: --log is for the command's run only, not the runs after it", () =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "vclicker-cli-" + Guid.NewGuid().ToString("N"));
                string ini = Path.Combine(dir, "settings.ini"), log = Path.Combine(dir, "cmd.jsonl");
                Directory.CreateDirectory(dir);
                try
                {
                    new ClickSettings { StartDelaySeconds = 0, Sounds = false, RepeatCount = 2, IntervalMs = 10 }.Save(ini);
                    using (var form = new MainForm(ini, new NullClickSink()) { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) })
                    {
                        IntPtr handle = form.Handle;
                        Equal(ExitCodes.BadArguments, form.RunCommand(CommandLine.Parse(new[] { "--macro", "Nope", "--log", log })), "failed");
                        form.Toggle(false); // a manual run afterwards
                        DateTime until = DateTime.UtcNow.AddSeconds(5);
                        while (form.ClickerBusy && DateTime.UtcNow < until) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                        Application.DoEvents();
                    }
                    string text = File.ReadAllText(log);
                    True(text.Contains("command.end") && !text.Contains("run.start"), text);
                }
                finally { Directory.Delete(dir, true); }
            });

            Test("copy command: a PowerShell line that waits and keeps the exit code, quotes escaped", () =>
            {
                string line = MacroStudio.CommandLineFor(@"C:\Apps\V-Clicker.exe", @"C:\Users\Ana O'Neil\Macros\Fim de mês.vcmacro", new[] { "mes" });
                Equal("$p = Start-Process -Wait -PassThru -FilePath 'C:\\Apps\\V-Clicker.exe' -ArgumentList '--macro','\"C:\\Users\\Ana O''Neil\\Macros\\Fim de mês.vcmacro\"','--var','mes=','--exit-when-done'; $p.ExitCode",
                      line, "command");
                // PowerShell also ends a single-quoted string at the typographic quotes: each is doubled too.
                string curly = MacroStudio.CommandLineFor(@"C:\V.exe", @"C:\M’; calc; ‘x.vcmacro", new string[0]);
                True(curly.Contains("M’’; calc; ‘‘x"), curly);
            });

            Test("command line: a hold-mode profile is refused (nothing holds the hotkey)", () =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "vclicker-cli-" + Guid.NewGuid().ToString("N"));
                string ini = Path.Combine(dir, "settings.ini");
                Directory.CreateDirectory(dir);
                try
                {
                    new ClickSettings { Sounds = false }.Save(ini);
                    var held = new AppProfile { ExeName = "Game.exe", Name = "Game", UsedUtc = DateTime.UtcNow };
                    held.Settings.HoldMode = true;
                    Profiles.Save(Profiles.Folder(ini), held);
                    using (var form = new MainForm(ini, new NullClickSink()) { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) })
                    {
                        IntPtr handle = form.Handle;
                        Equal(ExitCodes.BadArguments, form.RunCommand(CommandLine.Parse(new[] { "--profile", "Game" })), "refused");
                        True(!form.ClickerBusy, "nothing started");
                    }
                }
                finally { Directory.Delete(dir, true); }
            });

            Test("command line: a missing macro, profile or variable value is exit code 2", () =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "vclicker-cli-" + Guid.NewGuid().ToString("N"));
                string ini = Path.Combine(dir, "settings.ini");
                Directory.CreateDirectory(dir);
                try
                {
                    new ClickSettings { Sounds = false }.Save(ini);
                    Macro macro = Steps(1, new MacroStep { Kind = StepKind.Type, Text = "{{name}}" });
                    macro.ExeName = "app.exe";
                    macro.Name = "Hello";
                    MacroFile.Save(macro, Path.Combine(MacroFile.Folder(ini), "Hello.vcmacro"));
                    using (var form = new MainForm(ini, new NullClickSink()) { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) })
                    {
                        IntPtr handle = form.Handle;
                        Equal(ExitCodes.BadArguments, form.RunCommand(CommandLine.Parse(new[] { "--macro", "Nope" })), "no such macro");
                        Equal(ExitCodes.BadArguments, form.RunCommand(CommandLine.Parse(new[] { "--profile", "Nope" })), "no such profile");
                        Equal(ExitCodes.BadArguments, form.RunCommand(CommandLine.Parse(new[] { "--macro", "Hello" })), "{{name}} has no value");
                        True(!form.ClickerBusy, "nothing started");
                    }
                }
                finally { Directory.Delete(dir, true); }
            });
        }
    }
}
