using System;
using System.Runtime.InteropServices;

namespace VClicker
{
    /// <summary>Win32 declarations used by V-Clicker.</summary>
    internal static class Native
    {
        internal const int WM_HOTKEY = 0x0312;
        internal const uint MOD_NOREPEAT = 0x4000;

        internal const uint INPUT_MOUSE = 0;
        internal const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        internal const uint MOUSEEVENTF_LEFTUP = 0x0004;
        internal const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        internal const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        internal const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        internal const uint MOUSEEVENTF_MIDDLEUP = 0x0040;

        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT
        {
            public int X, Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL, wParamH;
        }

        // The union must contain every member so INPUT has the exact native size on x86 and x64.
        [StructLayout(LayoutKind.Explicit)]
        internal struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        internal static INPUT Mouse(uint flags)
        {
            var input = new INPUT { type = INPUT_MOUSE };
            input.u.mi.dwFlags = flags;
            return input;
        }

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [DllImport("user32.dll")]
        internal static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        internal static extern bool DestroyIcon(IntPtr icon);

        [DllImport("user32.dll")]
        internal static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool UnregisterHotKey(IntPtr window, int id);

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_POWER_THROTTLING_STATE
        {
            public uint Version, ControlMask, StateMask;
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(IntPtr process, int informationClass,
            ref PROCESS_POWER_THROTTLING_STATE information, uint size);

        /// <summary>
        /// Windows 11 may throttle a process whose windows are minimized or hidden (EcoQoS) and then ignores
        /// timeBeginPeriod, which would make a minimized click loop run several times slower than configured.
        /// Opts this process out; best-effort, older Windows versions simply reject the flags.
        /// </summary>
        internal static void DisablePowerThrottling()
        {
            const uint ExecutionSpeed = 0x1, IgnoreTimerResolution = 0x4;
            var state = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = 1,
                ControlMask = ExecutionSpeed | IgnoreTimerResolution,
                StateMask = 0 // controlled bit set + state bit clear = throttling off
            };
            SetProcessInformation(GetCurrentProcess(), 4 /* ProcessPowerThrottling */, ref state,
                (uint)Marshal.SizeOf(typeof(PROCESS_POWER_THROTTLING_STATE)));
        }

        [DllImport("kernel32.dll")]
        internal static extern bool Beep(uint frequency, uint durationMs);

        [DllImport("winmm.dll")]
        internal static extern uint timeBeginPeriod(uint milliseconds);

        [DllImport("winmm.dll")]
        internal static extern uint timeEndPeriod(uint milliseconds);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        /// <summary>
        /// Dark title bar on Windows 10/11, plus exact caption/text/border colors on Windows 11.
        /// Every call is best-effort: older builds simply ignore the attributes they do not know.
        /// </summary>
        /// <summary>"DarkMode_Explorer" gives a window dark scroll bars (Windows 10 1809+; elsewhere it changes nothing).</summary>
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        internal static extern int SetWindowTheme(IntPtr window, string app, string idList);

        internal static void StyleTitleBar(IntPtr window, int caption, int text, int border)
        {
            int on = 1;
            // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 10 20H1+); 19 = the pre-release value used by 1903/1909.
            if (DwmSetWindowAttribute(window, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(window, 19, ref on, sizeof(int));
            // Windows 11: 35 = DWMWA_CAPTION_COLOR, 36 = DWMWA_TEXT_COLOR, 34 = DWMWA_BORDER_COLOR (COLORREF 0x00BBGGRR).
            DwmSetWindowAttribute(window, 35, ref caption, sizeof(int));
            DwmSetWindowAttribute(window, 36, ref text, sizeof(int));
            DwmSetWindowAttribute(window, 34, ref border, sizeof(int));
        }
    }
}
