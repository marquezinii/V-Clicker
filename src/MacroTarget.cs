using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace VClicker
{
    /// <summary>What the guard says about the next macro step.</summary>
    internal enum MacroCheck
    {
        Ok,
        /// <summary>Another window is in front: pause.</summary>
        NotFocused,
        /// <summary>The step's dialog or menu is not open (yet): pause.</summary>
        AnchorMissing,
        /// <summary>The step's point is outside its window's client area: pause.</summary>
        PointOutside,
        /// <summary>Something else (a notification, V-Clicker itself) covers the step's point: pause.</summary>
        Covered,
        /// <summary>The bound window is gone: stop.</summary>
        Closed
    }

    internal struct MacroCheckResult
    {
        public MacroCheck Status;
        /// <summary>Ok mouse steps: the step's point on the screen.</summary>
        public Point Screen;
        /// <summary>Ok mouse steps: the client-area origin of the step's window (for a drag's end).</summary>
        public Point Origin;
    }

    /// <summary>
    /// The windows a macro may act on: the bound window (one instance, not the whole app) and the dialogs, menus and
    /// popups that belong to it. Plain Win32 calls, cheap enough to run before every input.
    /// </summary>
    internal static class MacroTarget
    {
        private const uint GA_ROOT = 2;

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr window, uint flags);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Native.POINT point);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, StringBuilder name, int max);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr window, StringBuilder text, int max);

        private delegate bool EnumWindowsProc(IntPtr window, IntPtr data);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);

        [DllImport("user32.dll")]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

        public static uint ProcessOf(IntPtr window)
        {
            GetWindowThreadProcessId(window, out uint process);
            return process;
        }

        /// <summary>The top-level window that contains <paramref name="window"/>.</summary>
        public static IntPtr Root(IntPtr window)
        {
            if (window == IntPtr.Zero) return IntPtr.Zero;
            IntPtr root = GetAncestor(window, GA_ROOT);
            return root == IntPtr.Zero ? window : root;
        }

        /// <summary>
        /// Whether <paramref name="window"/> belongs to the bound window: it is the bound window, a window it owns
        /// (a dialog), or a popup of the same process (a menu, a drop-down list) while the bound window is in front.
        /// Another app-style window of the same program (a second Chrome window) does not.
        /// </summary>
        public static bool Owns(IntPtr bound, IntPtr window)
        {
            if (bound == IntPtr.Zero || window == IntPtr.Zero) return false;
            window = Root(window);
            if (window == bound) return true;
            if (WindowLock.RootOwner(window) == bound) return true;
            if (ProcessOf(window) != ProcessOf(bound) || WindowLock.IsAppWindow(window)) return false;
            IntPtr front = WindowLock.Foreground();
            return front == window || front == bound || WindowLock.RootOwner(front) == bound;
        }

        /// <summary>
        /// The window under a screen point, as a step would refer to it: false when the point is not on the bound
        /// window or one of its dialogs and menus.
        /// </summary>
        public static bool Locate(IntPtr bound, Point screen, out WindowRef window, out Point at) =>
            Locate(bound, RootAt(screen), screen, out window, out at);

        /// <param name="root">The top-level window that was under <paramref name="screen"/> (see <see cref="RootAt"/>).</param>
        public static bool Locate(IntPtr bound, IntPtr root, Point screen, out WindowRef window, out Point at)
        {
            window = null;
            at = Point.Empty;
            if (!Owns(bound, root)) return false;
            window = Describe(bound, root);
            Rectangle client = WindowLock.ClientArea(root);
            at = new Point(screen.X - client.X, screen.Y - client.Y);
            return true;
        }

        /// <summary>The top-level window under a screen point.</summary>
        public static IntPtr RootAt(Point screen) => Root(WindowFromPoint(new Native.POINT { X = screen.X, Y = screen.Y }));

        /// <summary>How a step refers to <paramref name="root"/>: null for the bound window, else class and title.</summary>
        public static WindowRef Describe(IntPtr bound, IntPtr root)
        {
            if (root == bound) return null;
            var reference = new WindowRef { Class = ClassName(root) };
            if (!reference.IsMenu) reference.Title = Title(root);
            return reference;
        }

        /// <summary>The window a step acts on now: the bound window, or the frontmost matching dialog or menu of it.</summary>
        public static IntPtr FindAnchor(IntPtr bound, WindowRef reference)
        {
            if (reference == null) return IsWindow(bound) ? bound : IntPtr.Zero;
            IntPtr found = IntPtr.Zero;
            EnumWindows((window, data) => // top of the z-order first
            {
                if (IsWindowVisible(window) && window != bound && reference.Matches(Describe(bound, window)) && Owns(bound, window))
                {
                    found = window;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        /// <summary>
        /// The check run right before every input of a macro (on the player thread). A handle whose process changed
        /// counts as closed: Windows reuses handles of destroyed windows.
        /// </summary>
        public static MacroCheckResult Check(IntPtr bound, uint processId, MacroStep step)
        {
            var result = new MacroCheckResult();
            if (!IsWindow(bound) || ProcessOf(bound) != processId)
            {
                result.Status = MacroCheck.Closed;
                return result;
            }
            if (step.IsMouse)
            {
                IntPtr anchor = FindAnchor(bound, step.Window);
                if (anchor == IntPtr.Zero)
                {
                    result.Status = MacroCheck.AnchorMissing;
                    return result;
                }
                Rectangle client = WindowLock.ClientArea(anchor);
                var screen = new Point(client.X + step.At.X, client.Y + step.At.Y);
                // A drag must also be released inside the same window: never drop something on another app.
                var end = new Point(client.X + step.To.X, client.Y + step.To.Y);
                bool drag = step.Kind == StepKind.Drag;
                if (!client.Contains(screen) || (drag && !client.Contains(end)))
                {
                    result.Status = MacroCheck.PointOutside;
                    return result;
                }
                if (!Owns(bound, WindowLock.Foreground()))
                {
                    result.Status = MacroCheck.NotFocused;
                    return result;
                }
                // ponytail: WindowFromPoint may wait on a hung target; the run then stalls until it answers or F8.
                if (Root(WindowFromPoint(new Native.POINT { X = screen.X, Y = screen.Y })) != anchor ||
                    (drag && Root(WindowFromPoint(new Native.POINT { X = end.X, Y = end.Y })) != anchor))
                {
                    result.Status = MacroCheck.Covered;
                    return result;
                }
                result.Screen = screen;
                result.Origin = client.Location;
                return result;
            }
            if (!Owns(bound, WindowLock.Foreground())) result.Status = MacroCheck.NotFocused;
            return result;
        }

        /// <summary>
        /// The process is system-DPI aware; macro threads switch to per-monitor awareness so every coordinate they
        /// read or send is a physical pixel, on every monitor. Best-effort: Windows before 10 1703 keeps the default.
        /// </summary>
        public static IDisposable PerMonitorDpi() => new DpiScope();

        private sealed class DpiScope : IDisposable
        {
            private readonly IntPtr previous;

            public DpiScope()
            {
                try { previous = SetThreadDpiAwarenessContext(new IntPtr(-4)); } // PER_MONITOR_AWARE_V2
                catch (EntryPointNotFoundException) { }
            }

            public void Dispose()
            {
                if (previous == IntPtr.Zero) return;
                try { SetThreadDpiAwarenessContext(previous); }
                catch (EntryPointNotFoundException) { }
            }
        }

        public static bool IsBrowser(string exe)
        {
            switch ((exe ?? string.Empty).ToLowerInvariant())
            {
                case "chrome.exe":
                case "msedge.exe":
                case "firefox.exe":
                case "brave.exe":
                case "opera.exe":
                case "vivaldi.exe":
                    return true;
                default:
                    return false;
            }
        }

        public static string ClassName(IntPtr window)
        {
            var name = new StringBuilder(256);
            int length = GetClassName(window, name, name.Capacity);
            return length > 0 ? name.ToString(0, length) : string.Empty;
        }

        private static string Title(IntPtr window)
        {
            var text = new StringBuilder(512);
            int length = GetWindowText(window, text, text.Capacity);
            return length > 0 ? text.ToString(0, length) : string.Empty;
        }
    }
}
