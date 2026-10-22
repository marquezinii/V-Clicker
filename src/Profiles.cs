using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace VClicker
{
    /// <summary>The click settings V-Clicker keeps for one Window Lock app.</summary>
    internal sealed class AppProfile
    {
        public string ExeName = string.Empty;
        public string Name = string.Empty;
        public DateTime UsedUtc;
        /// <summary>Only the fields in <see cref="Profiles.Keys"/> mean anything here.</summary>
        public ClickSettings Settings = new ClickSettings();
    }

    /// <summary>
    /// Automatic per-app profiles (docs/superpowers/specs/2026-10-21-profiles-design.md): one .vcprofile file per
    /// executable in a "Profiles" folder next to the settings. The settings part uses the settings.ini keys; an
    /// exported file also carries the app's macros, each after a "[macro]" line, in the .vcmacro format.
    /// </summary>
    internal static class Profiles
    {
        public const string Extension = ".vcprofile";
        public const long MaxFileBytes = 20 * 1024 * 1024;
        private const string Magic = "V-Clicker profile 1";
        private const string MacroMark = "[macro]";

        /// <summary>The settings a profile carries. Hotkeys, window options, sounds and language stay global.</summary>
        internal static readonly string[] Keys =
        {
            "IntervalMs", "SpeedInCps", "JitterPercent", "Button", "DoubleClick", "RepeatCount", "UseFixedPosition", "X", "Y",
            "StartDelaySeconds", "HoldMode", "LockStopOnFocusLoss"
        };

        public static string Folder(string settingsPath) =>
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath)) ?? ".", "Profiles");

        private static string PathFor(string folder, string exe) => Path.Combine(folder, exe.Trim() + Extension);

        private static Dictionary<string, string> Pairs(ClickSettings settings) =>
            ClickSettings.ReadPairs(settings.Serialize().Where(IsProfileLine));

        private static bool IsProfileLine(string line)
        {
            int eq = line.IndexOf('=');
            return eq > 0 && Keys.Contains(line.Substring(0, eq).Trim(), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Copies the profile's fields (and only those) from one settings object to another.</summary>
        public static void CopyFields(ClickSettings from, ClickSettings to)
        {
            to.Apply(Pairs(from));
            to.Validate();
        }

        /// <summary>The profile of the app <paramref name="exe"/>; null if there is none or it cannot be read.</summary>
        public static AppProfile Load(string folder, string exe)
        {
            if (!ClickSettings.IsExeFileName(exe)) return null;
            try
            {
                string file = PathFor(folder, exe);
                return File.Exists(file) ? Read(File.ReadAllText(file, Encoding.UTF8), null, out _) : null;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is FormatException)
            {
                return null;
            }
        }

        /// <summary>Every readable profile, most recently used first.</summary>
        public static List<AppProfile> List(string folder)
        {
            var found = new List<AppProfile>();
            string[] files;
            try { files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*" + Extension) : new string[0]; }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return found; }
            foreach (string file in files)
            {
                AppProfile profile = Load(folder, Path.GetFileNameWithoutExtension(file));
                if (profile != null) found.Add(profile);
            }
            return found.OrderByDescending(p => p.UsedUtc).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <returns>false if the file could not be written.</returns>
        public static bool Save(string folder, AppProfile profile)
        {
            if (!ClickSettings.IsExeFileName(profile.ExeName)) return false;
            try
            {
                WriteFile(PathFor(folder, profile.ExeName), Write(profile, Enumerable.Empty<Macro>()));
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return false; }
        }

        public static bool Delete(string folder, string exe)
        {
            if (!ClickSettings.IsExeFileName(exe)) return false;
            try
            {
                File.Delete(PathFor(folder, exe));
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return false; }
        }

        private static void WriteFile(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            File.WriteAllText(temp, content, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        internal static string Write(AppProfile profile, IEnumerable<Macro> macros)
        {
            var text = new StringBuilder();
            text.Append(Magic).Append('\n');
            text.Append("exe=").Append(profile.ExeName.Trim()).Append('\n');
            text.Append("name=").Append(OneLine(profile.Name)).Append('\n');
            text.Append("used=").Append(profile.UsedUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)).Append('\n');
            foreach (string line in profile.Settings.Serialize().Where(IsProfileLine)) text.Append(line).Append('\n');
            foreach (Macro macro in macros) text.Append(MacroMark).Append('\n').Append(MacroFile.Write(macro));
            return text.ToString();
        }

        /// <param name="macros">Receives the macros of the profile's app (null: they are not read).</param>
        /// <param name="skipped">Macros left out: unreadable, or of another app.</param>
        /// <exception cref="FormatException">Not a profile, or no valid executable.</exception>
        internal static AppProfile Read(string content, List<Macro> macros, out int skipped)
        {
            skipped = 0;
            string[] lines = content.Replace("\r\n", "\n").Split('\n');
            if (lines.Length == 0 || lines[0].Trim().TrimStart('﻿') != Magic) throw new FormatException("not a V-Clicker profile");
            int end = Array.FindIndex(lines, l => l.Trim() == MacroMark);
            Dictionary<string, string> pairs = ClickSettings.ReadPairs(lines.Skip(1).Take(end < 0 ? lines.Length : end - 1));
            if (!pairs.TryGetValue("exe", out string exe) || !ClickSettings.IsExeFileName(exe)) throw new FormatException("no app");
            var profile = new AppProfile { ExeName = exe.Trim(), Name = pairs.TryGetValue("name", out string name) ? name : string.Empty };
            if (profile.Name.Length == 0) profile.Name = Path.GetFileNameWithoutExtension(profile.ExeName);
            if (pairs.TryGetValue("used", out string used) &&
                DateTime.TryParse(used, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime when))
                profile.UsedUtc = when.ToUniversalTime();
            profile.Settings.Apply(pairs.Where(p => Keys.Contains(p.Key, StringComparer.OrdinalIgnoreCase))
                                        .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase));
            profile.Settings.Validate();
            if (macros == null || end < 0) return profile;

            var chunks = new List<StringBuilder> { new StringBuilder() };
            for (int i = end + 1; i < lines.Length; i++)
            {
                if (lines[i].Trim() == MacroMark) chunks.Add(new StringBuilder());
                else chunks[chunks.Count - 1].Append(lines[i]).Append('\n');
            }
            foreach (StringBuilder chunk in chunks)
            {
                try
                {
                    Macro macro = MacroFile.Read(chunk.ToString());
                    if (macro.ExeName.Equals(profile.ExeName, StringComparison.OrdinalIgnoreCase)) macros.Add(macro);
                    else skipped++;
                }
                catch (FormatException) { skipped++; }
            }
            return profile;
        }

        /// <exception cref="FormatException">Not a profile, or too big.</exception>
        public static AppProfile ReadFile(string path, out List<Macro> macros, out int skipped)
        {
            if (new FileInfo(path).Length > MaxFileBytes) throw new FormatException("too big");
            macros = new List<Macro>();
            return Read(File.ReadAllText(path, Encoding.UTF8), macros, out skipped);
        }

        /// <summary>The saved macros of the app <paramref name="exe"/> (unreadable files are left out).</summary>
        public static List<Macro> MacrosOf(string macroFolder, string exe) =>
            AllMacros(macroFolder).Where(m => m.ExeName.Equals(exe, StringComparison.OrdinalIgnoreCase)).ToList();

        /// <summary>Every readable saved macro.</summary>
        public static List<Macro> AllMacros(string macroFolder)
        {
            var found = new List<Macro>();
            string[] files;
            try { files = Directory.Exists(macroFolder) ? Directory.GetFiles(macroFolder, "*" + MacroFile.Extension) : new string[0]; }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return found; }
            foreach (string file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    found.Add(MacroFile.Load(file));
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is FormatException) { }
            }
            return found;
        }

        /// <summary>Writes the profile and the app's saved macros to one file.</summary>
        public static void Export(AppProfile profile, string macroFolder, string path) =>
            WriteFile(path, Write(profile, MacrosOf(macroFolder, profile.ExeName)));

        /// <summary>
        /// Saves imported macros as new files. One identical to a saved macro of the same name is not added again;
        /// a different one with a taken name gets " (2)", " (3)"...
        /// </summary>
        /// <returns>How many were added.</returns>
        public static int AddMacros(string macroFolder, List<Macro> macros)
        {
            int added = 0;
            foreach (Macro macro in macros)
            {
                string content = MacroFile.Write(macro);
                string stem = MacroFile.SafeFileName(macro.Name.Length > 0 ? macro.Name : Path.GetFileNameWithoutExtension(macro.ExeName));
                string file = Path.Combine(macroFolder, stem + MacroFile.Extension);
                bool duplicate = false;
                for (int n = 2; File.Exists(file); n++)
                {
                    if (SameMacro(file, content)) { duplicate = true; break; }
                    file = Path.Combine(macroFolder, stem + " (" + n.ToString(CultureInfo.InvariantCulture) + ")" + MacroFile.Extension);
                }
                if (duplicate) continue;
                MacroFile.Save(macro, file);
                added++;
            }
            return added;
        }

        /// <summary>The file holds this macro, however it is laid out (line ends, blank lines, an older writer).</summary>
        private static bool SameMacro(string file, string content)
        {
            try { return MacroFile.Write(MacroFile.Load(file)) == content; }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is FormatException) { return false; }
        }

        /// <summary>"15 CPS · left": what the profile clicks, in a few words.</summary>
        public static string Summary(ClickSettings settings)
        {
            double perSecond = Interval.ClicksPerSecond(settings.IntervalMs);
            string speed = perSecond >= 0.1 ? perSecond.ToString("0.#", CultureInfo.CurrentCulture) : MainForm.FormatCps(perSecond);
            string button = settings.Button == ClickButton.Right ? "button.right" : settings.Button == ClickButton.Middle ? "button.middle" : "button.left";
            return speed + " CPS · " + MacroStep.Lower(Loc.T(button));
        }

        private static string OneLine(string text) => (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}
