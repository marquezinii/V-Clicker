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
    }

    /// <summary>
    /// Reads conditions from the screen with what Windows offers: UI Automation for elements, screen pixels for
    /// colours, the window list for dialogs. A read that fails, is late or is ambiguous is never "met".
    /// </summary>
    internal static class MacroConditions
    {
        public const int ColorTolerance = 24;
        public const int ReadTimeoutMs = 500;
        /// <summary>F7 has no hurry, and the first UI Automation call in a session can take over half a second.</summary>
        public const int CaptureTimeoutMs = 3000;
        private const int Half = 2; // 5x5 pixels around the point

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr window);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr window, IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr dc, int x, int y);

        // Controls a person waits for; plain text inside them leads up to them.
        private static readonly ControlType[] Interactive =
        {
            ControlType.Button, ControlType.Hyperlink, ControlType.Edit, ControlType.CheckBox, ControlType.RadioButton,
            ControlType.ComboBox, ControlType.MenuItem, ControlType.TabItem, ControlType.ListItem, ControlType.SplitButton
        };

        /// <summary>The F7 look: everything a condition at this point could watch. null = not the bound window's.</summary>
        public static ConditionProbe Capture(IntPtr bound, Point screen)
        {
            IntPtr root = MacroTarget.RootAt(screen);
            if (!MacroTarget.Locate(bound, root, screen, out WindowRef window, out Point at)) return null;
            var probe = new ConditionProbe { At = at, Window = window, Dialog = window != null && !window.IsMenu ? window : null };
            // ponytail: blocks the Studio up to 3 s if the app does not answer UI Automation; F7 is a one-off, user-started action.
            Tuple<string, string> element = WithTimeout(() => ElementAt(screen, MacroTarget.ProcessOf(bound)), null, CaptureTimeoutMs, CancellationToken.None);
            if (element != null)
            {
                probe.ControlType = element.Item1;
                probe.ElementName = element.Item2;
            }
            probe.Rgb = ReadColor(root, screen);
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
            Rectangle client = WindowLock.ClientArea(anchor);
            var screen = new Point(client.X + step.At.X, client.Y + step.At.Y);
            var area = new Rectangle(screen.X - Half, screen.Y - Half, 2 * Half + 1, 2 * Half + 1);
            if (!client.Contains(area)) return ConditionRead.NotYet;
            ConditionRead cover = Cover(anchor, process, area);
            if (cover != ConditionRead.Met) return cover;

            if (step.Condition == ConditionKind.Element)
            {
                // Only one read at a time: against an app that hangs, polls do not pile up threads.
                if (inFlight != null && !inFlight.IsCompleted) return ConditionRead.NotYet;
                Tuple<string, string> element = WithTimeout(() => AvailableElementAt(screen, process), null, ReadTimeoutMs, cancel, track: true);
                return element != null && element.Item1 == step.ControlType && element.Item2 == step.Text
                    ? ConditionRead.Met : ConditionRead.NotYet;
            }
            int? rgb = ReadColor(anchor, screen);
            if (rgb == null || Cover(anchor, process, area) != ConditionRead.Met) return ConditionRead.NotYet; // covered meanwhile
            return Close(rgb.Value, step.Rgb) ? ConditionRead.Met : ConditionRead.NotYet;
        }

        /// <summary>
        /// Met when the window under the area's centre and corners is <paramref name="anchor"/>. Something of the
        /// app itself on top is "not yet"; another program on top pauses.
        /// </summary>
        private static ConditionRead Cover(IntPtr anchor, uint process, Rectangle area)
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
                int r = 0, g = 0, b = 0;
                for (int y = area.Top; y < area.Bottom; y++)
                    for (int x = area.Left; x < area.Right; x++)
                    {
                        uint pixel = GetPixel(dc, x, y);
                        if (pixel == 0xFFFFFFFF) return null; // CLR_INVALID: outside the desktop or not readable
                        r += (int)(pixel & 0xFF);
                        g += (int)((pixel >> 8) & 0xFF);
                        b += (int)((pixel >> 16) & 0xFF);
                    }
                int n = area.Width * area.Height;
                return ((r / n) << 16) | ((g / n) << 8) | (b / n);
            }
            finally { ReleaseDC(IntPtr.Zero, dc); }
        }

        /// <summary>
        /// The element a person means at the point: the interactive control (the button, not the text inside it,
        /// up to 3 levels up), else the first element with a name. Only elements of <paramref name="process"/>.
        /// (control type, name), or null.
        /// </summary>
        private static Tuple<string, string> ElementAt(Point screen, uint process) => Resolve(screen, process, false);

        /// <summary>Like <see cref="ElementAt"/>, and only if it is enabled and on the screen.</summary>
        private static Tuple<string, string> AvailableElementAt(Point screen, uint process) => Resolve(screen, process, true);

        private static Tuple<string, string> Resolve(Point screen, uint process, bool mustBeAvailable)
        {
            AutomationElement element = AutomationElement.FromPoint(new System.Windows.Point(screen.X, screen.Y));
            AutomationElement named = null, chosen = null;
            for (int depth = 0; depth <= 3 && element != null; depth++)
            {
                AutomationElement.AutomationElementInformation info = element.Current;
                if (info.ProcessId != process) return null; // never someone else's element
                if (info.Name.Length > 0)
                {
                    if (Array.IndexOf(Interactive, info.ControlType) >= 0)
                    {
                        chosen = element;
                        break;
                    }
                    if (named == null) named = element;
                }
                element = TreeWalker.ControlViewWalker.GetParent(element);
            }
            chosen = chosen ?? named;
            if (chosen == null) return null;
            AutomationElement.AutomationElementInformation current = chosen.Current;
            if (mustBeAvailable && (!current.IsEnabled || current.IsOffscreen)) return null;
            return Tuple.Create(TypeName(current.ControlType), Normalize(current.Name));
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
        /// Runs a UI Automation read with a time limit. A late answer is thrown away (it can never move a macro on);
        /// a failure counts as no answer.
        /// </summary>
        private static T WithTimeout<T>(Func<T> read, T fallback, int timeoutMs, CancellationToken cancel, bool track = false) where T : class
        {
            Task<T> task = Task.Run(() =>
            {
                try
                {
                    using (MacroTarget.PerMonitorDpi()) return read(); // the points are physical pixels
                }
                catch (Exception) { return fallback; } // the element went away, or the app does not answer
            });
            if (track) inFlight = task;
            try
            {
                return task.Wait(timeoutMs, cancel) ? task.Result : fallback;
            }
            catch (OperationCanceledException)
            {
                return fallback; // stopped: the caller sees the stop and never uses this
            }
        }

        private static Task inFlight;
    }
}
