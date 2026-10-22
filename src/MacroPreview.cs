using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

namespace VClicker
{
    internal enum ShotProblem { None, Failed, Minimized, Blank }

    /// <summary>
    /// A still of one window's client area, taken without bringing it to the front (a change of focus could close a
    /// menu or change the app). Some apps (games, hardware-accelerated ones) render blank or stale stills: a still that
    /// fails the checks is never shown as trustworthy, and one that passes is still only "the window at that instant".
    /// </summary>
    internal sealed class WindowShot : IDisposable
    {
        private const uint PW_RENDERFULLCONTENT = 2;

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr window);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        /// <summary>The client area (null when <see cref="Problem"/> is not None).</summary>
        public readonly Bitmap Image;
        /// <summary>Where the client area was on the screen (physical pixels).</summary>
        public readonly Rectangle Client;
        public readonly DateTime TakenAt = DateTime.Now;
        public readonly ShotProblem Problem;

        public bool Usable => Problem == ShotProblem.None && Image != null;

        internal WindowShot(Bitmap image, Rectangle client, ShotProblem problem)
        {
            Image = problem == ShotProblem.None ? image : null;
            if (problem != ShotProblem.None) image?.Dispose();
            Client = client;
            Problem = problem;
        }

        /// <summary>Captures the window's client area as the window draws itself. Call on a per-monitor-DPI thread.</summary>
        public static WindowShot Take(IntPtr window)
        {
            Rectangle client = WindowLock.ClientArea(window);
            if (IsIconic(window)) return new WindowShot(null, client, ShotProblem.Minimized);
            if (!GetWindowRect(window, out RECT r) || client.Width <= 0 || client.Height <= 0)
                return new WindowShot(null, client, ShotProblem.Failed);
            var frame = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            if (!frame.Contains(client)) return new WindowShot(null, client, ShotProblem.Failed);
            try
            {
                using (var whole = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppArgb))
                {
                    bool drawn;
                    using (Graphics g = Graphics.FromImage(whole))
                    {
                        IntPtr dc = g.GetHdc();
                        try { drawn = PrintWindow(window, dc, PW_RENDERFULLCONTENT); }
                        finally { g.ReleaseHdc(dc); }
                    }
                    if (!drawn) return new WindowShot(null, client, ShotProblem.Failed);
                    Bitmap image = whole.Clone(new Rectangle(client.X - frame.X, client.Y - frame.Y, client.Width, client.Height), PixelFormat.Format32bppArgb);
                    return new WindowShot(image, client, IsBlank(image) ? ShotProblem.Blank : ShotProblem.None);
                }
            }
            catch (Exception error) when (error is ArgumentException || error is ExternalException || error is OutOfMemoryException)
            {
                return new WindowShot(null, client, ShotProblem.Failed);
            }
        }

        /// <summary>Every pixel the same colour: what a window that cannot be captured (black, or empty) gives.</summary>
        internal static bool IsBlank(Bitmap image)
        {
            BitmapData data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new int[image.Width];
                Marshal.Copy(data.Scan0, row, 0, row.Length);
                int first = row[0];
                for (int y = 0; y < image.Height; y++)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                    foreach (int pixel in row)
                        if (pixel != first) return false;
                }
                return true;
            }
            finally { image.UnlockBits(data); }
        }

        public void Dispose() => Image?.Dispose();
    }

    /// <summary>What a step's look found, in the spec's words (docs/superpowers/specs/2026-10-21-safeclick-design.md).</summary>
    internal enum PreviewState
    {
        /// <summary>Found by UI Automation in the window when the still was taken.</summary>
        Verified,
        /// <summary>Found, or the point lies, in the still (pixels: true for that instant only).</summary>
        InCapture,
        /// <summary>Not there now, and earlier steps exist: "may depend on an earlier action".</summary>
        Unavailable,
        /// <summary>Not there now, and it is the first step.</summary>
        NotFound,
        Ambiguous,
        /// <summary>The point, or the target, is outside the window's client area.</summary>
        Outside,
        /// <summary>The still or the look could not be trusted: never a positive answer.</summary>
        Inconclusive,
        /// <summary>A step without a place: text, keys, shortcuts, waits.</summary>
        NoPosition,
        /// <summary>The app is not open.</summary>
        NoApp
    }

    internal sealed class StepPreview
    {
        public int Index;
        public StepKind Kind;
        public PreviewState State;
        /// <summary>The window whose still the points belong to (Zero = none).</summary>
        public IntPtr Window;
        /// <summary>Points and bounds in the coordinates of that window's still.</summary>
        public Point? At, To;
        public Rectangle? Bounds;
        /// <summary>Candidates found (ambiguous).</summary>
        public int Count;
        /// <summary>"Wait until" steps: whether the condition holds in the look (null = could not tell).</summary>
        public bool? Met;
    }

    internal sealed class MacroPreviewResult : IDisposable
    {
        public readonly List<StepPreview> Steps = new List<StepPreview>();
        public readonly Dictionary<IntPtr, WindowShot> Shots = new Dictionary<IntPtr, WindowShot>();
        public bool WindowFound;
        public List<ImpactfulAction> Impactful = new List<ImpactfulAction>();

        public int Located => Steps.FindAll(s => s.State == PreviewState.Verified || s.State == PreviewState.InCapture).Count;
        public int Positional => Steps.FindAll(s => s.State != PreviewState.NoPosition).Count;
        public int Unverifiable => Steps.FindAll(s => s.State == PreviewState.Unavailable || s.State == PreviewState.Inconclusive).Count;

        public WindowShot ShotOf(StepPreview step) => Shots.TryGetValue(step.Window, out WindowShot shot) ? shot : null;

        public void Dispose()
        {
            foreach (WindowShot shot in Shots.Values) shot.Dispose();
            Shots.Clear();
        }
    }

    /// <summary>
    /// The simulation's look at every step, once, against the current state of the app. Passive: it reads the window
    /// list, UI Automation and stills of the windows; it never sends input, focuses or restores a window. Positions and
    /// targets go through the same code as a run (<see cref="MacroTarget.Resolve"/>, <see cref="SmartTargets.Look"/>,
    /// <see cref="MacroConditions.AverageColor"/>); only the focus and cover checks of a run are left out.
    /// </summary>
    internal static class MacroPreview
    {
        /// <param name="shoot">Takes a still of a window (<see cref="WindowShot.Take"/>; a fake in tests).</param>
        public static MacroPreviewResult Evaluate(Macro macro, IntPtr bound, uint process, Func<IntPtr, WindowShot> shoot, CancellationToken cancel)
        {
            var result = new MacroPreviewResult { Impactful = SafeClick.Impactful(macro) };
            using (MacroTarget.PerMonitorDpi())
            {
                result.WindowFound = bound != IntPtr.Zero && MacroTarget.FindAnchor(bound, null) == bound && MacroTarget.ProcessOf(bound) == process;
                WindowShot ShotOf(IntPtr window)
                {
                    if (!result.Shots.TryGetValue(window, out WindowShot shot)) result.Shots[window] = shot = shoot(window);
                    return shot;
                }
                for (int i = 0; i < macro.Steps.Count; i++)
                {
                    if (cancel.IsCancellationRequested) break;
                    result.Steps.Add(Look(macro.Steps[i], i, bound, process, result.WindowFound, ShotOf, cancel));
                }
            }
            return result;
        }

        private static StepPreview Look(MacroStep step, int index, IntPtr bound, uint process, bool open, Func<IntPtr, WindowShot> shotOf, CancellationToken cancel)
        {
            var preview = new StepPreview { Index = index, Kind = step.Kind };
            bool waitFor = step.Kind == StepKind.WaitFor;
            if (!step.IsMouse && !waitFor)
            {
                preview.State = PreviewState.NoPosition;
                return preview;
            }
            if (!open)
            {
                preview.State = PreviewState.NoApp;
                return preview;
            }
            PreviewState missing = index > 0 ? PreviewState.Unavailable : PreviewState.NotFound;
            IntPtr anchor = MacroTarget.FindAnchor(bound, step.Window);
            if (waitFor && step.Condition == ConditionKind.Window)
            {
                preview.Met = anchor != IntPtr.Zero;
                preview.State = anchor != IntPtr.Zero ? PreviewState.Verified : missing;
                return preview;
            }
            if (anchor == IntPtr.Zero)
            {
                preview.State = missing;
                return preview;
            }
            WindowShot shot = shotOf(anchor);
            preview.Window = anchor;
            Point origin = shot.Client.Location;
            Point InShot(Point screen) => new Point(screen.X - origin.X, screen.Y - origin.Y);

            SmartTarget target = waitFor && step.Condition == ConditionKind.Element ? MacroConditions.ElementTarget(step)
                               : step.Kind == StepKind.Click || step.Kind == StepKind.DoubleClick ? step.Target : null;
            if (target != null)
            {
                bool image = target.Kind == TargetKind.Image;
                if (image && !shot.Usable)
                {
                    preview.State = PreviewState.Inconclusive; // never the screen instead: the Studio may be covering the app
                    return preview;
                }
                TargetLook look = SmartTargets.Look(anchor, process, target, image ? shot.Image : null, shot.Client, cancel);
                preview.Count = look.Count;
                switch (look.Status)
                {
                    case TargetStatus.Found when look.Read == ConditionRead.Met:
                        preview.State = image ? PreviewState.InCapture : PreviewState.Verified;
                        preview.At = InShot(look.Screen);
                        preview.Bounds = new Rectangle(InShot(look.Bounds.Location), look.Bounds.Size);
                        break;
                    case TargetStatus.Ambiguous: preview.State = PreviewState.Ambiguous; break;
                    case TargetStatus.Offscreen: preview.State = PreviewState.Outside; break;
                    case TargetStatus.Inconclusive: preview.State = PreviewState.Inconclusive; break;
                    default: preview.State = missing; break; // not found, or disabled: not available in this state
                }
                if (waitFor) preview.Met = preview.State == PreviewState.Verified ? true : preview.State == PreviewState.Inconclusive ? (bool?)null : false;
                return preview;
            }

            if (waitFor) // colour
            {
                preview.At = step.At;
                if (!shot.Usable)
                {
                    preview.State = PreviewState.Inconclusive;
                    return preview;
                }
                int? rgb = MacroConditions.AverageColor(shot.Image, step.At);
                preview.State = rgb == null ? PreviewState.Outside : PreviewState.InCapture;
                preview.Met = rgb == null ? (bool?)null : MacroConditions.Close(rgb.Value, step.Rgb);
                return preview;
            }

            MacroCheck where = MacroTarget.Resolve(bound, step, out _, out _, out _, out _);
            if (where == MacroCheck.AnchorMissing)
            {
                preview.State = missing; // closed between the two looks
                return preview;
            }
            // A fixed point is a client point: in the capture it is the same point, wherever the window is by now.
            preview.At = step.At;
            if (step.Kind == StepKind.Drag) preview.To = step.To;
            preview.State = where == MacroCheck.PointOutside ? PreviewState.Outside
                          : shot.Usable ? PreviewState.InCapture : PreviewState.Inconclusive;
            return preview;
        }
    }
}
