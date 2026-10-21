using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace VClicker
{
    /// <summary>Screen corner of the on-screen status indicator. Declaration order is the UI segment order.</summary>
    internal enum HudCorner { TopLeft, TopRight, BottomLeft, BottomRight }

    /// <summary>
    /// User configuration. Persisted as a plain key=value .ini file so it is easy to read and edit,
    /// and so the app needs no serializer dependency. Loading never throws: bad data falls back to defaults.
    /// </summary>
    internal sealed class ClickSettings
    {
        public static readonly Hotkey DefaultToggle = new Hotkey(Keys.F6);
        public static readonly Hotkey DefaultStop = new Hotkey(Keys.F8);
        public static readonly Hotkey DefaultCapture = new Hotkey(Keys.F7);

        /// <summary>The one canonical click rate. Fractional, so 15 clicks/s is 1000/15 ms exactly.</summary>
        public double IntervalMs = 100;
        /// <summary>true = the speed is edited as clicks per second; false = as h / min / s / ms. Display only.</summary>
        public bool SpeedInCps = true;
        public int JitterPercent;
        public ClickButton Button = ClickButton.Left;
        public bool DoubleClick;
        /// <summary>0 = until stopped.</summary>
        public int RepeatCount;
        public bool UseFixedPosition;
        public int X, Y;
        public int StartDelaySeconds = 1;
        /// <summary>false = hotkey toggles on/off; true = click only while the hotkey is held.</summary>
        public bool HoldMode;

        public Hotkey ToggleKey = DefaultToggle;
        public Hotkey StopKey = DefaultStop;
        public Hotkey CaptureKey = DefaultCapture;

        /// <summary>Window Lock: clicks only reach a window of <see cref="LockExe"/> while it is in front.</summary>
        public bool WindowLock;
        /// <summary>File name of the locked app's executable (no folder: Roblox installs each version elsewhere).</summary>
        public string LockExe = string.Empty;
        /// <summary>Friendly name of the locked app, for display.</summary>
        public string LockName = string.Empty;
        /// <summary>false = pause and resume when the locked window loses focus; true = stop.</summary>
        public bool LockStopOnFocusLoss;

        public bool Hud = true;
        public HudCorner HudCorner = HudCorner.TopRight;

        public bool Sounds = true;
        public bool AlwaysOnTop;
        public bool MinimizeToTray;
        /// <summary>"auto", "pt" or "en".</summary>
        public string Language = "auto";

        /// <summary>Clamps every value into its supported range and repairs invalid or clashing hotkeys.</summary>
        public void Validate()
        {
            IntervalMs = Interval.Clamp(IntervalMs);
            JitterPercent = Math.Max(0, Math.Min(50, JitterPercent));
            if (!Enum.IsDefined(typeof(ClickButton), Button)) Button = ClickButton.Left;
            RepeatCount = Math.Max(0, Math.Min(1000000000, RepeatCount));
            X = Math.Max(-32768, Math.Min(32767, X));
            Y = Math.Max(-32768, Math.Min(32767, Y));
            StartDelaySeconds = Math.Max(0, Math.Min(30, StartDelaySeconds));
            if (Language != "pt" && Language != "en") Language = "auto";
            if (!Enum.IsDefined(typeof(HudCorner), HudCorner)) HudCorner = HudCorner.TopRight;
            LockExe = IsExeFileName(LockExe) ? LockExe.Trim() : string.Empty;
            LockName = Printable(LockName, 40); // shown in the badge and the indicator: keep it short
            if (LockName.Length == 0 && LockExe.Length > 0) LockName = Path.GetFileNameWithoutExtension(LockExe);

            if (!ToggleKey.IsAcceptable) ToggleKey = DefaultToggle;
            if (!StopKey.IsAcceptable || StopKey.Equals(ToggleKey)) StopKey = DefaultStop;
            if (!CaptureKey.IsAcceptable || CaptureKey.Equals(ToggleKey) || CaptureKey.Equals(StopKey))
                CaptureKey = DefaultCapture;
            // The defaults themselves can clash with a customised key; resolve by resetting all three.
            if (StopKey.Equals(ToggleKey) || CaptureKey.Equals(ToggleKey) || CaptureKey.Equals(StopKey))
            {
                ToggleKey = DefaultToggle;
                StopKey = DefaultStop;
                CaptureKey = DefaultCapture;
            }
        }

        /// <summary>A bare executable file name such as "RobloxPlayerBeta.exe": no folder, no odd characters.</summary>
        internal static bool IsExeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            name = name.Trim();
            return name.Length <= 260 && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && name.Length > 4 &&
                   name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name != "." && !name.StartsWith("..", StringComparison.Ordinal);
        }

        private static string Printable(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var clean = new System.Text.StringBuilder(Math.Min(text.Length, max));
            foreach (char c in text)
                if (!char.IsControl(c) && clean.Length < max) clean.Append(c);
            return clean.ToString().Trim();
        }

        public ClickPlan ToPlan() => new ClickPlan
        {
            IntervalMs = IntervalMs,
            JitterPercent = JitterPercent,
            Button = Button,
            DoubleClick = DoubleClick,
            MaxClicks = RepeatCount,
            FixedPosition = UseFixedPosition ? new Point(X, Y) : (Point?)null,
            StartDelayMs = StartDelaySeconds * 1000
        };

        // ---------------------------------------------------------------- persistence

        public static ClickSettings Load(string path)
        {
            var settings = new ClickSettings();
            try
            {
                if (File.Exists(path)) settings.Apply(ReadPairs(File.ReadAllLines(path)));
            }
            catch (IOException) { settings = new ClickSettings { readFailed = true }; }
            catch (UnauthorizedAccessException) { settings = new ClickSettings { readFailed = true }; }
            settings.Validate();
            return settings;
        }

        // The file exists but could not be read (locked by an antivirus, say): do not overwrite it with defaults.
        private bool readFailed;

        /// <returns>false if the file could not be written (read-only folder, disk full...).</returns>
        public bool Save(string path)
        {
            if (readFailed) return false;
            try
            {
                Validate();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                string temp = path + ".tmp";
                File.WriteAllLines(temp, Serialize());
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        internal IEnumerable<string> Serialize()
        {
            yield return "; V-Clicker settings - safe to edit while the app is closed.";
            // "G17" round-trips the exact double (1000/15 reads back as the same rate); "R" can fail on x64 .NET Framework.
            yield return "IntervalMs=" + IntervalMs.ToString("G17", CultureInfo.InvariantCulture);
            yield return "SpeedInCps=" + SpeedInCps;
            yield return "JitterPercent=" + Number(JitterPercent);
            yield return "Button=" + Button;
            yield return "DoubleClick=" + DoubleClick;
            yield return "RepeatCount=" + Number(RepeatCount);
            yield return "UseFixedPosition=" + UseFixedPosition;
            yield return "X=" + Number(X);
            yield return "Y=" + Number(Y);
            yield return "StartDelaySeconds=" + Number(StartDelaySeconds);
            yield return "HoldMode=" + HoldMode;
            yield return "ToggleKey=" + ToggleKey;
            yield return "StopKey=" + StopKey;
            yield return "CaptureKey=" + CaptureKey;
            yield return "WindowLock=" + WindowLock;
            yield return "LockExe=" + LockExe;
            yield return "LockName=" + LockName;
            yield return "LockStopOnFocusLoss=" + LockStopOnFocusLoss;
            yield return "Hud=" + Hud;
            yield return "HudCorner=" + HudCorner;
            yield return "Sounds=" + Sounds;
            yield return "AlwaysOnTop=" + AlwaysOnTop;
            yield return "MinimizeToTray=" + MinimizeToTray;
            yield return "Language=" + Language;
        }

        // Always invariant: some cultures write a Unicode minus sign that the invariant parser rejects.
        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

        internal static Dictionary<string, string> ReadPairs(IEnumerable<string> lines)
        {
            var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                pairs[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return pairs;
        }

        internal void Apply(Dictionary<string, string> pairs)
        {
            IntervalMs = Double(pairs, "IntervalMs", IntervalMs);
            SpeedInCps = Bool(pairs, "SpeedInCps", SpeedInCps);
            JitterPercent = Int(pairs, "JitterPercent", JitterPercent);
            Button = ReadEnum(pairs, "Button", Button);
            DoubleClick = Bool(pairs, "DoubleClick", DoubleClick);
            RepeatCount = Int(pairs, "RepeatCount", RepeatCount);
            UseFixedPosition = Bool(pairs, "UseFixedPosition", UseFixedPosition);
            X = Int(pairs, "X", X);
            Y = Int(pairs, "Y", Y);
            StartDelaySeconds = Int(pairs, "StartDelaySeconds", StartDelaySeconds);
            HoldMode = Bool(pairs, "HoldMode", HoldMode);
            ToggleKey = Key(pairs, "ToggleKey", ToggleKey);
            StopKey = Key(pairs, "StopKey", StopKey);
            CaptureKey = Key(pairs, "CaptureKey", CaptureKey);
            WindowLock = Bool(pairs, "WindowLock", WindowLock);
            if (pairs.TryGetValue("LockExe", out string exe)) LockExe = exe;
            if (pairs.TryGetValue("LockName", out string name)) LockName = name;
            LockStopOnFocusLoss = Bool(pairs, "LockStopOnFocusLoss", LockStopOnFocusLoss);
            Hud = Bool(pairs, "Hud", Hud);
            HudCorner = ReadEnum(pairs, "HudCorner", HudCorner);
            Sounds = Bool(pairs, "Sounds", Sounds);
            AlwaysOnTop = Bool(pairs, "AlwaysOnTop", AlwaysOnTop);
            MinimizeToTray = Bool(pairs, "MinimizeToTray", MinimizeToTray);
            if (pairs.TryGetValue("Language", out string language)) Language = language.ToLowerInvariant();
        }

        private static int Int(Dictionary<string, string> pairs, string key, int fallback) =>
            pairs.TryGetValue(key, out string text) &&
            int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value)
                ? value : fallback;

        // Infinity is clamped by Validate(); NaN falls back to the default there.
        private static double Double(Dictionary<string, string> pairs, string key, double fallback) =>
            pairs.TryGetValue(key, out string text) &&
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? value : fallback;

        private static bool Bool(Dictionary<string, string> pairs, string key, bool fallback) =>
            pairs.TryGetValue(key, out string text) && bool.TryParse(text, out bool value) ? value : fallback;

        private static T ReadEnum<T>(Dictionary<string, string> pairs, string key, T fallback) where T : struct =>
            pairs.TryGetValue(key, out string text) && Enum.TryParse(text, true, out T value) ? value : fallback;

        private static Hotkey Key(Dictionary<string, string> pairs, string key, Hotkey fallback) =>
            pairs.TryGetValue(key, out string text) && Hotkey.TryParse(text, out Hotkey value) ? value : fallback;
    }
}
