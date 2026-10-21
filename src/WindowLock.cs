using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace VClicker
{
    /// <summary>A top-level window of another app, as listed for Window Lock.</summary>
    internal sealed class AppWindow
    {
        public IntPtr Handle;
        public uint ProcessId;
        public string ExePath = string.Empty;
        /// <summary>File name only ("RobloxPlayerBeta.exe"): what is saved.</summary>
        public string ExeName = string.Empty;
        /// <summary>Friendly app name ("Roblox"): the file description, else the exe name without extension.</summary>
        public string DisplayName = string.Empty;
        public string Title = string.Empty;
        /// <summary>
        /// V-Clicker may not inspect this process (it runs as administrator, or is protected); the name comes from
        /// the process list instead. Windows also drops synthetic clicks sent to such a window from a normal process.
        /// </summary>
        public bool Restricted;
    }

    /// <summary>
    /// Window Lock's view of the desktop, with plain Win32 calls only: which app windows exist, which one is in
    /// front, and whether a click may go to the locked one. No hooks, no injection, no screen capture.
    /// </summary>
    internal static class WindowLock
    {
        private const int GWL_EXSTYLE = -20;
        private const long WS_EX_TOOLWINDOW = 0x80;
        private const uint GW_OWNER = 4;
        private const int DWMWA_CLOAKED = 14;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        private const int SW_RESTORE = 9;
        private const uint GA_ROOT = 2;

        private delegate bool EnumWindowsProc(IntPtr window, IntPtr data);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr window, uint command);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr window, StringBuilder text, int max);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr window);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLong32(IntPtr window, int index);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr window, int command);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr window, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr window, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr window, ref Native.POINT point);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref int size);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        public static IntPtr Foreground() => GetForegroundWindow();

        /// <summary>
        /// The visible window that owns <paramref name="window"/> (a game's dialog belongs to the game window).
        /// Only visible owners count: many programs own their windows from a hidden one (to stay off the taskbar),
        /// and a run bound to a hidden window would never see it in front.
        /// </summary>
        public static IntPtr RootOwner(IntPtr window)
        {
            if (window == IntPtr.Zero) return IntPtr.Zero;
            IntPtr root = GetAncestor(window, GA_ROOT);
            IntPtr current = root == IntPtr.Zero ? window : root;
            for (int depth = 0; depth < 32; depth++) // owner chains are short; never loop forever on a broken one
            {
                IntPtr owner = GetWindow(current, GW_OWNER);
                if (owner == IntPtr.Zero || !IsWindowVisible(owner)) break;
                current = owner;
            }
            return current;
        }

        /// <summary>
        /// The windows a person would call "an app": visible, titled, not owned by another window, not a tool
        /// window, not hidden by the shell ("cloaked"), not V-Clicker, not a Store app host.
        /// </summary>
        /// <param name="exeName">Only windows of this executable (file name, case-insensitive); null = all.</param>
        public static List<AppWindow> List(string exeName = null)
        {
            var found = new List<AppWindow>();
            uint self;
            using (Process current = Process.GetCurrentProcess()) self = (uint)current.Id;
            EnumWindows((window, data) =>
            {
                if (!IsAppWindow(window)) return true;
                GetWindowThreadProcessId(window, out uint processId);
                if (processId == self) return true;
                // The exe filter runs before the (slower) file description is read.
                AppWindow app = Describe(window, processId, exeName);
                if (app != null && !app.ExeName.Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
                    found.Add(app);
                return true;
            }, IntPtr.Zero);
            return found;
        }

        /// <summary>A window a person would call "an app" (see <see cref="List"/>), as opposed to a dialog, menu or popup.</summary>
        internal static bool IsAppWindow(IntPtr window)
        {
            if (!IsWindowVisible(window) || GetWindowTextLength(window) == 0) return false;
            if (GetWindow(window, GW_OWNER) != IntPtr.Zero) return false;
            long exStyle = (IntPtr.Size == 8 ? GetWindowLongPtr64(window, GWL_EXSTYLE) : GetWindowLong32(window, GWL_EXSTYLE)).ToInt64();
            if ((exStyle & WS_EX_TOOLWINDOW) != 0) return false;
            return DwmGetWindowAttribute(window, DWMWA_CLOAKED, out int cloaked, sizeof(int)) != 0 || cloaked == 0;
        }

        /// <summary>Describes any top-level window; null if it is gone or its process cannot be identified.</summary>
        public static AppWindow FromHandle(IntPtr window)
        {
            if (window == IntPtr.Zero || !IsWindow(window)) return null;
            GetWindowThreadProcessId(window, out uint processId);
            return Describe(window, processId, null);
        }

        private static AppWindow Describe(IntPtr window, uint processId, string exeFilter)
        {
            string path = ProcessPath(processId);
            bool restricted = path == null;
            string exe = restricted ? ProcessExeName(processId) : Path.GetFileName(path);
            if (!ClickSettings.IsExeFileName(exe)) return null; // gone, or not something Window Lock can save
            if (exeFilter != null && !exe.Equals(exeFilter, StringComparison.OrdinalIgnoreCase)) return null;

            var title = new StringBuilder(GetWindowTextLength(window) + 1);
            GetWindowText(window, title, title.Capacity);
            string name = null;
            if (!restricted)
            {
                try { name = FileVersionInfo.GetVersionInfo(path).FileDescription; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (ArgumentException) { }
            }
            if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(exe);
            return new AppWindow
            {
                Handle = window, ProcessId = processId, ExePath = path ?? string.Empty, ExeName = exe,
                DisplayName = name.Trim(), Title = title.ToString(), Restricted = restricted
            };
        }

        /// <summary>The exe name from the process list, which needs no access to the process itself.</summary>
        private static string ProcessExeName(uint processId)
        {
            try
            {
                using (Process process = Process.GetProcessById((int)processId)) return process.ProcessName + ".exe";
            }
            catch (ArgumentException) { return null; }          // exited
            catch (InvalidOperationException) { return null; }  // exited while reading
        }

        private static string ProcessPath(uint processId)
        {
            IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (process == IntPtr.Zero) return null;
            try
            {
                var path = new StringBuilder(32767); // long-path aware: the documented maximum
                int size = path.Capacity;
                return QueryFullProcessImageName(process, 0, path, ref size) ? path.ToString(0, size) : null;
            }
            finally { CloseHandle(process); }
        }

        /// <summary>
        /// The check run right before every click (on the engine thread; these calls are thread-safe and cheap).
        /// A handle whose process changed counts as closed: Windows reuses handles of destroyed windows.
        /// </summary>
        public static LockStatus Check(IntPtr window, uint processId, Point? at)
        {
            if (window == IntPtr.Zero || !IsWindow(window)) return LockStatus.Closed;
            GetWindowThreadProcessId(window, out uint owner);
            if (owner != processId) return LockStatus.Closed;
            if (GetForegroundWindow() != window) return LockStatus.NotFocused;
            if (at.HasValue && !ClientArea(window).Contains(at.Value)) return LockStatus.PointOutside;
            return LockStatus.Ok;
        }

        /// <summary>The window's usable area (no title bar or borders) in screen coordinates.</summary>
        internal static Rectangle ClientArea(IntPtr window)
        {
            if (!GetClientRect(window, out RECT client)) return Rectangle.Empty;
            var origin = new Native.POINT();
            if (!ClientToScreen(window, ref origin)) return Rectangle.Empty;
            return new Rectangle(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
        }

        /// <summary>
        /// Asks Windows to bring <paramref name="window"/> to the front and confirms it really got there.
        /// Only works while V-Clicker itself is the foreground app (the user just clicked its button).
        /// </summary>
        public static bool BringToFront(IntPtr window)
        {
            if (!IsWindow(window)) return false;
            // Async: a hung target must not freeze V-Clicker's window.
            if (IsIconic(window)) ShowWindowAsync(window, SW_RESTORE);
            // ponytail: short blocking wait on the UI thread (at most ~0.5 s, only when the button is used).
            for (int i = 0; i < 25; i++)
            {
                SetForegroundWindow(window);
                if (GetForegroundWindow() == window) return true;
                Thread.Sleep(20);
            }
            return false;
        }
    }
}
