using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

namespace VClicker
{
    internal enum EventKind { MouseDown, MouseUp, Move, Wheel, KeyDown, Left, Back }

    /// <summary>
    /// One input event as the recorder saw it, already resolved: which window, client coordinates, what text the
    /// key typed. <see cref="StepBuilder"/> turns a stream of these into steps; it never talks to Windows itself.
    /// </summary>
    internal struct RecordedEvent
    {
        public EventKind Kind;
        /// <summary>Milliseconds on the hook clock (GetTickCount); differences are taken unchecked, so it may wrap.</summary>
        public int Time;
        /// <summary>Client coordinates of <see cref="Window"/> (or of the bound window).</summary>
        public Point At;
        /// <summary>Screen coordinates (to carry a drag across windows).</summary>
        public Point Screen;
        /// <summary>null = the bound window.</summary>
        public WindowRef Window;
        /// <summary>The event happened in the bound window (or one of its dialogs and menus).</summary>
        public bool InTarget;
        public ClickButton Button;
        public int Notches;
        /// <summary>Key code plus Keys.Shift / Control / Alt.</summary>
        public Keys Key;
        /// <summary>Printable text the key produced (dead keys already composed); null = none.</summary>
        public string Text;
        /// <summary>The key went to a password field: never recorded.</summary>
        public bool Password;
    }

    /// <summary>
    /// The recording rules (see docs/superpowers/specs/2026-10-21-macro-studio-design.md): which events become which
    /// steps, and the waits between them. Pure logic, so every rule is unit-tested without hooks.
    /// </summary>
    internal sealed class StepBuilder
    {
        public const int WaitThresholdMs = 250;
        public const int HoverRestMs = 1000;
        public const int TypingBreakMs = 1000;
        public const int SamePointPx = 4;

        private readonly int doubleClickMs;
        private readonly List<MacroStep> steps = new List<MacroStep>();

        private bool anyAction;
        private int lastEnd;            // when the latest action ended
        private int? leftAfterAction;   // first time the bound window lost the front after that action

        private bool mouseDown;
        private RecordedEvent down;
        private bool dragging;
        private int lastClickDown;      // down time of the latest click step, for double clicks
        private bool hasLastMouse;
        private RecordedEvent lastMouse; // where the latest mouse action happened (a hover there adds nothing)

        private bool restValid;         // the cursor is resting at rest (in the target, moved since the last action)
        private RecordedEvent rest;
        private bool hoverPending;
        private RecordedEvent hover;

        public StepBuilder(int doubleClickMs)
        {
            this.doubleClickMs = doubleClickMs;
        }

        public void Add(RecordedEvent e)
        {
            switch (e.Kind)
            {
                case EventKind.Move: OnMove(e); break;
                case EventKind.MouseDown: OnMouseDown(e); break;
                case EventKind.MouseUp: OnMouseUp(e); break;
                case EventKind.Wheel: OnWheel(e); break;
                case EventKind.KeyDown: OnKey(e); break;
                case EventKind.Left:
                    if (anyAction && !leftAfterAction.HasValue) leftAfterAction = e.Time;
                    restValid = hoverPending = false;
                    break;
                case EventKind.Back: break;
            }
        }

        /// <param name="stopTime">When recording was stopped (on the same clock as the events).</param>
        public List<MacroStep> Finish(int stopTime)
        {
            // A rest at the very end leads nowhere: no hover step. A held button at the end is dropped.
            if (anyAction)
            {
                int end = leftAfterAction ?? stopTime;
                AddWait(end);
            }
            return steps;
        }

        // ------------------------------------------------------------------ mouse

        private void OnMove(RecordedEvent e)
        {
            if (mouseDown)
            {
                if (Distance(e.Screen, down.Screen) > SamePointPx) dragging = true;
                return;
            }
            if (!e.InTarget)
            {
                restValid = false;
                return;
            }
            if (restValid && Same(e, rest)) return; // jitter around the resting spot
            CheckRest(e);
            rest = e;
            restValid = true;
        }

