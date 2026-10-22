using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Threading;
using System.Windows.Automation;

namespace VClicker
{
    /// <summary>Why a target was or was not found (the stop message says which).</summary>
    internal enum TargetStatus
    {
        Found,
        NotFound,
        /// <summary>More than one candidate: never a guess.</summary>
        Ambiguous,
        Disabled,
        Offscreen,
        /// <summary>The search failed or was too slow.</summary>
        Inconclusive,
        /// <summary>Found, but still moving, or the last look before the click did not see it under the point.</summary>
        Unconfirmed
    }

    /// <summary>One look for a click target.</summary>
    internal sealed class TargetLook
    {
        /// <summary>Met = found (<see cref="Screen"/> is where to click); Away / Covered / Closed as for conditions.</summary>
        public ConditionRead Read = ConditionRead.NotYet;
        public TargetStatus Status = TargetStatus.NotFound;
        /// <summary>How many candidates matched (2 or more = ambiguous).</summary>
        public int Count;
        public Point Screen;
        public Rectangle Bounds;
        /// <summary>The UI Automation element found (for the hit test right before the click).</summary>
        public object Element;
    }

    /// <summary>What F7 found as an element at a point.</summary>
    internal sealed class ElementProbe
    {
        public string ControlType = string.Empty, Name = string.Empty;
        /// <summary>Kept only when type and name alone are not unique and the id makes them so.</summary>
        public string AutomationId = string.Empty;
        /// <summary>Elements like it in the window (1 = unique; 0 = the pointed element could not be confirmed in the window).</summary>
        public int Count;
        /// <summary>The element on the screen.</summary>
        public Rectangle Bounds;
    }

    /// <summary>What F7 found for a click target at a point: the element there, and the image regions around it.</summary>
    internal sealed class TargetProbe : IDisposable
    {
        /// <summary>The point, in the client area of the step's window.</summary>
        public Point At;
        /// <summary>null = no element with a name there.</summary>
        public ElementProbe Element;
        /// <summary>The element as it looks on the screen (null = none).</summary>
        public Bitmap ElementPreview;
        public List<ImageOption> Images = new List<ImageOption>();

        public bool ElementUsable => Element != null && Element.Name.Length > 0 && Element.Count == 1;
        public ImageOption FirstImage => Images.Find(o => o.Problem == ImageProblem.None);

        public void Dispose()
        {
            ElementPreview?.Dispose();
            foreach (ImageOption option in Images) option.Crop?.Dispose();
        }
    }

    /// <summary>
    /// Finds click targets on the screen (see docs/superpowers/specs/2026-10-23-smart-target-design.md): UI elements
    /// by control type and name in the step window's UI Automation tree, images by <see cref="ImageTarget"/> in a
    /// screenshot of its client area. Every slow read goes through <see cref="MacroConditions.WithTimeout"/>: off the
    /// calling thread, one at a time, a late answer thrown away. A look that is not certain is never "found".
    /// </summary>
    internal static class SmartTargets
    {
        /// <summary>One element search; measured at 350-600 ms on Chrome pages of about 10,000 elements.</summary>
        public const int SearchTimeoutMs = 2000;
        /// <summary>F7 has no hurry: the first UI Automation call can take over half a second, and Chrome needs about 1 s
        /// to build its tree after the first request (the uniqueness search retries meanwhile).</summary>
        public const int ProbeTimeoutMs = 6000;
        private const int ProbeTries = 4, ProbeRetryMs = 300;

        // Controls a person clicks or waits for; plain text inside them leads up to them.
        private static readonly ControlType[] Interactive =
        {
            ControlType.Button, ControlType.Hyperlink, ControlType.Edit, ControlType.CheckBox, ControlType.RadioButton,
            ControlType.ComboBox, ControlType.MenuItem, ControlType.TabItem, ControlType.ListItem, ControlType.SplitButton
        };
        /// <summary>The final check right before a click (one hit test, or one small screenshot).</summary>
        public const int ConfirmTimeoutMs = 1000;
        private const int CoverGrid = 16;

        /// <summary>One look for the step's target, right now (on the player thread).</summary>
        public static TargetLook Find(IntPtr bound, uint process, MacroStep step, CancellationToken cancel)
        {
            var look = new TargetLook();
            if (MacroTarget.ProcessOf(bound) != process) { look.Read = ConditionRead.Closed; return look; }
            if (!MacroTarget.Owns(bound, WindowLock.Foreground())) { look.Read = ConditionRead.Away; return look; }
            IntPtr anchor = MacroTarget.FindAnchor(bound, step.Window);
            if (anchor == IntPtr.Zero) return look; // the step's dialog is not open (yet)
            return Look(anchor, process, step.Target, null, Rectangle.Empty, cancel);
        }

