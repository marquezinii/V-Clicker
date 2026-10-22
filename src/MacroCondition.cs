using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace VClicker
{
    /// <summary>One look at a "wait until" condition.</summary>
    internal enum ConditionRead
    {
        Met,
        /// <summary>Not (yet) satisfied, or the read was inconclusive: the time limit keeps running.</summary>
        NotYet,
        /// <summary>Another app is in front: pause (the time limit stops).</summary>
        Away,
        /// <summary>Another program covers the watched point: pause (the time limit stops).</summary>
        Covered,
        /// <summary>The bound window is gone: stop.</summary>
        Closed
    }

    /// <summary>What the F7 point offers as a condition.</summary>
    internal sealed class ConditionProbe
    {
        public Point At;
        public WindowRef Window;
        /// <summary>Empty = no element with a name there.</summary>
        public string ElementName = string.Empty, ControlType = string.Empty;
        /// <summary>null = the colour could not be read safely.</summary>
        public int? Rgb;
        /// <summary>The dialog under the point (null = the point is in the bound window itself).</summary>
        public WindowRef Dialog;
        /// <summary>Elements like it in the window (1 = unique); the automation id when it was needed to tell them apart.</summary>
        public int ElementCount;
        public string AutomationId = string.Empty;
    }

    /// <summary>
    /// Reads conditions from the screen with what Windows offers: UI Automation for elements, screen pixels for
    /// colours, the window list for dialogs. A read that fails, is late or is ambiguous is never "met".
    /// </summary>
    internal static class MacroConditions
    {
        public const int ColorTolerance = 24;
        private const int Half = 2; // 5x5 pixels around the point

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr window);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr window, IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr dc, int x, int y);

        /// <summary>
        /// The F7 look, off the UI thread: everything a condition at this point could watch, read while the app is
        /// still in front.
        /// </summary>
        /// <param name="root">The top-level window under the point, already checked to be the bound window's.</param>
        public static ConditionProbe Capture(IntPtr bound, IntPtr root, Point screen)
        {
            if (!MacroTarget.Locate(bound, root, screen, out WindowRef window, out Point at)) return null;
            var probe = new ConditionProbe { At = at, Window = window, Dialog = window != null && !window.IsMenu ? window : null };
            probe.Rgb = ReadColor(root, screen); // first: the colour as the user saw it when pressing F7
            ElementProbe element = SmartTargets.ProbeElement(root, screen);
            if (element != null)
            {
                probe.ControlType = element.ControlType;
                probe.ElementName = element.Name;
                probe.ElementCount = element.Count;
                probe.AutomationId = element.AutomationId;
            }
            return probe;
        }

        /// <summary>One look at the condition, right now (on the player thread).</summary>
        /// <param name="cancel">The run's stop: a slow read is abandoned at once.</param>
        public static ConditionRead Read(IntPtr bound, uint process, MacroStep step, CancellationToken cancel)
        {
            if (MacroTarget.ProcessOf(bound) != process) return ConditionRead.Closed;
            if (!MacroTarget.Owns(bound, WindowLock.Foreground())) return ConditionRead.Away;
            if (step.Condition == ConditionKind.Window)
                return MacroTarget.FindAnchor(bound, step.Window) != IntPtr.Zero ? ConditionRead.Met : ConditionRead.NotYet;

            // The point's own window (the app's dialog) not open yet, or the point outside it: "not yet".
            IntPtr anchor = MacroTarget.FindAnchor(bound, step.Window);
            if (anchor == IntPtr.Zero) return ConditionRead.NotYet;
            if (step.Condition == ConditionKind.Element) return ReadElement(anchor, process, step, cancel);
            Rectangle client = WindowLock.ClientArea(anchor);
            var screen = new Point(client.X + step.At.X, client.Y + step.At.Y);
            var area = new Rectangle(screen.X - Half, screen.Y - Half, 2 * Half + 1, 2 * Half + 1);
            if (!client.Contains(area)) return ConditionRead.NotYet;
            ConditionRead cover = Cover(anchor, process, area);
            if (cover != ConditionRead.Met) return cover;
            int? rgb = ReadColor(anchor, screen);
            if (rgb == null || Cover(anchor, process, area) != ConditionRead.Met) return ConditionRead.NotYet; // covered meanwhile
            return Close(rgb.Value, step.Rgb) ? ConditionRead.Met : ConditionRead.NotYet;
        }

        /// <summary>
        /// Element: exactly one available element with this type and name (and id) anywhere in the window, through
        /// the same search as a click target (Smart Target), and not covered at its centre.
        /// </summary>
        private static ConditionRead ReadElement(IntPtr anchor, uint process, MacroStep step, CancellationToken cancel)
        {
            TargetLook found = SmartTargets.Look(anchor, process, ElementTarget(step), null, Rectangle.Empty, cancel);
            if (found.Read != ConditionRead.Met) return ConditionRead.NotYet;
            var area = new Rectangle(found.Screen.X - Half, found.Screen.Y - Half, 2 * Half + 1, 2 * Half + 1);
            if (!WindowLock.ClientArea(anchor).Contains(area)) return ConditionRead.NotYet;
            return Cover(anchor, process, area);
        }

        /// <summary>An element condition as the target the search looks for.</summary>
        internal static SmartTarget ElementTarget(MacroStep step) => new SmartTarget
        {
            Kind = TargetKind.Element, ControlType = step.ControlType, Name = step.Text, AutomationId = step.AutomationId
        };

        /// <summary>
        /// Met when the window under the area's centre and corners is <paramref name="anchor"/>. Something of the
        /// app itself on top is "not yet"; another program on top pauses.
        /// </summary>
        internal static ConditionRead Cover(IntPtr anchor, uint process, Rectangle area)
        {
            foreach (Point p in Probes(area))
            {
                IntPtr root = MacroTarget.RootAt(p);
                if (root == anchor) continue;
                return MacroTarget.ProcessOf(root) == process ? ConditionRead.NotYet : ConditionRead.Covered;
            }
            return ConditionRead.Met;
        }

        private static Point[] Probes(Rectangle area) => new[]
        {
            new Point(area.X + Half, area.Y + Half), new Point(area.Left, area.Top), new Point(area.Right - 1, area.Top),
            new Point(area.Left, area.Bottom - 1), new Point(area.Right - 1, area.Bottom - 1)
        };

        internal static bool Close(int a, int b) =>
            Math.Abs(((a >> 16) & 0xFF) - ((b >> 16) & 0xFF)) <= ColorTolerance &&
            Math.Abs(((a >> 8) & 0xFF) - ((b >> 8) & 0xFF)) <= ColorTolerance &&
            Math.Abs((a & 0xFF) - (b & 0xFF)) <= ColorTolerance;

        /// <summary>The average colour of 5x5 pixels around the point (0xRRGGBB); null if it cannot be read safely.</summary>
        private static int? ReadColor(IntPtr anchor, Point screen)
        {
            var area = new Rectangle(screen.X - Half, screen.Y - Half, 2 * Half + 1, 2 * Half + 1);
            foreach (Point p in Probes(area))
                if (MacroTarget.RootAt(p) != anchor) return null; // something else is on top of part of it
            IntPtr dc = GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return null;
            try
            {
                return Average(area, (x, y) =>
                {
                    uint pixel = GetPixel(dc, x, y); // 0x00BBGGRR
                    if (pixel == 0xFFFFFFFF) return null; // CLR_INVALID: outside the desktop or not readable
                    return (int)(((pixel & 0xFF) << 16) | (pixel & 0xFF00) | ((pixel >> 16) & 0xFF));
                });
            }
            finally { ReleaseDC(IntPtr.Zero, dc); }
        }

        /// <summary>The same 5x5 average in a capture of the client area (the simulation); null when it leaves the capture.</summary>
        internal static int? AverageColor(Bitmap shot, Point at)
        {
            var area = new Rectangle(at.X - Half, at.Y - Half, 2 * Half + 1, 2 * Half + 1);
            if (!new Rectangle(Point.Empty, shot.Size).Contains(area)) return null;
            return Average(area, (x, y) => shot.GetPixel(x, y).ToArgb() & 0xFFFFFF);
        }

        /// <param name="rgbAt">One pixel as 0xRRGGBB; null = not readable.</param>
        private static int? Average(Rectangle area, Func<int, int, int?> rgbAt)
        {
            int r = 0, g = 0, b = 0;
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                {
                    int? rgb = rgbAt(x, y);
                    if (rgb == null) return null;
                    r += (rgb.Value >> 16) & 0xFF;
                    g += (rgb.Value >> 8) & 0xFF;
                    b += rgb.Value & 0xFF;
                }
            int n = area.Width * area.Height;
            return ((r / n) << 16) | ((g / n) << 8) | (b / n);
        }

        /// <summary>One kind of line break, so a multi-line name survives the file (which keeps only \n).</summary>
        internal static string Normalize(string name) => name.Replace("\r\n", "\n").Replace('\r', '\n');

        /// <summary>"Button" for ControlType.Button: what the file stores.</summary>
        internal static string TypeName(ControlType type)
        {
            string name = type.ProgrammaticName;
            int dot = name.LastIndexOf('.');
            return dot >= 0 ? name.Substring(dot + 1) : name;
        }

        /// <summary>
        /// Runs a slow screen read (UI Automation, a screenshot) off the calling thread with a time limit. A late
        /// answer is thrown away (it can never move a macro on); a failure counts as no answer. Only one read runs at
        /// a time in the whole program: while an abandoned one is still running (an app that hangs), new reads get
        /// the fallback at once instead of piling up threads.
        /// </summary>
        internal static T WithTimeout<T>(Func<T> read, T fallback, int timeoutMs, CancellationToken cancel)
        {
            Task<T> task;
            lock (gate)
            {
                if (inFlight != null && !inFlight.IsCompleted) return fallback;
                task = Task.Run(() =>
                {
                    try
                    {
                        using (MacroTarget.PerMonitorDpi()) return read(); // the points are physical pixels
                    }
                    catch (Exception) { return fallback; } // the element went away, or the app does not answer
                });
                inFlight = task;
            }
            try
            {
                return task.Wait(timeoutMs, cancel) ? task.Result : fallback;
            }
            catch (OperationCanceledException)
            {
                return fallback; // stopped: the caller sees the stop and never uses this
            }
        }

        private static readonly object gate = new object();
        private static Task inFlight;
    }
}