        /// <summary>The cursor rested at <see cref="rest"/> until <paramref name="next"/>: remember it as a hover.</summary>
        private void CheckRest(RecordedEvent next)
        {
            if (restValid && Elapsed(rest.Time, next.Time) >= HoverRestMs)
            {
                hover = rest;
                hoverPending = true; // consecutive rests keep only the last one
            }
        }

        /// <summary>Before an action: emit the pending hover, unless the action happens at that same spot.</summary>
        private void FlushHover(RecordedEvent action, bool mouseAction)
        {
            CheckRest(action);
            restValid = false;
            if (!hoverPending) return;
            hoverPending = false;
            if (mouseAction && Same(action, hover)) return;
            if (hasLastMouse && Same(lastMouse, hover)) return;
            BeginAction(hover.Time);
            steps.Add(new MacroStep { Kind = StepKind.Hover, At = hover.At, Window = hover.Window?.Clone() });
            EndAction(hover.Time);
        }

        private void OnMouseDown(RecordedEvent e)
        {
            if (mouseDown) return; // a second button while one is held: ignored, the first one decides
            if (!e.InTarget)
            {
                restValid = hoverPending = false;
                return;
            }
            FlushHover(e, true);
            mouseDown = true;
            dragging = false;
            down = e;
            lastMouse = e;
            hasLastMouse = true;
        }

        private void OnMouseUp(RecordedEvent e)
        {
            if (!mouseDown || e.Button != down.Button) return;
            mouseDown = false;
            var to = new Point(down.At.X + e.Screen.X - down.Screen.X, down.At.Y + e.Screen.Y - down.Screen.Y);
            if (dragging || Distance(e.Screen, down.Screen) > SamePointPx)
            {
                if (!e.InTarget) return; // dropped on another app: not something a macro may repeat
                BeginAction(down.Time);
                steps.Add(new MacroStep { Kind = StepKind.Drag, Button = down.Button, At = down.At, To = to, Window = down.Window?.Clone() });
                lastMouse.At = to; // the cursor is now where the drag ended
                EndAction(e.Time);
                return;
            }

            MacroStep last = Last;
            if (last != null && last.Kind == StepKind.Click && last.Button == down.Button && SameWindow(last.Window, down.Window) &&
                Distance(last.At, down.At) <= SamePointPx && Elapsed(lastClickDown, down.Time) <= doubleClickMs)
            {
                last.Kind = StepKind.DoubleClick;
                EndAction(e.Time);
                return;
            }
            BeginAction(down.Time);
            steps.Add(new MacroStep { Kind = StepKind.Click, Button = down.Button, At = down.At, Window = down.Window?.Clone() });
            lastClickDown = down.Time;
            EndAction(e.Time);
        }

        private void OnWheel(RecordedEvent e)
        {
            if (!e.InTarget || e.Notches == 0 || mouseDown) return;
            FlushHover(e, true);
            lastMouse = e;
            hasLastMouse = true;
            MacroStep last = Last;
            if (last != null && last.Kind == StepKind.Scroll && Math.Sign(last.Notches) == Math.Sign(e.Notches) &&
                SameWindow(last.Window, e.Window) && Distance(last.At, e.At) <= SamePointPx &&
                Elapsed(lastEnd, e.Time) < WaitThresholdMs)
            {
                last.Notches = Math.Max(-MacroStep.MaxCount, Math.Min(MacroStep.MaxCount, last.Notches + e.Notches));
                EndAction(e.Time);
                return;
            }
            BeginAction(e.Time);
            steps.Add(new MacroStep { Kind = StepKind.Scroll, Notches = e.Notches, At = e.At, Window = e.Window?.Clone() });
            EndAction(e.Time);
        }

        // ------------------------------------------------------------------ keyboard