        /// <summary>
        /// The search itself, shared by a run and the simulation (no focus check): the element in the window's UI
        /// Automation tree, or the image in <paramref name="shot"/>, a capture of the client area <paramref name="client"/>
        /// (null: a screenshot of the window's client area now, as a run does).
        /// </summary>
        internal static TargetLook Look(IntPtr anchor, uint process, SmartTarget target, Bitmap shot, Rectangle client, CancellationToken cancel)
        {
            TargetLook found = MacroConditions.WithTimeout(
                () => target.Kind == TargetKind.Element
                    ? FindElement(anchor, target.ControlType, target.Name, target.AutomationId)
                    : shot != null ? FindImage(shot, client, target) : FindImage(anchor, process, target),
                null, SearchTimeoutMs, cancel);
            if (found == null) return new TargetLook { Status = TargetStatus.Inconclusive };
            if (found.Status == TargetStatus.Found)
            {
                if ((shot != null ? client : WindowLock.ClientArea(anchor)).Contains(found.Screen)) found.Read = ConditionRead.Met;
                else found.Status = TargetStatus.Offscreen; // visible, but not where the window shows it
            }
            return found;
        }

        /// <summary>
        /// The last look right before the click, at the point <see cref="Find"/> gave: the app in front, the point in
        /// the step's window and not covered, and the target itself under it (the element, or the image).
        /// </summary>
        public static ConditionRead Confirm(IntPtr bound, uint process, MacroStep step, TargetLook look, CancellationToken cancel)
        {
            if (MacroTarget.ProcessOf(bound) != process) return ConditionRead.Closed;
            if (!MacroTarget.Owns(bound, WindowLock.Foreground())) return ConditionRead.Away;
            IntPtr anchor = MacroTarget.FindAnchor(bound, step.Window);
            if (anchor == IntPtr.Zero) return ConditionRead.NotYet;
            ConditionRead cover = MacroConditions.CoverAround(anchor, process, look.Screen);
            if (cover != ConditionRead.Met) return cover;
            SmartTarget target = step.Target;
            bool there = MacroConditions.WithTimeout(
                () => target.Kind == TargetKind.Element ? HitsElement(look) : ImageStillThere(look, target),
                false, ConfirmTimeoutMs, cancel);
            return there ? ConditionRead.Met : ConditionRead.NotYet;
        }

        /// <summary>
        /// The one available element of the window with this type and name (and id, when given). Run it through
        /// <see cref="MacroConditions.WithTimeout"/>.
        /// </summary>
        internal static TargetLook FindElement(IntPtr anchor, string type, string name, string id)
        {
            var look = new TargetLook();
            List<AutomationElement> matches = Matches(anchor, type, name, id);
            look.Count = matches.Count;
            if (matches.Count == 0) return look;
            if (matches.Count > 1)
            {
                look.Status = TargetStatus.Ambiguous;
                return look;
            }
            AutomationElement.AutomationElementInformation info = matches[0].Current;
            System.Windows.Rect rect = info.BoundingRectangle;
            if (!info.IsEnabled) look.Status = TargetStatus.Disabled;
            else if (info.IsOffscreen || rect.IsEmpty || rect.Width < 1 || rect.Height < 1) look.Status = TargetStatus.Offscreen;
            else
            {
                look.Status = TargetStatus.Found;
                look.Bounds = ToRectangle(rect);
                look.Screen = new Point(look.Bounds.X + look.Bounds.Width / 2, look.Bounds.Y + look.Bounds.Height / 2);
                look.Element = matches[0];
            }
            return look;
        }

        private static Rectangle ToRectangle(System.Windows.Rect rect) =>
            Rectangle.FromLTRB((int)Math.Round(rect.Left), (int)Math.Round(rect.Top), (int)Math.Round(rect.Right), (int)Math.Round(rect.Bottom));

