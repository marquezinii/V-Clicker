using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace VClicker
{
    /// <summary>A global shortcut: one key plus optional Ctrl/Alt/Shift.</summary>
    internal readonly struct Hotkey : IEquatable<Hotkey>
    {
        public Hotkey(Keys key, bool ctrl = false, bool alt = false, bool shift = false)
        {
            Key = key;
            Ctrl = ctrl;
            Alt = alt;
            Shift = shift;
        }

        public Keys Key { get; }
        public bool Ctrl { get; }
        public bool Alt { get; }
        public bool Shift { get; }

        public bool IsEmpty => Key == Keys.None;

        /// <summary>RegisterHotKey modifier mask (MOD_ALT=1, MOD_CONTROL=2, MOD_SHIFT=4).</summary>
        public uint Modifiers => (Alt ? 1u : 0u) | (Ctrl ? 2u : 0u) | (Shift ? 4u : 0u);

        /// <summary>
        /// Keys that are safe to take over on their own (F-keys, navigation, numpad); anything
        /// else, like a letter, needs Ctrl or Alt so it cannot hijack normal typing.
        /// </summary>
        public bool IsAcceptable =>
            !IsEmpty && Key >= Keys.Back && !IsModifierKey(Key) && (IsStandalone(Key) || Ctrl || Alt);

        public static Hotkey FromKeyData(Keys keyData) => new Hotkey(
            keyData & Keys.KeyCode,
            (keyData & Keys.Control) != 0,
            (keyData & Keys.Alt) != 0,
            (keyData & Keys.Shift) != 0);

        public static bool IsModifierKey(Keys key) =>
            key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu ||
            key == Keys.LControlKey || key == Keys.RControlKey || key == Keys.LShiftKey ||
            key == Keys.RShiftKey || key == Keys.LMenu || key == Keys.RMenu ||
            key == Keys.LWin || key == Keys.RWin;

        private static bool IsStandalone(Keys key) =>
            (key >= Keys.F1 && key <= Keys.F24) ||
            (key >= Keys.NumPad0 && key <= Keys.Divide) ||
            key == Keys.Pause || key == Keys.Scroll || key == Keys.Insert || key == Keys.Delete ||
            key == Keys.Home || key == Keys.End || key == Keys.PageUp || key == Keys.PageDown;

        public static bool TryParse(string text, out Hotkey hotkey)
        {
            hotkey = default(Hotkey);
            if (string.IsNullOrWhiteSpace(text)) return false;

            bool ctrl = false, alt = false, shift = false;
            Keys key = Keys.None;
            string[] parts = text.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (i < parts.Length - 1)
                {
                    switch (part.ToLowerInvariant())
                    {
                        case "ctrl":
                        case "control": ctrl = true; break;
                        case "alt": alt = true; break;
                        case "shift": shift = true; break;
                        default: return false;
                    }
                }
                else if (!TryParseKey(part, out key))
                {
                    return false;
                }
            }

            var parsed = new Hotkey(key, ctrl, alt, shift);
            if (!parsed.IsAcceptable) return false;
            hotkey = parsed;
            return true;
        }

        private static bool TryParseKey(string name, out Keys key)
        {
            key = Keys.None;
            if (name.Length == 1 && name[0] >= '0' && name[0] <= '9')
            {
                key = Keys.D0 + (name[0] - '0');
                return true;
            }
            // Enum.TryParse also accepts numeric strings; only allow names.
            if (name.Length == 0 || !char.IsLetter(name[0])) return false;
            switch (name.ToLowerInvariant())
            {
                case "enter": key = Keys.Enter; return true;
                case "backspace": key = Keys.Back; return true;
                case "pageup": key = Keys.PageUp; return true;
                case "pagedown": key = Keys.PageDown; return true;
            }
            return Enum.TryParse(name, true, out key) && Enum.IsDefined(typeof(Keys), key);
        }

        public override string ToString()
        {
            if (IsEmpty) return string.Empty;
            var parts = new List<string>(4);
            if (Ctrl) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            parts.Add(KeyName(Key));
            return string.Join("+", parts);
        }

        private static string KeyName(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9) return ((char)('0' + (key - Keys.D0))).ToString();
            switch (key)
            {
                case Keys.PageUp: return "PageUp";
                case Keys.PageDown: return "PageDown";
                case Keys.Enter: return "Enter";
                case Keys.Back: return "Backspace";
                default: return key.ToString();
            }
        }

        public bool Equals(Hotkey other) =>
            Key == other.Key && Ctrl == other.Ctrl && Alt == other.Alt && Shift == other.Shift;

        public override bool Equals(object obj) => obj is Hotkey other && Equals(other);

        public override int GetHashCode() => ((int)Key << 3) | (int)Modifiers;
    }

    /// <summary>Owns the RegisterHotKey registrations of one window.</summary>
    internal sealed class HotkeyRegistry
    {
        private readonly IntPtr window;
        private readonly HashSet<int> registered = new HashSet<int>();

        public HotkeyRegistry(IntPtr window)
        {
            this.window = window;
        }

        /// <summary>Registers (or replaces) a hotkey. Returns false if Windows refused it, e.g. another app owns it.</summary>
        public bool Register(int id, Hotkey hotkey)
        {
            Unregister(id);
            if (!hotkey.IsAcceptable) return false;
            if (!Native.RegisterHotKey(window, id, hotkey.Modifiers | Native.MOD_NOREPEAT, (uint)hotkey.Key))
                return false;
            registered.Add(id);
            return true;
        }

        public void Unregister(int id)
        {
            if (registered.Remove(id)) Native.UnregisterHotKey(window, id);
        }

        public void UnregisterAll()
        {
            foreach (int id in registered) Native.UnregisterHotKey(window, id);
            registered.Clear();
        }
    }
}
