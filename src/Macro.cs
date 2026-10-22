using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace VClicker
{
    internal enum StepKind { Click, DoubleClick, Drag, Hover, Scroll, Type, Key, Shortcut, Wait, Password, Unknown, WaitFor }

    /// <summary>What a "wait until" step watches (see docs/superpowers/specs/2026-10-22-conditional-click-design.md).</summary>
    internal enum ConditionKind { Element, Color, Window }

    internal enum TargetKind { Element, Image }

    /// <summary>
    /// What a click step clicks, found anew before every click instead of a fixed point
    /// (see docs/superpowers/specs/2026-10-23-smart-target-design.md).
    /// </summary>
    internal sealed class SmartTarget
    {
        public const int DefaultTimeoutMs = 10 * 1000;

        public TargetKind Kind;
        /// <summary>Element: the UI Automation control type ("Button"), name, and automation id (only when needed to tell it apart).</summary>
        public string ControlType = string.Empty, Name = string.Empty, AutomationId = string.Empty;
        /// <summary>Image: a square PNG of one of <see cref="ImageTarget.Sides"/>. Never changed in place.</summary>
        public byte[] Png;
        /// <summary>How long to look for it before the macro stops.</summary>
        public int TimeoutMs = DefaultTimeoutMs;

        public SmartTarget Clone() => (SmartTarget)MemberwiseClone();

        /// <summary>The image as a new 32-bit bitmap (the caller disposes it); null if it is not a valid target image.</summary>
        public Bitmap Decode() => DecodeImage(Png);

        internal static Bitmap DecodeImage(byte[] png)
        {
            if (png == null || png.Length == 0) return null;
            try
            {
                using (var stream = new MemoryStream(png))
                using (var image = new Bitmap(stream))
                {
                    if (image.Width != image.Height || Array.IndexOf(ImageTarget.Sides, image.Width) < 0) return null;
                    return image.Clone(new Rectangle(0, 0, image.Width, image.Height), PixelFormat.Format32bppArgb);
                }
            }
            catch (Exception error) when (error is ArgumentException || error is System.Runtime.InteropServices.ExternalException || error is OutOfMemoryException)
            {
                return null; // not an image, or a damaged one (GDI+ reports some of those as out of memory)
            }
        }

        internal static byte[] EncodeImage(Bitmap image)
        {
            using (var stream = new MemoryStream())
            {
                image.Save(stream, ImageFormat.Png);
                return stream.ToArray();
            }
        }
    }

    /// <summary>
    /// A window of the macro's app other than the bound one (a dialog like "Save as", a menu), by class and title.
    /// A step without one targets the bound window itself.
    /// </summary>
    internal sealed class WindowRef
    {
        /// <summary>Menus are top-level windows of this class and have no title.</summary>
        public const string MenuClass = "#32768";

        public string Class = string.Empty;
        public string Title = string.Empty;

        public bool IsMenu => Class == MenuClass;

        public WindowRef Clone() => new WindowRef { Class = Class, Title = Title };

        public bool Matches(WindowRef other) =>
            other != null && Class == other.Class && (IsMenu || Title == other.Title);
    }

    /// <summary>One line of a macro. Which fields matter depends on <see cref="Kind"/>.</summary>
    internal sealed class MacroStep
    {
        public const int MaxWaitMs = 24 * 60 * 60 * 1000;
        public const int MaxCoordinate = 100000;
        public const int MaxCount = 999;

        public StepKind Kind;
        public ClickButton Button = ClickButton.Left;
        /// <summary>Client-area coordinates of <see cref="Window"/> (or of the bound window).</summary>
        public Point At;
        /// <summary>Drag: where the button is released.</summary>
        public Point To;
        public WindowRef Window;
        public string Text = string.Empty;
        /// <summary>Key code plus Keys.Shift / Keys.Control / Keys.Alt.</summary>
        public Keys Key;
        /// <summary>Key: how many presses in a row.</summary>
        public int Count = 1;
        /// <summary>Scroll: wheel notches, positive = up.</summary>
        public int Notches;
        public int DurationMs;
        /// <summary>Wait: the duration as recorded (0 = not recorded, inserted by hand).</summary>
        public int RecordedMs;
        /// <summary>Unknown: the original file line, written back unchanged.</summary>
        public string Raw = string.Empty;

        // Wait until (WaitFor): DurationMs is the timeout, At + Window the watched point (Window = the dialog itself
        // for ConditionKind.Window), Text the element's name.
        public const int MinTimeoutMs = 1000, MaxTimeoutMs = 3600 * 1000, DefaultTimeoutMs = 30 * 1000;
        public ConditionKind Condition;
        /// <summary>Element: the UI Automation control type ("Button", "Hyperlink"...).</summary>
        public string ControlType = string.Empty;
        /// <summary>Color: 0xRRGGBB.</summary>
        public int Rgb;
        /// <summary>The wait this condition replaced (<see cref="Reverts"/>): its duration and recorded time.</summary>
        public int WasMs, WasRecordedMs;
        /// <summary>The condition came from a wait and can turn back into it (false = inserted from scratch).</summary>
        public bool Reverts;
        /// <summary>Element: the automation id, when name and type alone are not unique in the window.</summary>
        public string AutomationId = string.Empty;

        /// <summary>Click / DoubleClick: what to click, found before every click (null = the fixed point <see cref="At"/>).</summary>
        public SmartTarget Target;

        /// <summary>
        /// Click / DoubleClick proposed without a place (the AI Macro Builder never sees the screen): it is never clicked
        /// until a point is captured or typed, or a target is set.
        /// </summary>
        public bool NeedsPoint;

        public bool WaitsForPoint => NeedsPoint && Target == null && (Kind == StepKind.Click || Kind == StepKind.DoubleClick);

        public bool IsMouse =>
            Kind == StepKind.Click || Kind == StepKind.DoubleClick || Kind == StepKind.Drag ||
            Kind == StepKind.Hover || Kind == StepKind.Scroll;

        public bool IsShortened => Kind == StepKind.Wait && RecordedMs > 0 && RecordedMs != DurationMs;

        public MacroStep Clone()
        {
            var copy = (MacroStep)MemberwiseClone();
            copy.Window = Window?.Clone();
            copy.Target = Target?.Clone();
            return copy;
        }

        /// <summary>The sentence shown in the step list.</summary>
        public string Describe()
        {
            string where = Window == null ? string.Empty
                : Window.IsMenu ? Loc.T("step.inMenu")
                : Loc.F("step.inWindow", Clean(Window.Title));
            if (Target != null && (Kind == StepKind.Click || Kind == StepKind.DoubleClick))
            {
                string what = Target.Kind == TargetKind.Element
                    ? Loc.F("step.targetElement", Lower(Loc.ControlTypeLabel(Target.ControlType)), Clean(Target.Name))
                    : Loc.F("step.targetImage", At.X, At.Y);
                return Loc.F(Kind == StepKind.Click ? "step.clickTarget" : "step.doubleTarget", ButtonName(Button), what) + where;
            }
            if (WaitsForPoint)
                return Loc.F(Kind == StepKind.Click ? "step.clickPending" : "step.doublePending", ButtonName(Button), Loc.T("step.pendingPoint"));
            switch (Kind)
            {
                case StepKind.Click: return Loc.F("step.click", ButtonName(Button), At.X, At.Y) + where;
                case StepKind.DoubleClick: return Loc.F("step.double", ButtonName(Button), At.X, At.Y) + where;
                case StepKind.Drag: return Loc.F("step.drag", ButtonName(Button), At.X, At.Y, To.X, To.Y) + where;
                case StepKind.Hover: return Loc.F("step.hover", At.X, At.Y) + where;
                case StepKind.Scroll:
                    return Loc.F(Notches >= 0 ? "step.scrollUp" : "step.scrollDown", Math.Abs(Notches), At.X, At.Y) + where;
                case StepKind.Type: return Loc.F("step.type", Clean(Text));
                case StepKind.Key:
                    return Loc.F("step.key", MacroKeys.Display(Key)) + (Count > 1 ? " \u00D7" + Count : string.Empty);
                case StepKind.Shortcut: return Loc.F("step.shortcut", MacroKeys.Display(Key));
                case StepKind.Wait:
                    string text = Loc.F("step.wait", Seconds(DurationMs));
                    return IsShortened ? text + Loc.F("step.recorded", Seconds(RecordedMs)) : text;
                case StepKind.Password: return Loc.T("step.password");
                case StepKind.WaitFor:
                    string limit = Seconds(DurationMs);
                    switch (Condition)
                    {
                        case ConditionKind.Element: return Loc.F("step.waitElement", Clean(Text), limit) + where;
                        case ConditionKind.Color: return Loc.F("step.waitColor", Rgb.ToString("X6", CultureInfo.InvariantCulture), At.X, At.Y, limit) + where;
                        default: return Loc.F("step.waitWindow", Clean(Window?.Title ?? string.Empty), limit);
                    }
                default: return Loc.F("step.unknown", Raw);
            }
        }

        internal static string Seconds(int ms) =>
            (ms / 1000.0).ToString(ms % 1000 == 0 ? "0" : ms % 100 == 0 ? "0.0" : "0.00", CultureInfo.CurrentCulture);

        internal static string Lower(string text) =>
            text.Length == 0 ? text : char.ToLower(text[0], CultureInfo.CurrentCulture) + text.Substring(1);

        private static string ButtonName(ClickButton button) =>
            Loc.T(button == ClickButton.Right ? "step.right" : button == ClickButton.Middle ? "step.middle" : "step.left");

        /// <summary>One line, no control characters: what the list can show.</summary>
        private static string Clean(string text)
        {
            var clean = new StringBuilder(text.Length);
            foreach (char c in text) clean.Append(char.IsControl(c) ? ' ' : c);
            return clean.ToString();
        }
    }

    /// <summary>A recorded sequence, bound to an app by its executable.</summary>
    internal sealed class Macro
    {
        public const int LongWaitMs = 5000;

        public string Name = string.Empty;
        public string ExeName = string.Empty;
        /// <summary>Window title and client size at recording time: only used to warn before running.</summary>
        public string Title = string.Empty;
        public Size ClientSize;
        /// <summary>Repetitions; 0 = until stopped.</summary>
        public int Repeat = 1;
        public const int MaxLimitMinutes = 99 * 60 + 59;
        /// <summary>The run stops after this many minutes, pauses included; 0 = no limit.</summary>
        public int LimitMinutes;
        public List<MacroStep> Steps = new List<MacroStep>();
        /// <summary>Header lines this version does not know (from a newer one): written back unchanged.</summary>
        public List<string> ExtraHeader = new List<string>();

        /// <summary>"1 h 30 min", "45 min", "2 h".</summary>
        public static string LimitText(int minutes) =>
            minutes < 60 ? minutes + " min" : minutes % 60 == 0 ? minutes / 60 + " h" : minutes / 60 + " h " + minutes % 60 + " min";

        public bool HasText => Steps.Any(s => s.Kind == StepKind.Type);
        public bool HasPassword => Steps.Any(s => s.Kind == StepKind.Password);
        public bool HasUnknown => Steps.Any(s => s.Kind == StepKind.Unknown);

        /// <summary>Waits of 5 s or more: candidates for the pause optimization.</summary>
        public IEnumerable<int> LongWaits()
        {
            for (int i = 0; i < Steps.Count; i++)
                if (Steps[i].Kind == StepKind.Wait && Steps[i].DurationMs >= LongWaitMs) yield return i;
        }

        public Macro Clone()
        {
            var copy = (Macro)MemberwiseClone();
            copy.Steps = Steps.Select(s => s.Clone()).ToList();
            copy.ExtraHeader = new List<string>(ExtraHeader);
            return copy;
        }
    }

    /// <summary>Names of the keys a macro can press: what the list shows and what the file stores.</summary>
    internal static class MacroKeys
    {
        /// <summary>Keys recorded as a "Key" step (with or without Shift); everything else that types is text.</summary>
        public static bool IsSpecial(Keys code) =>
            code == Keys.Enter || code == Keys.Tab || code == Keys.Escape || code == Keys.Back || code == Keys.Delete ||
            code == Keys.Insert || code == Keys.Home || code == Keys.End || code == Keys.PageUp || code == Keys.PageDown ||
            code == Keys.Up || code == Keys.Down || code == Keys.Left || code == Keys.Right ||
            (code >= Keys.F1 && code <= Keys.F24);

        /// <summary>Keys SendInput must flag as extended (otherwise they arrive as numpad keys).</summary>
        public static bool IsExtended(Keys code) =>
            code == Keys.Delete || code == Keys.Insert || code == Keys.Home || code == Keys.End ||
            code == Keys.PageUp || code == Keys.PageDown ||
            code == Keys.Up || code == Keys.Down || code == Keys.Left || code == Keys.Right;

        public static string Display(Keys key)
        {
            string name;
            switch (key & Keys.KeyCode)
            {
                case Keys.Up: name = "\u2191"; break;
                case Keys.Down: name = "\u2193"; break;
                case Keys.Left: name = "\u2190"; break;
                case Keys.Right: name = "\u2192"; break;
                default: return Name(key);
            }
            return Prefix(key) + name;
        }

        /// <summary>"Ctrl+Shift+C": the file form, also used for display of most keys.</summary>
        public static string Name(Keys key)
        {
            Keys code = key & Keys.KeyCode;
            string name;
            if (code >= Keys.D0 && code <= Keys.D9) name = ((char)('0' + (code - Keys.D0))).ToString();
            else
            {
                switch (code)
                {
                    case Keys.Enter: name = "Enter"; break;
                    case Keys.Back: name = "Backspace"; break;
                    case Keys.Escape: name = "Esc"; break;
                    case Keys.PageUp: name = "PageUp"; break;
                    case Keys.PageDown: name = "PageDown"; break;
                    default:
                        // Keys without a name (the ABNT2 "/" key is 0xC1) are written as VK followed by the hex code.
                        name = Enum.IsDefined(typeof(Keys), code) ? code.ToString() : "VK" + ((int)code).ToString("X2", CultureInfo.InvariantCulture);
                        break;
                }
            }
            return Prefix(key) + name;
        }

        private static string Prefix(Keys key) =>
            ((key & Keys.Control) != 0 ? "Ctrl+" : string.Empty) +
            ((key & Keys.Alt) != 0 ? "Alt+" : string.Empty) +
            ((key & Keys.Shift) != 0 ? "Shift+" : string.Empty);

        public static bool TryParse(string text, out Keys key)
        {
            key = Keys.None;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text.Split('+');
            Keys modifiers = Keys.None;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim().ToLowerInvariant())
                {
                    case "ctrl": modifiers |= Keys.Control; break;
                    case "alt": modifiers |= Keys.Alt; break;
                    case "shift": modifiers |= Keys.Shift; break;
                    default: return false;
                }
            }
            string name = parts[parts.Length - 1].Trim();
            Keys code;
            if (name.Length == 1 && name[0] >= '0' && name[0] <= '9') code = Keys.D0 + (name[0] - '0');
            else
            {
                switch (name.ToLowerInvariant())
                {
                    case "enter": code = Keys.Enter; break;
                    case "backspace": code = Keys.Back; break;
                    case "esc": code = Keys.Escape; break;
                    case "pageup": code = Keys.PageUp; break;
                    case "pagedown": code = Keys.PageDown; break;
                    default:
                        if (name.Length > 2 && name.StartsWith("VK", StringComparison.OrdinalIgnoreCase) &&
                            int.TryParse(name.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int vk) &&
                            vk > 0 && vk <= (int)Keys.OemClear && !Hotkey.IsModifierKey((Keys)vk))
                        {
                            code = (Keys)vk;
                            break;
                        }
                        // Names only (Enum.TryParse also takes numbers), and only real key codes.
                        if (name.Length == 0 || !char.IsLetter(name[0]) || !Enum.TryParse(name, true, out code) ||
                            !Enum.IsDefined(typeof(Keys), code) || code > Keys.OemClear || code <= Keys.None ||
                            Hotkey.IsModifierKey(code) || (code & ~Keys.KeyCode) != 0)
                            return false;
                        break;
                }
            }
            key = code | modifiers;
            return true;
        }
    }

    /// <summary>
    /// The .vcmacro text format: a magic line, key=value header lines, then one step per line after [steps].
    /// Reading never throws anything but FormatException (bad header) and never drops a line it does not know.
    /// </summary>
    internal static class MacroFile
    {
        public const string Extension = ".vcmacro";
        private const string Magic = "V-Clicker macro 1";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Macros live in a "Macros" folder next to the settings file (portable mode included).</summary>
        public static string Folder(string settingsPath) =>
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath)) ?? ".", "Macros");

        /// <summary>Where the macro called <paramref name="name"/> is saved in <paramref name="folder"/>.</summary>
        public static string PathFor(string folder, string name) => Path.Combine(folder, SafeFileName(name) + Extension);

        /// <summary>The macro files in <paramref name="folder"/>; none when it is missing or unreadable.</summary>
        public static string[] SavedFiles(string folder)
        {
            try { return Directory.Exists(folder) ? Directory.GetFiles(folder, "*" + Extension) : new string[0]; }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return new string[0]; }
        }

        /// <summary>The two paths name the same file.</summary>
        public static bool SamePath(string a, string b) =>
            string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

        public static string SafeFileName(string name)
        {
            var clean = new StringBuilder();
            foreach (char c in name ?? string.Empty)
                if (Array.IndexOf(Path.GetInvalidFileNameChars(), c) < 0 && !char.IsControl(c)) clean.Append(c);
            string result = clean.ToString().Trim().TrimEnd('.', ' ');
            if (result.Length > 80) result = result.Substring(0, 80).TrimEnd('.', ' ');
            string stem = result.Split('.')[0].ToUpperInvariant();
            bool reserved = stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" ||
                            (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3]));
            if (reserved) result = "_" + result;
            return result.Length == 0 ? "macro" : result;
        }

        public static void Save(Macro macro, string path)
        {
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            string temp = path + ".tmp";
            File.WriteAllText(temp, Write(macro), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        public static Macro Load(string path) => Read(File.ReadAllText(path, Encoding.UTF8));

        internal static string Write(Macro macro)
        {
            var text = new StringBuilder();
            text.Append(Magic).Append('\n');
            text.Append("name=").Append(OneLine(macro.Name)).Append('\n');
            text.Append("exe=").Append(OneLine(macro.ExeName)).Append('\n');
            text.Append("title=").Append(OneLine(macro.Title)).Append('\n');
            text.Append("client=").Append(macro.ClientSize.Width.ToString(Inv)).Append('x')
                .Append(macro.ClientSize.Height.ToString(Inv)).Append('\n');
            text.Append("repeat=").Append(macro.Repeat.ToString(Inv)).Append('\n');
            // Only when set: a macro without a limit keeps the exact text (and safety approval) it had before limits existed.
            if (macro.LimitMinutes > 0) text.Append("limit=").Append(macro.LimitMinutes.ToString(Inv)).Append('\n');
            foreach (string line in macro.ExtraHeader) text.Append(line).Append('\n');
            text.Append("[steps]\n");
            foreach (MacroStep step in macro.Steps) text.Append(Line(step)).Append('\n');
            return text.ToString();
        }

        internal static Macro Read(string content)
        {
            string[] lines = content.Replace("\r\n", "\n").Split('\n');
            if (lines.Length == 0 || lines[0].Trim().TrimStart('\uFEFF') != Magic)
                throw new FormatException("not a V-Clicker macro");
            var macro = new Macro();
            int i = 1;
            for (; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Trim() == "[steps]") { i++; break; }
                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    if (line.Trim().Length > 0) macro.ExtraHeader.Add(line.TrimEnd());
                    continue;
                }
                string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "name": macro.Name = value; break;
                    case "exe": macro.ExeName = ClickSettings.IsExeFileName(value) ? value : string.Empty; break;
                    case "title": macro.Title = value; break;
                    case "client":
                        string[] size = value.Split('x');
                        if (size.Length == 2 && int.TryParse(size[0], NumberStyles.Integer, Inv, out int w) &&
                            int.TryParse(size[1], NumberStyles.Integer, Inv, out int h))
                            macro.ClientSize = new Size(Clamp(w, 0, MacroStep.MaxCoordinate), Clamp(h, 0, MacroStep.MaxCoordinate));
                        break;
                    case "repeat":
                        if (int.TryParse(value, NumberStyles.Integer, Inv, out int repeat)) macro.Repeat = Clamp(repeat, 0, 1000000);
                        break;
                    case "limit":
                        if (int.TryParse(value, NumberStyles.Integer, Inv, out int limit)) macro.LimitMinutes = Clamp(limit, 0, Macro.MaxLimitMinutes);
                        break;
                    default:
                        macro.ExtraHeader.Add(line.TrimEnd());
                        break;
                }
            }
            for (; i < lines.Length; i++)
            {
                if (lines[i].Trim().Length == 0) continue;
                macro.Steps.Add(ParseStep(lines[i]));
            }
            return macro;
        }

        private static string Line(MacroStep step)
        {
            string window = step.Window == null ? string.Empty
                : " in " + Quote(step.Window.Class) + " " + Quote(step.Window.Title);
            string find = step.Target == null ? string.Empty : " " + Find(step.Target);
            switch (step.Kind)
            {
                case StepKind.Click: return "click " + Btn(step.Button) + " " + (step.NeedsPoint ? "pending" : Pt(step.At)) + find + window;
                case StepKind.DoubleClick: return "double " + Btn(step.Button) + " " + (step.NeedsPoint ? "pending" : Pt(step.At)) + find + window;
                case StepKind.Drag: return "drag " + Btn(step.Button) + " " + Pt(step.At) + " " + Pt(step.To) + window;
                case StepKind.Hover: return "hover " + Pt(step.At) + window;
                case StepKind.Scroll: return "scroll " + step.Notches.ToString(Inv) + " " + Pt(step.At) + window;
                case StepKind.Type: return "type " + Quote(step.Text);
                case StepKind.Key:
                    return "key " + MacroKeys.Name(step.Key) + (step.Count > 1 ? " x" + step.Count.ToString(Inv) : string.Empty);
                case StepKind.Shortcut: return "shortcut " + MacroKeys.Name(step.Key);
                case StepKind.Wait:
                    return "wait " + step.DurationMs.ToString(Inv) +
                           (step.RecordedMs > 0 ? " recorded=" + step.RecordedMs.ToString(Inv) : string.Empty);
                case StepKind.Password: return "password";
                case StepKind.WaitFor:
                    string target;
                    switch (step.Condition)
                    {
                        case ConditionKind.Element:
                            target = "element " + Pt(step.At) + " " + Quote(step.ControlType) + " " + Quote(step.Text) + Id(step.AutomationId);
                            break;
                        case ConditionKind.Color: target = "color " + Pt(step.At) + " " + step.Rgb.ToString("X6", Inv); break;
                        default: target = "window " + Quote(step.Window?.Class) + " " + Quote(step.Window?.Title); break;
                    }
                    return "waitfor " + target + " timeout=" + step.DurationMs.ToString(Inv) +
                           (step.Reverts ? " was=" + step.WasMs.ToString(Inv) : string.Empty) +
                           (step.WasRecordedMs > 0 ? " recorded=" + step.WasRecordedMs.ToString(Inv) : string.Empty) +
                           (step.Condition != ConditionKind.Window ? window : string.Empty);
                default: return step.Raw;
            }
        }

        private static string Find(SmartTarget target) =>
            "find " + (target.Kind == TargetKind.Element
                ? "element " + Quote(target.ControlType) + " " + Quote(target.Name) + Id(target.AutomationId)
                : "image " + Convert.ToBase64String(target.Png)) + " timeout=" + target.TimeoutMs.ToString(Inv);

        private static string Id(string id) => string.IsNullOrEmpty(id) ? string.Empty : " id " + Quote(id);

        /// <summary>"find element "T" "N" [id "I"] timeout=N" or "find image BASE64 timeout=N", from t[i].</summary>
        private static bool ParseFind(List<string> t, ref int i, out SmartTarget target)
        {
            target = null;
            if (i + 1 >= t.Count || t[i] != "find") return false;
            var found = new SmartTarget();
            i++;
            switch (t[i++])
            {
                case "element":
                    if (i + 1 >= t.Count || t[i].Length == 0 || t[i + 1].Length == 0) return false;
                    found.Kind = TargetKind.Element;
                    found.ControlType = t[i];
                    found.Name = t[i + 1];
                    i += 2;
                    if (!ParseId(t, ref i, out found.AutomationId)) return false;
                    break;
                case "image":
                    if (i >= t.Count) return false;
                    found.Kind = TargetKind.Image;
                    try { found.Png = Convert.FromBase64String(t[i++]); }
                    catch (FormatException) { return false; }
                    using (Bitmap image = found.Decode())
                        if (image == null) return false; // not a square PNG of a size the matcher uses
                    break;
                default:
                    return false;
            }
            if (i >= t.Count || !t[i].StartsWith("timeout=", StringComparison.Ordinal) ||
                !int.TryParse(t[i].Substring(8), NumberStyles.Integer, Inv, out int timeout)) return false;
            found.TimeoutMs = Clamp(timeout, MacroStep.MinTimeoutMs, MacroStep.MaxTimeoutMs);
            i++;
            target = found;
            return true;
        }

        /// <summary>An optional "id "I"" (a non-empty id) at t[i].</summary>
        private static bool ParseId(List<string> t, ref int i, out string id)
        {
            id = string.Empty;
            if (i >= t.Count || t[i] != "id") return true;
            if (i + 1 >= t.Count || t[i + 1].Length == 0) return false;
            id = t[i + 1];
            i += 2;
            return true;
        }

        private static MacroStep ParseStep(string line)
        {
            var unknown = new MacroStep { Kind = StepKind.Unknown, Raw = line.TrimEnd() };
            List<string> t = Tokens(line);
            if (t == null || t.Count == 0) return unknown;
            var step = new MacroStep();
            int i = 1;
            switch (t[0])
            {
                case "click":
                case "double":
                case "drag":
                    step.Kind = t[0] == "click" ? StepKind.Click : t[0] == "double" ? StepKind.DoubleClick : StepKind.Drag;
                    if (!ParseButton(t, ref i, out step.Button)) return unknown;
                    if (step.Kind != StepKind.Drag && i < t.Count && t[i] == "pending")
                    {
                        step.NeedsPoint = true;
                        i++;
                    }
                    else if (!ParsePoint(t, ref i, out step.At)) return unknown;
                    if (step.Kind == StepKind.Drag && !ParsePoint(t, ref i, out step.To)) return unknown;
                    if (step.Kind != StepKind.Drag && i < t.Count && t[i] == "find" && !ParseFind(t, ref i, out step.Target)) return unknown;
                    break;
                case "hover":
                    step.Kind = StepKind.Hover;
                    if (!ParsePoint(t, ref i, out step.At)) return unknown;
                    break;
                case "scroll":
                    step.Kind = StepKind.Scroll;
                    if (!ParseInt(t, ref i, out int notches) || !ParsePoint(t, ref i, out step.At)) return unknown;
                    step.Notches = Clamp(notches, -MacroStep.MaxCount, MacroStep.MaxCount);
                    if (step.Notches == 0) return unknown;
                    break;
                case "type":
                    step.Kind = StepKind.Type;
                    if (t.Count != 2 || t[1].Length == 0) return unknown;
                    step.Text = t[1];
                    return step;
                case "key":
                case "shortcut":
                    step.Kind = t[0] == "key" ? StepKind.Key : StepKind.Shortcut;
                    if (t.Count < 2 || !MacroKeys.TryParse(t[1], out step.Key)) return unknown;
                    if (t.Count == 3 && step.Kind == StepKind.Key && t[2].StartsWith("x") &&
                        int.TryParse(t[2].Substring(1), NumberStyles.Integer, Inv, out int count))
                        step.Count = Clamp(count, 1, MacroStep.MaxCount);
                    else if (t.Count != 2) return unknown;
                    return step;
                case "wait":
                    step.Kind = StepKind.Wait;
                    if (!ParseInt(t, ref i, out int ms)) return unknown;
                    step.DurationMs = Clamp(ms, 0, MacroStep.MaxWaitMs);
                    if (t.Count == 3 && t[2].StartsWith("recorded=") &&
                        int.TryParse(t[2].Substring(9), NumberStyles.Integer, Inv, out int recorded))
                        step.RecordedMs = Clamp(recorded, 0, MacroStep.MaxWaitMs);
                    else if (t.Count != 2) return unknown;
                    return step;
                case "password":
                    step.Kind = StepKind.Password;
                    return t.Count == 1 ? step : unknown;
                case "waitfor":
                    return ParseWaitFor(t, step) ? step : unknown;
                default:
                    return unknown;
            }
            // Mouse steps: optional "in <class> <title>".
            if (i == t.Count) return step;
            if (t.Count == i + 3 && t[i] == "in" && t[i + 1].Length > 0)
            {
                step.Window = new WindowRef { Class = t[i + 1], Title = t[i + 2] };
                return step;
            }
            return unknown;
        }

        private static bool ParseWaitFor(List<string> t, MacroStep step)
        {
            step.Kind = StepKind.WaitFor;
            if (t.Count < 2) return false;
            int i = 2;
            switch (t[1])
            {
                case "element":
                    step.Condition = ConditionKind.Element;
                    if (!ParsePoint(t, ref i, out step.At) || i + 1 >= t.Count || t[i].Length == 0 || t[i + 1].Length == 0) return false;
                    step.ControlType = t[i];
                    step.Text = t[i + 1];
                    i += 2;
                    if (!ParseId(t, ref i, out step.AutomationId)) return false;
                    break;
                case "color":
                    step.Condition = ConditionKind.Color;
                    if (!ParsePoint(t, ref i, out step.At) || i >= t.Count || t[i].Length != 6 ||
                        !int.TryParse(t[i], NumberStyles.AllowHexSpecifier, Inv, out step.Rgb)) return false;
                    i++;
                    break;
                case "window":
                    step.Condition = ConditionKind.Window;
                    if (i + 1 >= t.Count || t[i].Length == 0) return false;
                    step.Window = new WindowRef { Class = t[i], Title = t[i + 1] };
                    i += 2;
                    break;
                default:
                    return false;
            }
            bool timeout = false;
            while (i < t.Count)
            {
                if (t[i] == "in" && step.Condition != ConditionKind.Window && step.Window == null && i + 2 < t.Count && t[i + 1].Length > 0)
                {
                    step.Window = new WindowRef { Class = t[i + 1], Title = t[i + 2] };
                    i += 3;
                    continue;
                }
                int eq = t[i].IndexOf('=');
                if (eq <= 0 || !int.TryParse(t[i].Substring(eq + 1), NumberStyles.Integer, Inv, out int value)) return false;
                switch (t[i].Substring(0, eq))
                {
                    case "timeout": step.DurationMs = Clamp(value, MacroStep.MinTimeoutMs, MacroStep.MaxTimeoutMs); timeout = true; break;
                    case "was": step.WasMs = Clamp(value, 0, MacroStep.MaxWaitMs); step.Reverts = true; break;
                    case "recorded": step.WasRecordedMs = Clamp(value, 0, MacroStep.MaxWaitMs); break;
                    default: return false;
                }
                i++;
            }
            return timeout; // a condition without a time limit is never run
        }

        private static bool ParseButton(List<string> t, ref int i, out ClickButton button)
        {
            button = ClickButton.Left;
            if (i >= t.Count) return false;
            switch (t[i++])
            {
                case "left": return true;
                case "right": button = ClickButton.Right; return true;
                case "middle": button = ClickButton.Middle; return true;
                default: return false;
            }
        }

        private static bool ParsePoint(List<string> t, ref int i, out Point point)
        {
            point = Point.Empty;
            if (!ParseInt(t, ref i, out int x) || !ParseInt(t, ref i, out int y)) return false;
            point = new Point(Clamp(x, -MacroStep.MaxCoordinate, MacroStep.MaxCoordinate), Clamp(y, -MacroStep.MaxCoordinate, MacroStep.MaxCoordinate));
            return true;
        }

        private static bool ParseInt(List<string> t, ref int i, out int value)
        {
            value = 0;
            if (i >= t.Count) return false;
            if (!long.TryParse(t[i++], NumberStyles.AllowLeadingSign, Inv, out long parsed)) return false;
            value = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, parsed));
            return true;
        }

        /// <summary>Splits on spaces; "quoted" tokens may contain spaces and \\ \" \n \t escapes. null = malformed.</summary>
        private static List<string> Tokens(string line)
        {
            var tokens = new List<string>();
            int i = 0;
            while (true)
            {
                while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
                if (i >= line.Length) return tokens;
                var token = new StringBuilder();
                if (line[i] == '"')
                {
                    i++;
                    while (true)
                    {
                        if (i >= line.Length) return null;
                        char c = line[i++];
                        if (c == '"') break;
                        if (c == '\\')
                        {
                            if (i >= line.Length) return null;
                            char e = line[i++];
                            token.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e);
                        }
                        else token.Append(c);
                    }
                    if (i < line.Length && line[i] != ' ' && line[i] != '\t') return null;
                }
                else
                {
                    while (i < line.Length && line[i] != ' ' && line[i] != '\t') token.Append(line[i++]);
                }
                tokens.Add(token.ToString());
            }
        }

        private static string Quote(string text)
        {
            var quoted = new StringBuilder("\"");
            foreach (char c in text ?? string.Empty)
            {
                switch (c)
                {
                    case '\\': quoted.Append("\\\\"); break;
                    case '"': quoted.Append("\\\""); break;
                    case '\n': quoted.Append("\\n"); break;
                    case '\t': quoted.Append("\\t"); break;
                    case '\r': break;
                    default: quoted.Append(c); break;
                }
            }
            return quoted.Append('"').ToString();
        }

        internal static string OneLine(string text) => (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();

        private static string Btn(ClickButton button) =>
            button == ClickButton.Right ? "right" : button == ClickButton.Middle ? "middle" : "left";

        private static string Pt(Point point) => point.X.ToString(Inv) + " " + point.Y.ToString(Inv);

        private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
    }
}