        /// <summary>
        /// The elements of the window's UI Automation tree with this type and name (and id, when given). Not filtered
        /// by process: a browser serves its page from several; the scope is the window itself.
        /// </summary>
        private static List<AutomationElement> Matches(IntPtr anchor, string type, string name, string id)
        {
            var matches = new List<AutomationElement>();
            ControlType control = ControlTypeOf(type);
            if (control == null || name.Length == 0) return matches;
            // UI Automation compares names exactly, and the file keeps only \n: multi-line names are compared here.
            bool multiline = name.IndexOf('\n') >= 0;
            Condition condition = new PropertyCondition(AutomationElement.ControlTypeProperty, control);
            if (!multiline) condition = new AndCondition(condition, new PropertyCondition(AutomationElement.NameProperty, name));
            if (id.Length > 0) condition = new AndCondition(condition, new PropertyCondition(AutomationElement.AutomationIdProperty, id));
            foreach (AutomationElement element in AutomationElement.FromHandle(anchor).FindAll(TreeScope.Descendants, condition))
                if (!multiline || MacroConditions.Normalize(element.Current.Name) == name) matches.Add(element);
            return matches;
        }

        /// <summary>
        /// F7 for a click target, on a background thread: the element at the point and how many like it the window
        /// has, and the image regions around the point checked against the screenshot taken at F7.
        /// </summary>
        /// <param name="anchor">The step's window (the window under the point).</param>
        /// <param name="shot">The client area of <paramref name="anchor"/> at F7 (owned by the probe afterwards: disposed here).</param>
        public static TargetProbe Probe(IntPtr anchor, Point screen, Rectangle client, Bitmap shot)
        {
            var probe = new TargetProbe { At = new Point(screen.X - client.X, screen.Y - client.Y) };
            using (shot)
            {
                probe.Element = ProbeElement(anchor, screen);
                if (probe.Element != null)
                {
                    Rectangle visible = Rectangle.Intersect(probe.Element.Bounds, client);
                    if (visible.Width > 0 && visible.Height > 0)
                        probe.ElementPreview = shot.Clone(new Rectangle(visible.X - client.X, visible.Y - client.Y, visible.Width, visible.Height), PixelFormat.Format32bppArgb);
                }
                probe.Images = ImageTarget.Options(shot, probe.At);
            }
            return probe;
        }

        /// <summary>
        /// The element a person means at the point (the interactive control, not the text inside it, up to 3 levels
        /// up; else the first element with a name) and how many like it the window has. Retries while the window's
        /// tree is still empty (Chrome builds it on the first request). Off the UI thread; null = no named element.
        /// </summary>
        internal static ElementProbe ProbeElement(IntPtr anchor, Point screen)
        {
            AutomationElement pointed = MacroConditions.WithTimeout(() => ElementAt(screen), null, ProbeTimeoutMs, CancellationToken.None);
            if (pointed == null) return null;
            return MacroConditions.WithTimeout(() =>
            {
                AutomationElement.AutomationElementInformation info = pointed.Current;
                var probe = new ElementProbe { ControlType = MacroConditions.TypeName(info.ControlType), Name = MacroConditions.Normalize(info.Name) };
                System.Windows.Rect rect = info.BoundingRectangle;
                if (!rect.IsEmpty)
                    probe.Bounds = ToRectangle(rect);
                List<AutomationElement> same = Matches(anchor, probe.ControlType, probe.Name, string.Empty);
                for (int attempt = 1; attempt < ProbeTries && same.Count == 0; attempt++)
                {
                    Thread.Sleep(ProbeRetryMs);
                    same = Matches(anchor, probe.ControlType, probe.Name, string.Empty);
                }
                // The pointed element must be one of the window's own: what the run will search.
                if (!same.Exists(e => Automation.Compare(e, pointed))) return probe;
                probe.Count = same.Count;
                string id = info.AutomationId ?? string.Empty;
                if (probe.Count > 1 && id.Length > 0)
                {
                    List<AutomationElement> withId = Matches(anchor, probe.ControlType, probe.Name, id);
                    if (withId.Count == 1 && Automation.Compare(withId[0], pointed))
                    {
                        probe.AutomationId = id;
                        probe.Count = 1;
                    }
                }
                return probe;
            }, null, ProbeTimeoutMs, CancellationToken.None);
        }

        private static AutomationElement ElementAt(Point screen)
        {
            AutomationElement element = AutomationElement.FromPoint(new System.Windows.Point(screen.X, screen.Y));
            AutomationElement named = null;
            for (int depth = 0; depth <= 3 && element != null; depth++)
            {
                AutomationElement.AutomationElementInformation info = element.Current;
                if (info.Name.Length > 0)
                {
                    if (Array.IndexOf(Interactive, info.ControlType) >= 0) return element;
                    if (named == null) named = element;
                }
                element = TreeWalker.ControlViewWalker.GetParent(element);
            }
            return named;
        }