        private void OnKey(RecordedEvent e)
        {
            if (!e.InTarget || mouseDown) return;
            Keys code = e.Key & Keys.KeyCode;
            bool ctrl = (e.Key & Keys.Control) != 0, alt = (e.Key & Keys.Alt) != 0;
            if (Hotkey.IsModifierKey(code)) return;                                   // Alt alone, Shift alone...
            if (alt && (code == Keys.Tab || code == Keys.Escape)) return;             // switches windows

            MacroStep step;
            if (e.Password) step = new MacroStep { Kind = StepKind.Password };
            else if (MacroKeys.IsSpecial(code) && !ctrl && !alt) step = new MacroStep { Kind = StepKind.Key, Key = e.Key & (Keys.KeyCode | Keys.Shift) };
            else if (!string.IsNullOrEmpty(e.Text) && ctrl == alt) step = new MacroStep { Kind = StepKind.Type, Text = e.Text }; // Ctrl+Alt = AltGr
            else if (ctrl || alt) step = new MacroStep { Kind = StepKind.Shortcut, Key = e.Key };
            else return;                                                                // Caps Lock, Print Screen, media keys...

            FlushHover(e, false);
            MacroStep last = Last;
            int gap = Elapsed(lastEnd, e.Time);
            if (last != null && last.Kind == step.Kind)
            {
                if ((step.Kind == StepKind.Type || step.Kind == StepKind.Password) && gap < TypingBreakMs)
                {
                    if (step.Kind == StepKind.Type) last.Text += step.Text;
                    EndAction(e.Time);
                    return;
                }
                if (step.Kind == StepKind.Key && last.Key == step.Key && gap < WaitThresholdMs && last.Count < MacroStep.MaxCount)
                {
                    last.Count++;
                    EndAction(e.Time);
                    return;
                }
            }
            BeginAction(e.Time);
            steps.Add(step);
            EndAction(e.Time);
        }

        // ------------------------------------------------------------------ timing

        private MacroStep Last => steps.Count == 0 ? null : steps[steps.Count - 1];

        /// <summary>An action starts at <paramref name="time"/>: the time since the previous one becomes a wait.</summary>
        private void BeginAction(int time)
        {
            if (anyAction) AddWait(time);
            anyAction = true;
        }

        private void EndAction(int time)
        {
            lastEnd = time;
            leftAfterAction = null;
        }

        private void AddWait(int until)
        {
            int gap = Elapsed(lastEnd, until);
            if (gap < WaitThresholdMs) return;
            int rounded = (int)Math.Min(MacroStep.MaxWaitMs, (gap + 50L) / 100 * 100);
            steps.Add(new MacroStep { Kind = StepKind.Wait, DurationMs = rounded, RecordedMs = rounded });
        }

        private static int Elapsed(int from, int to) => unchecked(to - from);

        private static bool Same(RecordedEvent a, RecordedEvent b) =>
            SameWindow(a.Window, b.Window) && Distance(a.At, b.At) <= SamePointPx;

        private static bool SameWindow(WindowRef a, WindowRef b) => a == null ? b == null : a.Matches(b);

        private static double Distance(Point a, Point b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    /// <summary>
    /// Records mouse and keyboard input in the bound window, with the Windows low-level hooks, only between
    /// <see cref="Start"/> and <see cref="Stop"/>. The hook callbacks only queue what they see (Windows drops slow
    /// hooks); a second thread resolves windows, typed text and password fields and feeds a <see cref="StepBuilder"/>.
    /// Nothing is injected into other programs.
    /// </summary>
    internal sealed class MacroRecorder : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
        private const int WM_QUIT = 0x12;
        private const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
        private const int WM_MOUSEMOVE = 0x200, WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_RBUTTONDOWN = 0x204,
                          WM_RBUTTONUP = 0x205, WM_MBUTTONDOWN = 0x207, WM_MBUTTONUP = 0x208, WM_MOUSEWHEEL = 0x20A;
        private const uint LLMHF_INJECTED = 0x1, LLKHF_INJECTED = 0x10;
        private const int ForegroundPollMs = 50;

        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public Native.POINT pt;
            public uint mouseData, flags, time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode, scanCode, flags, time;
            public UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam, lParam;
            public uint time;
            public Native.POINT pt;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint threadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG message, IntPtr window, uint min, uint max);

