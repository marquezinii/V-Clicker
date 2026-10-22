using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace VClicker
{
    /// <summary>The answer to the safety review before a run.</summary>
    internal enum ReviewChoice { Cancel, Simulate, Run }

    /// <summary>A step that presses a combination that can affect the app or its data (it is shown, never blocked).</summary>
    internal sealed class ImpactfulAction
    {
        public int Step;
        public Keys Key;
        /// <summary>It can delete something (the alert icon); the others get the attention icon.</summary>
        public bool Deletes;

        public string Explain() => Loc.T("impact." + Name(Key));

        internal static string Name(Keys key)
        {
            switch (key)
            {
                case Keys.Delete: return "delete";
                case Keys.Delete | Keys.Shift: return "shiftDelete";
                case Keys.F4 | Keys.Alt: return "altF4";
                case Keys.W | Keys.Control: return "ctrlW";
                case Keys.Escape | Keys.Control | Keys.Shift: return "taskManager";
                default: return null;
            }
        }
    }

    /// <summary>
    /// The safety review before running (docs/superpowers/specs/2026-10-21-safeclick-design.md): which steps press an
    /// impactful combination, and the approval of one local copy of a macro in one version.
    /// </summary>
    internal static class SafeClick
    {
        /// <summary>Key and shortcut steps that press exactly one of the listed combinations.</summary>
        public static List<ImpactfulAction> Impactful(Macro macro)
        {
            var found = new List<ImpactfulAction>();
            for (int i = 0; i < macro.Steps.Count; i++)
            {
                MacroStep step = macro.Steps[i];
                if ((step.Kind != StepKind.Key && step.Kind != StepKind.Shortcut) || ImpactfulAction.Name(step.Key) == null) continue;
                found.Add(new ImpactfulAction { Step = i, Key = step.Key, Deletes = (step.Key & Keys.KeyCode) == Keys.Delete });
            }
            return found;
        }

        /// <summary>SHA-256 (lowercase hex) of the macro as its file stores it, without the name: renaming changes nothing it does.</summary>
        public static string Signature(Macro macro)
        {
            Macro unnamed = macro.Clone();
            unnamed.Name = string.Empty;
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(MacroFile.Write(unnamed)));
                var hex = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        /// <summary>
        /// This local copy of a macro file: its creation time. Saving (File.Replace) and renaming (File.Move) keep it; a
        /// copied, downloaded or extracted file gets a new one. null = no file.
        /// </summary>
        public static string FileInstance(string path)
        {
            try
            {
                return path != null && File.Exists(path)
                    ? File.GetCreationTimeUtc(path).Ticks.ToString(CultureInfo.InvariantCulture)
                    : null;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is NotSupportedException)
            {
                return null;
            }
        }

        public static string ApprovalKey(string signature, string instance) => signature + "@" + instance;
    }
}
