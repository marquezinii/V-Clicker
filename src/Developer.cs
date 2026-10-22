using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace VClicker
{
    /// <summary>
    /// What the command line asks for (docs/superpowers/specs/2026-10-21-developer-mode-design.md). It never adds a
    /// hidden mode: a command does in the normal window what the Start or Run button would.
    /// </summary>
    internal sealed class CommandLine
    {
        public string Macro, Profile, LogPath;
        public readonly Dictionary<string, string> Vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public bool ExitWhenDone, Help;
        /// <summary>Why the arguments were rejected; null = fine.</summary>
        public string Error;

        public bool HasAction => Macro != null || Profile != null;

        public const string Usage =
            "V-Clicker command line\n" +
            "  V-Clicker.exe --macro <file.vcmacro | name> [--var name=value]... [--log file.jsonl] [--exit-when-done]\n" +
            "  V-Clicker.exe --profile <app.exe | name> [--log file.jsonl] [--exit-when-done]\n" +
            "  V-Clicker.exe --help\n" +
            "Exit codes (with --exit-when-done): 0 done, 1 stopped by the user, 2 bad arguments or not found,\n" +
            "3 V-Clicker already open, 4 did not start, 10+ other stop (see the log). Details: docs/developer.md";

        public static CommandLine Parse(string[] args)
        {
            var cmd = new CommandLine();
            for (int i = 0; i < args.Length && cmd.Error == null; i++)
            {
                string arg = args[i];
                string Value()
                {
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) return args[++i];
                    cmd.Error = arg + " needs a value.";
                    return null;
                }
                switch (arg.ToLowerInvariant())
                {
                    case "--help": case "-h": case "/?": cmd.Help = true; break;
                    case "--wait-previous": break; // used by "restart as administrator"
                    case "--macro": cmd.Macro = Value(); break;
                    case "--profile": cmd.Profile = Value(); break;
                    case "--log": cmd.LogPath = Value(); break;
                    case "--exit-when-done": cmd.ExitWhenDone = true; break;
                    case "--var":
                        string pair = Value();
                        if (pair == null) break;
                        int eq = pair.IndexOf('=');
                        string name = eq > 0 ? pair.Substring(0, eq) : null;
                        if (!MacroVariables.IsName(name)) cmd.Error = "--var takes name=value (a name of letters, digits, _ or -): " + pair;
                        else cmd.Vars[name] = pair.Substring(eq + 1);
                        break;
                    default: cmd.Error = "Unknown argument: " + arg; break;
                }
            }
            if (cmd.Error != null || cmd.Help) return cmd;
            if (cmd.Macro != null && cmd.Profile != null) cmd.Error = "Use --macro or --profile, not both.";
            else if (!cmd.HasAction && (cmd.LogPath != null || cmd.ExitWhenDone || cmd.Vars.Count > 0)) cmd.Error = "--var, --log and --exit-when-done need --macro or --profile.";
            else if (cmd.Profile != null && cmd.Vars.Count > 0) cmd.Error = "--var is for macros.";
            return cmd;
        }
    }

    /// <summary>The process exit code of a command run with --exit-when-done.</summary>
    internal static class ExitCodes
    {
        public const int Done = 0, Stopped = 1, BadArguments = 2, AlreadyRunning = 3, NotStarted = 4, OtherStop = 10;

        public static int For(MacroStopReason reason) =>
            reason == MacroStopReason.Completed ? Done : reason == MacroStopReason.Manual ? Stopped : OtherStop + (int)reason;

        public static int For(StopReason reason) =>
            reason == StopReason.Completed ? Done : reason == StopReason.Manual ? Stopped : OtherStop + (int)reason;
    }

    /// <summary>
    /// {{name}} in a macro's typed text. Values come from --var, then from "var.name=value" header lines of the file;
    /// {{iteration}}, {{date}} and {{time}} are filled in when the step types.
    /// </summary>
    internal static class MacroVariables
    {
        private static readonly Regex Reference = new Regex(@"\{\{([A-Za-z0-9_-]{1,40})\}\}");
        private static readonly Regex Name = new Regex("^[A-Za-z0-9_-]{1,40}$");
        private static readonly string[] BuiltIn = { "iteration", "date", "time" };
        private const string HeaderPrefix = "var.";

        public static bool IsName(string text) => text != null && Name.IsMatch(text);

        /// <summary>The variables a macro types, built-ins left out, sorted, once each.</summary>
        public static List<string> Used(Macro macro) =>
            macro.Steps.Where(s => s.Kind == StepKind.Type)
                 .SelectMany(s => Reference.Matches(s.Text).Cast<Match>().Select(m => m.Groups[1].Value))
                 .Where(n => !BuiltIn.Contains(n, StringComparer.OrdinalIgnoreCase))
                 .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>The values the file gives ("var.name=value" header lines).</summary>
        public static Dictionary<string, string> Defaults(Macro macro)
        {
            var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in macro.ExtraHeader)
            {
                int eq = line.IndexOf('=');
                if (eq <= HeaderPrefix.Length || !line.StartsWith(HeaderPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                string name = line.Substring(HeaderPrefix.Length, eq - HeaderPrefix.Length).Trim();
                if (IsName(name)) found[name] = line.Substring(eq + 1);
            }
            return found;
        }

        /// <summary>The file's values with <paramref name="given"/> on top.</summary>
        public static Dictionary<string, string> Resolve(Macro macro, IDictionary<string, string> given)
        {
            Dictionary<string, string> values = Defaults(macro);
            foreach (KeyValuePair<string, string> pair in given) values[pair.Key] = pair.Value;
            return values;
        }

        /// <summary>Variables the macro types that have no value (neither given nor in the file).</summary>
        public static List<string> Missing(Macro macro, IDictionary<string, string> given)
        {
            Dictionary<string, string> values = Resolve(macro, given);
            return Used(macro).Where(n => !values.ContainsKey(n)).ToList();
        }

        public static string Expand(string text, IDictionary<string, string> values, int iteration)
        {
            if (text.IndexOf("{{", StringComparison.Ordinal) < 0) return text;
            return Reference.Replace(text, m =>
            {
                string name = m.Groups[1].Value;
                switch (name.ToLowerInvariant())
                {
                    case "iteration": return iteration.ToString(CultureInfo.InvariantCulture);
                    case "date": return DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    case "time": return DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                }
                return values != null && values.TryGetValue(name, out string value) ? value : m.Value;
            });
        }
    }

    /// <summary>
    /// A structured log: one JSON object per line (JSON Lines, UTF-8), appended. Writing never throws: a full disk
    /// must not end a run. Callers never put typed text or variable values in it.
    /// </summary>
    internal sealed class RunLog
    {
        private static readonly object Gate = new object();
        private readonly string path;

        public RunLog(string path)
        {
            this.path = Path.GetFullPath(path);
        }

        /// <summary>Developer Mode's daily file: Logs\v-clicker-YYYY-MM-DD.jsonl next to the settings.</summary>
        public static string DailyPath(string settingsPath) =>
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath)) ?? ".", "Logs",
                         "v-clicker-" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl");

        /// <param name="fields">Name, value, name, value…: strings, numbers, booleans or null.</param>
        public void Write(string name, params object[] fields)
        {
            var line = new StringBuilder("{\"time\":");
            Value(line, DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture));
            line.Append(",\"event\":");
            Value(line, name);
            for (int i = 0; i + 1 < fields.Length; i += 2)
            {
                line.Append(',');
                Value(line, Convert.ToString(fields[i], CultureInfo.InvariantCulture));
                line.Append(':');
                Value(line, fields[i + 1]);
            }
            line.Append("}\n");
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.AppendAllText(path, line.ToString(), new UTF8Encoding(false));
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { }
            }
        }

        private static void Value(StringBuilder json, object value)
        {
            switch (value)
            {
                case null: json.Append("null"); return;
                case bool flag: json.Append(flag ? "true" : "false"); return;
                case int _: case long _: case double _: case float _:
                    json.Append(Convert.ToString(value, CultureInfo.InvariantCulture)); return;
            }
            json.Append('"');
            foreach (char c in value.ToString())
            {
                switch (c)
                {
                    case '"': json.Append("\\\""); break;
                    case '\\': json.Append("\\\\"); break;
                    case '\n': json.Append("\\n"); break;
                    case '\r': json.Append("\\r"); break;
                    case '\t': json.Append("\\t"); break;
                    default:
                        if (c < ' ') json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else json.Append(c);
                        break;
                }
            }
            json.Append('"');
        }
    }
}