        /// <summary>The image in a screenshot of the window's client area. Run it through <see cref="MacroConditions.WithTimeout"/>.</summary>
        private static TargetLook FindImage(IntPtr anchor, uint process, SmartTarget target)
        {
            Rectangle client = WindowLock.ClientArea(anchor);
            using (Bitmap image = target.Decode())
                if (image == null || client.Width < image.Width || client.Height < image.Height) return new TargetLook();
            TargetLook look;
            using (Bitmap shot = Shoot(client)) look = FindImage(shot, client, target);
            if (look.Status == TargetStatus.NotFound && ForeignCover(anchor, process, client)) look.Read = ConditionRead.Covered; // maybe hidden under it: pause
            return look;
        }

        /// <summary>The image in <paramref name="shot"/>, a capture of the client area <paramref name="client"/> (screen coordinates).</summary>
        private static TargetLook FindImage(Bitmap shot, Rectangle client, SmartTarget target)
        {
            var look = new TargetLook();
            using (Bitmap image = target.Decode())
            {
                if (image == null || shot.Width < image.Width || shot.Height < image.Height) return look;
                ImageMatch match = ImageTarget.Search(shot, image);
                look.Count = match.Count;
                if (match.Found.HasValue)
                {
                    Rectangle found = match.Found.Value;
                    look.Status = TargetStatus.Found;
                    look.Bounds = new Rectangle(client.X + found.X, client.Y + found.Y, found.Width, found.Height);
                    look.Screen = new Point(look.Bounds.X + found.Width / 2, look.Bounds.Y + found.Height / 2);
                }
                else if (match.Count > 1) look.Status = TargetStatus.Ambiguous;
            }
            return look;
        }

        /// <summary>Whether a window of another program is over part of the area (checked on a 16-pixel grid).</summary>
        private static bool ForeignCover(IntPtr anchor, uint process, Rectangle area)
        {
            // ponytail: a window narrower than 16 px can slip between the grid points; it also hides little of a 32 px image.
            for (int y = area.Top; y < area.Bottom; y += CoverGrid)
                for (int x = area.Left; x < area.Right; x += CoverGrid)
                {
                    IntPtr root = MacroTarget.RootAt(new Point(x, y));
                    // Zero = off the screen: nothing can be hidden there by another program.
                    if (root != IntPtr.Zero && root != anchor && MacroTarget.ProcessOf(root) != process) return true;
                }
            return false;
        }

        private static bool HitsElement(TargetLook look)
        {
            var target = (AutomationElement)look.Element;
            if (!target.Current.IsEnabled) return false;
            AutomationElement hit = AutomationElement.FromPoint(new System.Windows.Point(look.Screen.X, look.Screen.Y));
            // The target itself, or something inside it (the text of a button), up to 3 levels down.
            for (int depth = 0; depth <= 3 && hit != null; depth++)
            {
                if (Automation.Compare(hit, target)) return true;
                hit = TreeWalker.ControlViewWalker.GetParent(hit);
            }
            return false;
        }

        private static bool ImageStillThere(TargetLook look, SmartTarget target)
        {
            using (Bitmap image = target.Decode())
            using (Bitmap shot = Shoot(look.Bounds))
                return image != null && ImageTarget.IsAt(shot, image, Point.Empty);
        }

        /// <summary>The screen inside the rectangle (physical pixels on a per-monitor-aware thread). The cursor is not included.</summary>
        internal static Bitmap Shoot(Rectangle area)
        {
            var shot = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics g = Graphics.FromImage(shot)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);
                return shot;
            }
            catch
            {
                shot.Dispose();
                throw;
            }
        }

        private static Dictionary<string, ControlType> controlTypes;

        /// <summary>ControlType.Button for "Button" (the name the file stores); null if unknown.</summary>
        internal static ControlType ControlTypeOf(string name)
        {
            Dictionary<string, ControlType> types = Volatile.Read(ref controlTypes);
            if (types == null)
            {
                types = new Dictionary<string, ControlType>();
                foreach (FieldInfo field in typeof(ControlType).GetFields(BindingFlags.Public | BindingFlags.Static))
                    if (field.GetValue(null) is ControlType type) types[MacroConditions.TypeName(type)] = type;
                Volatile.Write(ref controlTypes, types);
            }
            return types.TryGetValue(name, out ControlType found) ? found : null;
        }
    }
}
