using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using static VClicker.Tests.Program;

namespace VClicker.Tests
{
    /// <summary>AI Macro Builder (docs/superpowers/specs/2026-10-21-ai-macro-builder-design.md).</summary>
    internal static partial class MacroTests
    {
        /// <summary>A Messages API reply whose text block is <paramref name="proposal"/> (as JSON).</summary>
        private static string Reply(object proposal, string stop = "end_turn")
        {
            var json = new JavaScriptSerializer();
            return json.Serialize(new Dictionary<string, object>
            {
                ["id"] = "msg_1", ["type"] = "message", ["role"] = "assistant", ["model"] = MacroAi.Model, ["stop_reason"] = stop,
                ["content"] = new object[] { new Dictionary<string, object> { ["type"] = "text", ["text"] = json.Serialize(proposal) } }
            });
        }

        private static Dictionary<string, object> AiStep(string action, string button = "left", string text = "", string key = "", int ms = 0, string target = "") =>
            new Dictionary<string, object> { ["action"] = action, ["button"] = button, ["text"] = text, ["key"] = key, ["ms"] = ms, ["target"] = target };

        private static Macro Bound() => new Macro { ExeName = "Game.exe", Title = "Game", ClientSize = new Size(800, 600) };

        private static void AiTests()
        {
            Test("pending point: a proposed click without a place round-trips in the file and says so", () =>
            {
                Loc.SetLanguage("en");
                Macro macro = Steps(1, new MacroStep { Kind = StepKind.Click, Button = ClickButton.Right, NeedsPoint = true });
                macro.ExeName = "Game.exe";
                string text = MacroFile.Write(macro);
                True(text.Contains("click right pending\n"), text);
                MacroStep read = MacroFile.Read(text).Steps[0];
                True(read.Kind == StepKind.Click && read.NeedsPoint && read.Button == ClickButton.Right, "read back");
                True(read.Describe().Contains(Loc.T("step.pendingPoint")), read.Describe());
                Equal(StepKind.Unknown, MacroFile.Read(text.Replace("click right pending", "drag right pending")).Steps[0].Kind, "only clicks can wait for a point");
                Loc.SetLanguage("auto");
            });

            Test("pending point: never clicked; Run refuses it and the player stops before it", () =>
            {
                string ini = Path.Combine(Path.GetTempPath(), "vclicker-studio-" + Guid.NewGuid().ToString("N"), "settings.ini");
                using (var studio = new MacroStudio(null, new ClickSettings(), ini))
                {
                    Macro macro = Steps(1, new MacroStep { Kind = StepKind.Wait, DurationMs = 10 }, new MacroStep { Kind = StepKind.Click, NeedsPoint = true });
                    macro.ExeName = "Game.exe";
                    studio.Open(macro, null);
                    studio.BeginRun();
                    True(!studio.Busy, "refused");
                    Equal(1, studio.Selected, "the click without a point is selected");
                }
                var sink = new FakeInputSink();
                MacroStopReason reason = MacroStopReason.Completed;
                using (var player = new MacroPlayer(sink))
                {
                    player.Stopped += r => reason = r;
                    player.Start(Steps(1, new MacroStep { Kind = StepKind.Click, NeedsPoint = true }), Ok, 0);
                    True(player.WaitForStop(5000), "ended");
                }
                Equal(MacroStopReason.Unsupported, reason, "stopped");
                Equal(0, sink.Snapshot().Length, "nothing was sent");
            });

            Test("pending point: the simulation shows it without a place", () =>
            {
                Macro macro = Steps(1, new MacroStep { Kind = StepKind.Click, NeedsPoint = true });
                using (MacroPreviewResult preview = MacroPreview.Evaluate(macro, IntPtr.Zero, 0, w => null, CancellationToken.None))
                    Equal(PreviewState.NoPosition, preview.Steps[0].State, "no place yet");
            });

            Test("AI request: only the description and the app's name, the model, the schema; never the key", () =>
            {
                string body = MacroAi.RequestBody("Clique neste botão a cada 2 segundos", "Roblox");
                var request = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(body);
                Equal(MacroAi.Model, (string)request["model"], "model");
                Equal("default", (string)request["fallbacks"], "server-side fallbacks on refusals");
                var config = (Dictionary<string, object>)request["output_config"];
                Equal("json_schema", (string)((Dictionary<string, object>)config["format"])["type"], "structured output");
                string user = (string)((Dictionary<string, object>)((object[])request["messages"])[0])["content"];
                True(user.Contains("Clique neste botão a cada 2 segundos") && user.Contains("Roblox"), user);
                True(!body.Contains("x-api-key") && !body.Contains("sk-ant"), "the key never goes in the body");
            });

            Test("AI reply: steps become an unsaved macro of the app; clicks wait for a point; invalid steps are dropped", () =>
            {
                string reply = Reply(new Dictionary<string, object>
                {
                    ["repeat"] = 0, ["note"] = "Para quando a janela fechar.",
                    ["steps"] = new object[]
                    {
                        AiStep("click", "left", target: "o botão Coletar"), AiStep("wait", ms: 2000), AiStep("key", key: "F5"),
                        AiStep("shortcut", key: "Ctrl+S"), AiStep("type", text: "oi"), AiStep("double_click", "right", target: "o ícone"),
                        AiStep("hover"), AiStep("key", key: "NotAKey"), AiStep("type", text: ""), AiStep("shortcut", key: "S"), AiStep("wait", ms: -5)
                    }
                });
                AiProposal proposal = MacroAi.Parse(reply, Bound());
                Macro macro = proposal.Macro;
                Equal("Click|Wait|Key|Shortcut|Type|DoubleClick|Wait", string.Join("|", macro.Steps.Select(s => s.Kind)), "steps");
                True(macro.Steps[0].NeedsPoint && macro.Steps[5].NeedsPoint && macro.Steps[5].Button == ClickButton.Right, "clicks need a point");
                Equal(2000, macro.Steps[1].DurationMs, "wait");
                Equal(0, macro.Steps[6].DurationMs, "a negative wait is clamped");
                Equal(Keys.S | Keys.Control, macro.Steps[3].Key, "shortcut");
                Equal(4, proposal.Skipped, "hover, a bad key name, empty text, a shortcut without Ctrl/Alt");
                Equal(0, macro.Repeat, "until stopped");
                True(macro.ExeName == "Game.exe" && macro.ClientSize == new Size(800, 600), "bound to the chosen app");
                True(proposal.Note.Contains("Para quando a janela fechar.") && proposal.Note.Contains("o botão Coletar") && proposal.Note.Contains("o ícone"), proposal.Note);
            });

            Test("AI reply: a refusal, a cut-off reply or garbage is explained, never turned into steps", () =>
            {
                foreach (string bad in new[]
                {
                    Reply(new Dictionary<string, object>(), "refusal"), Reply(new Dictionary<string, object>(), "max_tokens"),
                    "not json", "{\"content\":[]}", Reply("just a string")
                })
                {
                    bool threw = false;
                    try { MacroAi.Parse(bad, Bound()); }
                    catch (AiException error) { threw = error.Message.Length > 0; }
                    True(threw, "rejected: " + bad.Substring(0, Math.Min(60, bad.Length)));
                }
                string overLimit = Reply(new Dictionary<string, object>
                {
                    ["repeat"] = 1, ["note"] = "", ["steps"] = Enumerable.Range(0, MacroAi.MaxSteps + 50).Select(i => (object)AiStep("wait", ms: 10)).ToArray()
                });
                Equal(MacroAi.MaxSteps, MacroAi.Parse(overLimit, Bound()).Macro.Steps.Count, "capped");
            });

            Test("AI call: key in the header, errors in plain words", () =>
            {
                HttpRequestMessage sent = null;
                string sentBody = null;
                Task<HttpResponseMessage> Answer(HttpStatusCode code, string body) =>
                    Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
                string ok = Reply(new Dictionary<string, object> { ["repeat"] = 1, ["note"] = "", ["steps"] = new object[] { AiStep("wait", ms: 100) } });
                AiProposal proposal = MacroAi.Generate("sk-ant-test", "espere", "Game", Bound(), CancellationToken.None, (request, cancel) =>
                {
                    sent = request;
                    sentBody = request.Content.ReadAsStringAsync().Result;
                    return Answer(HttpStatusCode.OK, ok);
                }).Result;
                Equal(1, proposal.Macro.Steps.Count, "proposal");
                Equal("sk-ant-test", sent.Headers.GetValues("x-api-key").Single(), "key header");
                Equal("2023-06-01", sent.Headers.GetValues("anthropic-version").Single(), "version");
                True(sent.Headers.GetValues("anthropic-beta").Single().Contains("server-side-fallback"), "fallback beta");
                Equal(MacroAi.Endpoint, sent.RequestUri.ToString(), "endpoint");
                True(!sentBody.Contains("sk-ant-test"), "the key is not in the body");

                foreach (var failure in new[]
                {
                    Tuple.Create(HttpStatusCode.Unauthorized, "ai.err.key"), Tuple.Create((HttpStatusCode)429, "ai.err.limit"),
                    Tuple.Create((HttpStatusCode)529, "ai.err.busy"), Tuple.Create(HttpStatusCode.BadRequest, "ai.err.other")
                })
                {
                    string message = null;
                    try
                    {
                        MacroAi.Generate("k", "x", "Game", Bound(), CancellationToken.None,
                            (r, c) => Answer(failure.Item1, "{\"type\":\"error\",\"error\":{\"type\":\"x\",\"message\":\"details here\"}}")).Wait();
                    }
                    catch (AggregateException error) when (error.InnerException is AiException ai) { message = ai.Message; }
                    True(message != null && message.StartsWith(Loc.T(failure.Item2).Split('{')[0]), failure.Item1 + ": " + message);
                }
                string offline = null;
                try
                {
                    MacroAi.Generate("k", "x", "Game", Bound(), CancellationToken.None, (r, c) => throw new HttpRequestException("no network")).Wait();
                }
                catch (AggregateException error) when (error.InnerException is AiException ai) { offline = ai.Message; }
                Equal(Loc.T("ai.err.network"), offline, "no connection");
            });

            Test("AI key: kept for this Windows user only, never in clear text", () =>
            {
                string stored = ApiKeys.Protect("sk-ant-secret");
                True(stored.StartsWith("dpapi:") && !stored.Contains("sk-ant-secret"), stored);
                Equal("sk-ant-secret", ApiKeys.Unprotect(stored), "read back");
                True(ApiKeys.Unprotect("dpapi:AAAA") == null && ApiKeys.Unprotect("sk-ant-x") == null && ApiKeys.Unprotect("") == null, "garbage");
                string ini = Path.Combine(Path.GetTempPath(), "vclicker-ai-" + Guid.NewGuid().ToString("N") + ".ini");
                try
                {
                    new ClickSettings { AiKey = stored }.Save(ini);
                    Equal(stored, ClickSettings.Load(ini).AiKey, "saved protected");
                    True(!File.ReadAllText(ini).Contains("sk-ant-secret"), "never in clear in the file");
                    File.WriteAllLines(ini, new[] { "AiKey=sk-ant-typed-by-hand" });
                    Equal(string.Empty, ClickSettings.Load(ini).AiKey, "a clear-text key in the file is not used");
                }
                finally { File.Delete(ini); }
            });

            Test("AI window: asks for a description, shows why there is no proposal, returns one", () =>
            {
                var proposal = new AiProposal { Macro = Steps(1, new MacroStep { Kind = StepKind.Wait, DurationMs = 10 }) };
                string failure = null;
                using (var dialog = new AiDialog(new ClickSettings(), null, "Game", Bound())
                {
                    Generator = (text, app, cancel) => failure == null ? Task.FromResult(proposal) : throw new AiException(failure)
                })
                {
                    IntPtr handle = dialog.Handle;
                    dialog.GenerateAsync().Wait();
                    Equal(Loc.T("ai.needText"), dialog.StatusText, "nothing to send");
                    True(dialog.Result == null, "no proposal");
                    dialog.Description = "clique a cada 2 s";
                    failure = "A API recusou a chave.";
                    dialog.GenerateAsync().Wait();
                    Equal(failure, dialog.StatusText, "the reason is shown");
                    True(dialog.Result == null, "still no proposal");
                    failure = null;
                    dialog.GenerateAsync().Wait();
                    True(dialog.Result == proposal && dialog.DialogResult == DialogResult.OK, "the proposal");
                }
            });

            Test("studio: a proposal opens unsaved, with the AI note, and nothing runs", () =>
            {
                Loc.SetLanguage("en");
                string ini = Path.Combine(Path.GetTempPath(), "vclicker-studio-" + Guid.NewGuid().ToString("N"), "settings.ini");
                using (var studio = new MacroStudio(null, new ClickSettings(), ini))
                {
                    var proposal = new AiProposal { Macro = Steps(0, new MacroStep { Kind = StepKind.Click, NeedsPoint = true }), Note = "Stops when the window closes." };
                    proposal.Macro.ExeName = "Game.exe";
                    studio.OpenProposal(proposal);
                    True(studio.Current.Steps.Count == 1 && studio.Current.Steps[0].NeedsPoint, "the proposal");
                    True(!studio.Busy, "not running");
                    True(studio.Dirty, "unsaved: closing asks");
                    IEnumerable<Control> AllOf(Control root) => root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(AllOf(c)));
                    True(AllOf(studio).OfType<Label>().Any(l => l.Text.Contains("Stops when the window closes.")), "the note is shown");
                    True(AllOf(studio).OfType<Label>().Any(l => l.Text == Loc.T("banner.ai")), "the review warning is shown");
                }
                Loc.SetLanguage("auto");
            });
        }
    }
}