        [DllImport("user32.dll")]
        private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string name);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint threadId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int ToUnicodeEx(uint key, uint scan, byte[] state, StringBuilder text, int size, uint flags, IntPtr layout);

        /// <summary>What a hook saw, untouched (resolved later on the processing thread).</summary>
        private struct Raw
        {
            public bool Mouse;
            public int Message;
            public Point Screen;
            public int Data;      // wheel delta, or the virtual key
            public int Scan;
            public int Time;
            /// <summary>Taken in the hook itself, so a later look-up cannot see a window that has changed since.</summary>
            public IntPtr Front;
            /// <summary>Mouse buttons and wheel: the top-level window under the point at that moment.</summary>
            public IntPtr Root;
        }

        private readonly IntPtr bound;
        private readonly uint boundProcess;
        private readonly List<Hotkey> ignored;
        private readonly bool acceptInjected;
        private readonly Func<bool> isPassword;
        private readonly StepBuilder builder = new StepBuilder(SystemInformation.DoubleClickTime);
        private readonly BlockingCollection<Raw> queue = new BlockingCollection<Raw>();
        private readonly HookProc mouseProc, keyboardProc; // kept alive while the hooks exist

        private Thread hookThread, workThread;
        private uint hookThreadId;
        private IntPtr mouseHook, keyboardHook;
        private volatile bool inFront = true;
        private volatile bool stopping;
        private bool started, stopped;

        // UI Automation is slow in some apps: one answer per focused window, until the user moves elsewhere.
        private const int PasswordTimeoutMs = 500;
        private IntPtr passwordFront;
        private bool passwordKnown, passwordAnswer;

        // Keyboard state, tracked from the events themselves (processing thread only).
        private bool shift, ctrl, alt, win, capsLock;
        private char pendingDead;

        /// <param name="ignored">V-Clicker's own hotkeys: never recorded.</param>
        /// <param name="acceptInjected">Tests only: record synthetic input too.</param>
        /// <param name="isPassword">Whether the focused field is a password field (default: UI Automation).</param>
        public MacroRecorder(IntPtr bound, IEnumerable<Hotkey> ignored, bool acceptInjected = false, Func<bool> isPassword = null)
        {
            this.bound = bound;
            boundProcess = MacroTarget.ProcessOf(bound);
            this.ignored = new List<Hotkey>(ignored);
            this.acceptInjected = acceptInjected;
            this.isPassword = isPassword ?? FocusIsPassword;
            mouseProc = OnMouse;
            keyboardProc = OnKeyboard;
        }

        /// <summary>The bound window is not in front: input is not being recorded.</summary>
        public bool Paused => !inFront;

