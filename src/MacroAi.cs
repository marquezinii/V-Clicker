using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace VClicker
{
    /// <summary>A macro the AI drafted: unsaved, never run until the user reviews it.</summary>
    internal sealed class AiProposal
    {
        public Macro Macro;
        /// <summary>The AI's note, what each click should point at, and what was dropped.</summary>
        public string Note = string.Empty;
        /// <summary>Proposed steps that were not valid and were left out.</summary>
        public int Skipped;
    }

    /// <summary>Why no proposal came back, in words for the user.</summary>
    internal sealed class AiException : Exception
    {
        public AiException(string message) : base(message) { }
    }

    /// <summary>
    /// The AI Macro Builder (docs/superpowers/specs/2026-10-21-ai-macro-builder-design.md): one Messages API call that
    /// turns a description into a proposal. Only the description and the app's name are sent. The reply is a JSON object
    /// constrained by a schema, and every step is validated again here, like a .vcmacro line, before it becomes a step.
    /// Plain HTTPS (no SDK): V-Clicker is one .exe with no third-party DLLs.
    /// </summary>
    internal static class MacroAi
    {
        public const string Model = "claude-opus-5";
        public const string Endpoint = "https://api.anthropic.com/v1/messages";
        public const int MaxSteps = 200;
        private const int MaxText = 2000;
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

        private const string Instructions =
            "You turn a person's description of a repetitive task in one Windows app into a V-Clicker macro proposal. " +
            "The person reviews and edits it before anything runs; you never run anything and you cannot see the screen.\n" +
            "Actions: click or double_click (button: left, right or middle; put in target a short description of what the " +
            "person should point at, because they will capture each click's point themselves); type (text); key (one key: " +
            "Enter, Tab, Esc, Backspace, Delete, F1-F24, Up, Down, Left, Right, Home, End, PageUp, PageDown, a letter or a " +
            "digit, optionally with Shift+); shortcut (a combination with Ctrl+ or Alt+, such as Ctrl+S); wait (ms, the pause " +
            "in milliseconds). Fields that an action does not use are empty strings or 0.\n" +
            "repeat is how many times the whole list runs; 0 means until the person stops it. The macro stops by itself when " +
            "the app's window closes and pauses while another window is in front, so \"only while this window is open\" needs " +
            "no extra step.\n" +
            "Rules: propose only what was asked. Never press F6, F7 or F8 (V-Clicker's own hotkeys). If part of the task " +
            "needs something these actions cannot do (reading the screen, deciding, other apps), leave it out and say so in " +
            "note. Write note and target in the language of the description, briefly.";

        private static Dictionary<string, object> Prop(string type, string description = null, object[] values = null)
        {
            var schema = new Dictionary<string, object> { ["type"] = type };
            if (description != null) schema["description"] = description;
            if (values != null) schema["enum"] = values;
            return schema;
        }

        private static Dictionary<string, object> Schema() => new Dictionary<string, object>
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new object[] { "steps", "repeat", "note" },
            ["properties"] = new Dictionary<string, object>
            {
                ["repeat"] = Prop("integer", "Repetitions of the whole list; 0 = until stopped"),
                ["note"] = Prop("string", "Short note for the person: what to check, what was left out"),
                ["steps"] = new Dictionary<string, object>
                {
                    ["type"] = "array",
                    ["items"] = new Dictionary<string, object>
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = new object[] { "action", "button", "text", "key", "ms", "target" },
                        ["properties"] = new Dictionary<string, object>
                        {
                            ["action"] = Prop("string", null, new object[] { "click", "double_click", "type", "key", "shortcut", "wait" }),
                            ["button"] = Prop("string", null, new object[] { "left", "right", "middle" }),
                            ["text"] = Prop("string"),
                            ["key"] = Prop("string"),
                            ["ms"] = Prop("integer"),
                            ["target"] = Prop("string")
                        }
                    }
                }
            }
        };

        /// <summary>The request body: the description and the app's name, nothing else from this computer.</summary>
        internal static string RequestBody(string description, string appName) =>
            new JavaScriptSerializer().Serialize(new Dictionary<string, object>
            {
                ["model"] = Model,
                ["max_tokens"] = 8000,
                ["fallbacks"] = "default",
                ["system"] = Instructions,
                ["output_config"] = new Dictionary<string, object>
                {
                    ["effort"] = "low",
                    ["format"] = new Dictionary<string, object> { ["type"] = "json_schema", ["schema"] = Schema() }
                },
                ["messages"] = new object[]
                {
                    new Dictionary<string, object> { ["role"] = "user", ["content"] = "App: " + appName + "\nTask: " + description }
                }
            });

        /// <param name="send">Sends the request (tests); null = HTTPS to the API.</param>
        /// <exception cref="AiException">No proposal: the reason, in words for the user.</exception>
        public static async Task<AiProposal> Generate(string apiKey, string description, string appName, Macro bound, CancellationToken cancel,
                                                      Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send = null)
        {
            // An API key is printable ASCII; anything else could not even be sent as a header.
            if (string.IsNullOrEmpty(apiKey) || apiKey.Any(ch => ch < '!' || ch > '~')) throw new AiException(Loc.T("ai.err.key"));
            using (var request = new HttpRequestMessage(HttpMethod.Post, Endpoint))
            {
                request.Headers.Add("x-api-key", apiKey);
                request.Headers.Add("anthropic-version", "2023-06-01");
                request.Headers.Add("anthropic-beta", "server-side-fallback-2026-07-01");
                request.Content = new StringContent(RequestBody(description, appName), Encoding.UTF8, "application/json");
                HttpClient client = null;
                try
                {
                    if (send == null)
                    {
                        client = new HttpClient { Timeout = Timeout };
                        send = client.SendAsync;
                    }
                    using (HttpResponseMessage response = await send(request, cancel).ConfigureAwait(false))
                    {
                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (response.StatusCode != HttpStatusCode.OK) throw new AiException(ErrorText((int)response.StatusCode, body));
                        return Parse(body, bound);
                    }
                }
                catch (HttpRequestException) { throw new AiException(Loc.T("ai.err.network")); }
                catch (TaskCanceledException) when (!cancel.IsCancellationRequested) { throw new AiException(Loc.T("ai.err.network")); } // timed out
                finally { client?.Dispose(); }
            }
        }

        internal static string ErrorText(int status, string body)
        {
            switch (status)
            {
                case 401: case 403: return Loc.T("ai.err.key");
                case 429: return Loc.T("ai.err.limit");
                case 500: case 502: case 503: case 504: case 529: return Loc.T("ai.err.busy");
            }
            string detail = status.ToString(System.Globalization.CultureInfo.InvariantCulture);
            try
            {
                if (new JavaScriptSerializer().DeserializeObject(body) is Dictionary<string, object> root &&
                    root.TryGetValue("error", out object error) && error is Dictionary<string, object> e &&
                    e.TryGetValue("message", out object message) && message is string text && text.Length > 0)
                    detail = text.Length > 300 ? text.Substring(0, 300) + "…" : text;
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return Loc.F("ai.err.other", detail);
        }

        /// <summary>A Messages API reply → a proposal for the app of <paramref name="bound"/> (its exe, title and size).</summary>
        /// <exception cref="AiException">A refusal, a cut-off or malformed reply, or no usable step.</exception>
        internal static AiProposal Parse(string reply, Macro bound)
        {
            var json = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
            Dictionary<string, object> proposal;
            try
            {
                if (!(json.DeserializeObject(reply) is Dictionary<string, object> root)) throw new AiException(Loc.T("ai.err.reply"));
                string stop = root.TryGetValue("stop_reason", out object s) ? s as string : null;
                if (stop == "refusal") throw new AiException(Loc.T("ai.err.refused"));
                if (stop == "max_tokens") throw new AiException(Loc.T("ai.err.reply"));
                string text = (root.TryGetValue("content", out object c) ? c as object[] : null)?
                    .OfType<Dictionary<string, object>>().Where(b => b.TryGetValue("type", out object t) && (t as string) == "text")
                    .Select(b => b.TryGetValue("text", out object x) ? x as string : null).FirstOrDefault(x => x != null);
                proposal = text == null ? null : json.DeserializeObject(text) as Dictionary<string, object>;
            }
            catch (ArgumentException) { proposal = null; }
            catch (InvalidOperationException) { proposal = null; }
            if (proposal == null || !(Get(proposal, "steps") is object[] items)) throw new AiException(Loc.T("ai.err.reply"));

            var macro = new Macro { ExeName = bound.ExeName, Title = bound.Title, ClientSize = bound.ClientSize, Repeat = Clamp(Int(proposal, "repeat", 1), 0, 1000000) };
            var result = new AiProposal { Macro = macro };
            var points = new List<string>();
            foreach (object item in items)
            {
                MacroStep step = item is Dictionary<string, object> fields && macro.Steps.Count < MaxSteps ? Step(fields) : null;
                if (step == null) { result.Skipped++; continue; }
                macro.Steps.Add(step);
                string target = Get((Dictionary<string, object>)item, "target") as string;
                if (step.NeedsPoint && !string.IsNullOrWhiteSpace(target)) points.Add(macro.Steps.Count + ": " + OneLine(target, 120));
            }
            if (macro.Steps.Count == 0) throw new AiException(Loc.T("ai.err.empty"));
            var note = new StringBuilder(OneLine(Get(proposal, "note") as string ?? string.Empty, 600));
            if (points.Count > 0) note.Append(note.Length > 0 ? " " : string.Empty).Append(Loc.T("ai.points")).Append(' ').Append(string.Join(" · ", points));
            if (result.Skipped > 0) note.Append(note.Length > 0 ? " " : string.Empty).Append(Loc.F("ai.skipped", result.Skipped));
            result.Note = note.ToString();
            return result;
        }

        /// <summary>One proposed step, validated like a .vcmacro line; null = not valid.</summary>
        private static MacroStep Step(Dictionary<string, object> fields)
        {
            string action = Get(fields, "action") as string;
            string key = Get(fields, "key") as string ?? string.Empty;
            switch (action)
            {
                case "click":
                case "double_click":
                    ClickButton button;
                    switch (Get(fields, "button") as string)
                    {
                        case "right": button = ClickButton.Right; break;
                        case "middle": button = ClickButton.Middle; break;
                        default: button = ClickButton.Left; break;
                    }
                    return new MacroStep { Kind = action == "click" ? StepKind.Click : StepKind.DoubleClick, Button = button, NeedsPoint = true };
                case "type":
                    string text = (Get(fields, "text") as string ?? string.Empty).Replace("\r", string.Empty);
                    return text.Length == 0 || text.Length > MaxText ? null : new MacroStep { Kind = StepKind.Type, Text = text };
                case "key":
                    return MacroKeys.TryParse(key, out Keys pressed) && (pressed & (Keys.Control | Keys.Alt)) == 0
                        ? new MacroStep { Kind = StepKind.Key, Key = pressed } : null;
                case "shortcut":
                    return MacroKeys.TryParse(key, out Keys chord) && (chord & (Keys.Control | Keys.Alt)) != 0
                        ? new MacroStep { Kind = StepKind.Shortcut, Key = chord } : null;
                case "wait":
                    return new MacroStep { Kind = StepKind.Wait, DurationMs = Clamp(Int(fields, "ms", 0), 0, MacroStep.MaxWaitMs) };
                default:
                    return null;
            }
        }

        private static object Get(Dictionary<string, object> fields, string name) => fields.TryGetValue(name, out object value) ? value : null;

        private static int Int(Dictionary<string, object> fields, string name, int fallback)
        {
            switch (Get(fields, name))
            {
                case int i: return i;
                case long l: return (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, l));
                case decimal d: return (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, Math.Round(d)));
                default: return fallback;
            }
        }

        private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

        private static string OneLine(string text, int max)
        {
            string line = new string(text.Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray()).Trim();
            return line.Length > max ? line.Substring(0, max) + "…" : line;
        }
    }

    /// <summary>The API key at rest: DPAPI for the current Windows user, so the settings file alone does not reveal it.</summary>
    internal static class ApiKeys
    {
        private const string Prefix = "dpapi:";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("V-Clicker AI Macro Builder");

        public static bool IsProtected(string stored) => stored != null && stored.StartsWith(Prefix, StringComparison.Ordinal) && stored.Length > Prefix.Length;

        public static string Protect(string key) =>
            Prefix + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), Entropy, DataProtectionScope.CurrentUser));

        /// <returns>null if there is no key, or it is not readable by this user on this computer.</returns>
        public static string Unprotect(string stored)
        {
            if (!IsProtected(stored)) return null;
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored.Substring(Prefix.Length)), Entropy, DataProtectionScope.CurrentUser)); }
            catch (FormatException) { return null; }
            catch (CryptographicException) { return null; }
        }
    }
}