        /// <exception cref="InvalidOperationException">Windows refused the hooks.</exception>
        public void Start()
        {
            if (started) throw new InvalidOperationException("already started");
            capsLock = Control.IsKeyLocked(Keys.CapsLock);
            inFront = MacroTarget.Owns(bound, WindowLock.Foreground());

            int error = 0;
            using (var ready = new ManualResetEventSlim())
            {
                hookThread = new Thread(() =>
                {
                    using (MacroTarget.PerMonitorDpi()) // the hook looks up windows at physical points
                    {
                        hookThreadId = GetCurrentThreadId();
                        IntPtr module = GetModuleHandle(null);
                        mouseHook = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, module, 0);
                        keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, keyboardProc, module, 0);
                        if (mouseHook == IntPtr.Zero || keyboardHook == IntPtr.Zero) error = Marshal.GetLastWin32Error() | 1;
                        ready.Set();
                        if (error == 0)
                            while (GetMessage(out MSG message, IntPtr.Zero, 0, 0) > 0) { } // low-level hooks need a message loop
                        if (mouseHook != IntPtr.Zero) UnhookWindowsHookEx(mouseHook);
                        if (keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(keyboardHook);
                    }
                })
                { IsBackground = true, Name = "V-Clicker recorder hooks", Priority = ThreadPriority.AboveNormal };
                hookThread.Start();
                ready.Wait();
            }
            if (error != 0)
            {
                hookThread.Join(1000);
                throw new InvalidOperationException("Windows refused the input hooks (error " + error + ").");
            }
            workThread = new Thread(Process) { IsBackground = true, Name = "V-Clicker recorder" };
            workThread.Start();
            started = true;
        }

        /// <summary>Removes the hooks and returns the recorded steps. Safe to call more than once.</summary>
        public List<MacroStep> Stop()
        {
            int stopTime = Environment.TickCount;
            if (!started || stopped) return new List<MacroStep>();
            stopped = true;
            stopping = true; // the hooks stop queueing at once, even if their thread is slow to exit
            PostThreadMessage(hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            hookThread.Join(1000);
            queue.CompleteAdding();
            workThread.Join(3000);
            lock (builder) return builder.Finish(stopTime);
        }

        public void Dispose()
        {
            Stop();
            queue.Dispose();
        }

        // ------------------------------------------------------------------ hook thread: queue only

        private IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && !stopping)
            {
                var data = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                if ((data.flags & LLMHF_INJECTED) == 0 || acceptInjected)
                {
                    var point = new Point(data.pt.X, data.pt.Y);
                    int message = (int)wParam;
                    Enqueue(new Raw
                    {
                        Mouse = true, Message = message, Screen = point,
                        Data = (short)(data.mouseData >> 16), Time = unchecked((int)data.time), Front = WindowLock.Foreground(),
                        // Moves are many and only matter for hovers: their window is looked up later.
                        Root = message == WM_MOUSEMOVE ? IntPtr.Zero : MacroTarget.RootAt(point)
                    });
                }
            }
            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        private IntPtr OnKeyboard(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0 && !stopping)
            {
                var data = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                if ((data.flags & LLKHF_INJECTED) == 0 || acceptInjected)
                {
                    Enqueue(new Raw
                    {
                        Message = (int)wParam, Data = (int)data.vkCode, Scan = (int)data.scanCode, Time = unchecked((int)data.time),
                        Front = WindowLock.Foreground()
                    });
                }
            }
            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        private void Enqueue(Raw raw)
        {
            try { queue.Add(raw); }
            catch (InvalidOperationException) { } // stopping or stopped (ObjectDisposedException too): a hook must never throw
        }

        // ------------------------------------------------------------------ processing thread

        private void Process()
        {
            using (MacroTarget.PerMonitorDpi())
            {
                Raw? held = null; // an event taken while collapsing mouse moves, handled next
                while (true)
                {
                    Raw raw;
                    if (held.HasValue)
                    {
                        raw = held.Value;
                        held = null;
                    }
                    else
                    {
                        bool got;
                        try { got = queue.TryTake(out raw, ForegroundPollMs); }
                        catch (InvalidOperationException) { break; }
                        if (!got)
                        {
                            if (queue.IsCompleted) break;
                            PollForeground(Environment.TickCount, WindowLock.Foreground());
                            continue;
                        }
                    }
                    if (raw.Mouse && raw.Message == WM_MOUSEMOVE)
                    {
                        // Only the latest position of a burst of moves matters.
                        while (queue.TryTake(out Raw next))
                        {
                            if (next.Mouse && next.Message == WM_MOUSEMOVE) raw = next;
                            else { held = next; break; }
                        }
                    }
                    PollForeground(raw.Time, raw.Front);
                    RecordedEvent? e;
                    try { e = raw.Mouse ? Mouse(raw) : Keyboard(raw); }
                    catch (Exception) { e = null; } // a window vanished mid-lookup: drop the event, keep recording
                    if (e.HasValue) lock (builder) builder.Add(e.Value);
                }
            }
        }

        /// <summary>The bound window still exists (a closed window's handle may be reused by another program).</summary>
        private bool BoundAlive => MacroTarget.ProcessOf(bound) == boundProcess;

        private void PollForeground(int time, IntPtr foreground)
        {
            bool front = BoundAlive && MacroTarget.Owns(bound, foreground);
            if (front != inFront || foreground != passwordFront) passwordKnown = false;
            if (front == inFront) return;
            inFront = front;
            lock (builder) builder.Add(new RecordedEvent { Kind = front ? EventKind.Back : EventKind.Left, Time = time });
        }

        private RecordedEvent? Mouse(Raw raw)
        {
            var e = new RecordedEvent { Time = raw.Time, Screen = raw.Screen };
            switch (raw.Message)
            {
                case WM_MOUSEMOVE: e.Kind = EventKind.Move; break;
                case WM_LBUTTONDOWN: e.Kind = EventKind.MouseDown; e.Button = ClickButton.Left; break;
                case WM_LBUTTONUP: e.Kind = EventKind.MouseUp; e.Button = ClickButton.Left; break;
                case WM_RBUTTONDOWN: e.Kind = EventKind.MouseDown; e.Button = ClickButton.Right; break;
                case WM_RBUTTONUP: e.Kind = EventKind.MouseUp; e.Button = ClickButton.Right; break;
                case WM_MBUTTONDOWN: e.Kind = EventKind.MouseDown; e.Button = ClickButton.Middle; break;
                case WM_MBUTTONUP: e.Kind = EventKind.MouseUp; e.Button = ClickButton.Middle; break;
                case WM_MOUSEWHEEL:
                    e.Kind = EventKind.Wheel;
                    e.Notches = raw.Data / 120; // ponytail: high-resolution wheels sending < 120 per event are dropped
                    break;
                default: return null; // X buttons, horizontal wheel
            }
            if (e.Kind == EventKind.MouseDown) passwordKnown = false; // probably a different field now
            if (!BoundAlive) return e;
            IntPtr root = raw.Root != IntPtr.Zero ? raw.Root : MacroTarget.RootAt(raw.Screen);
            e.InTarget = MacroTarget.Locate(bound, root, raw.Screen, out e.Window, out e.At);
            return e;
        }

        private RecordedEvent? Keyboard(Raw raw)
        {
            var key = (Keys)raw.Data;
            bool down = raw.Message == WM_KEYDOWN || raw.Message == WM_SYSKEYDOWN;
            if (!down && raw.Message != WM_KEYUP && raw.Message != WM_SYSKEYUP) return null;
            switch (key)
            {
                case Keys.LShiftKey: case Keys.RShiftKey: case Keys.ShiftKey: shift = down; return null;
                case Keys.LControlKey: case Keys.RControlKey: case Keys.ControlKey: ctrl = down; return null;
                case Keys.LMenu: case Keys.RMenu: case Keys.Menu: alt = down; return null;
                case Keys.LWin: case Keys.RWin: win = down; return null;
            }
            if (!down) return null;
            if (key == Keys.CapsLock) capsLock = !capsLock;
            if (win) return null; // Windows-key shortcuts belong to the system

            Keys withModifiers = key | (shift ? Keys.Shift : 0) | (ctrl ? Keys.Control : 0) | (alt ? Keys.Alt : 0);
            foreach (Hotkey hotkey in ignored)
                if (hotkey.Key == key) return null; // V-Clicker's own keys, with or without modifiers

            if (MacroKeys.IsSpecial(key))
            {
                pendingDead = '\0';    // Esc, Backspace, Enter... cancel a pending accent
                passwordKnown = false; // Tab, Enter: probably a different field now
            }
            IntPtr front = raw.Front;
            var e = new RecordedEvent
            {
                Kind = EventKind.KeyDown, Time = raw.Time, Key = withModifiers, InTarget = BoundAlive && MacroTarget.Owns(bound, front)
            };
            if (!e.InTarget) return e; // the builder drops it; the time still passes

            // VK_PACKET: a character typed by an IME, the emoji panel or an on-screen keyboard; the scan code is the character.
            string text = key == Keys.Packet ? ((char)raw.Scan).ToString()
                        : MacroKeys.IsSpecial(key) ? null : Translate(key, raw.Scan, front);
            if (key == Keys.Packet) e.Key = Keys.Packet;
            if (text != null && IsPasswordField(front))
            {
                e.Password = true;
                return e;
            }
            e.Text = text;
            return e;
        }

        /// <summary>The printable text a key produces with the current modifiers (dead keys composed); null = none.</summary>
        private string Translate(Keys key, int scan, IntPtr front)
        {
            var state = new byte[256];
            if (shift) state[(int)Keys.ShiftKey] = state[(int)Keys.LShiftKey] = 0x80;
            if (ctrl) state[(int)Keys.ControlKey] = state[(int)Keys.LControlKey] = 0x80;
            if (alt) state[(int)Keys.Menu] = state[(int)Keys.RMenu] = 0x80;
            if (capsLock) state[(int)Keys.CapsLock] = 0x01;
            IntPtr layout = GetKeyboardLayout(GetWindowThreadProcessId(front, out _));
            var buffer = new StringBuilder(8);
            // Flag 4: leave the keyboard's dead-key state alone (the user is typing into the target right now).
            int count = ToUnicodeEx((uint)key, (uint)scan, state, buffer, buffer.Capacity, 4, layout);
            if (count < 0)
            {
                char dead = buffer.Length > 0 ? buffer[0] : '\0';
                if (pendingDead == '\0')
                {
                    pendingDead = dead;
                    return null;
                }
                string both = new string(new[] { pendingDead, dead }); // accent, accent: both are typed
                pendingDead = '\0';
                return both;
            }
            string text = count > 0 ? buffer.ToString(0, Math.Min(count, buffer.Length)) : string.Empty;
            if (text.Any(char.IsControl)) text = string.Empty;
            if (pendingDead != '\0')
            {
                char dead = pendingDead;
                pendingDead = '\0';
                if (text.Length == 1) text = DeadKeys.Compose(dead, text[0]);
            }
            return text.Length == 0 ? null : text;
        }

        private bool IsPasswordField(IntPtr front)
        {
            if (passwordKnown && front == passwordFront) return passwordAnswer;
            System.Threading.Tasks.Task<bool> ask = System.Threading.Tasks.Task.Run(isPassword);
            // ponytail: an app that does not answer in time counts as "not a password field"; raise the timeout if one turns up.
            passwordAnswer = ask.Wait(PasswordTimeoutMs) && ask.Result;
            passwordFront = front;
            passwordKnown = true;
            return passwordAnswer;
        }

        private static bool FocusIsPassword()
        {
            try
            {
                AutomationElement focused = AutomationElement.FocusedElement;
                return focused != null && focused.Current.IsPassword;
            }
            catch (Exception)
            {
                return false; // the element went away, or the app does not answer UI Automation
            }
        }
    }

    /// <summary>Dead keys (the accent keys of ABNT2 and US-International): accent + letter = accented letter.</summary>
    internal static class DeadKeys
    {
        public static string Compose(char dead, char next)
        {
            if (next == ' ') return dead.ToString(); // accent then space = the accent itself
            char mark;
            switch (dead)
            {
                case '\u00B4': case '\'': mark = '\u0301'; break; // acute
                case '`': mark = '\u0300'; break;                  // grave
                case '^': mark = '\u0302'; break;                  // circumflex
                case '~': mark = '\u0303'; break;                  // tilde
                case '\u00A8': case '"': mark = '\u0308'; break;  // diaeresis
                case '\u00B8': mark = '\u0327'; break;             // cedilla
                default: return new string(new[] { dead, next });
            }
            string composed = new string(new[] { next, mark }).Normalize(NormalizationForm.FormC);
            return composed.Length == 1 ? composed : new string(new[] { dead, next });
        }
    }
}
