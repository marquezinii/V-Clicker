using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace VClicker
{
    /// <summary>Where a macro's input goes. The real one calls SendInput; tests use a fake.</summary>
    internal interface IInputSink
    {
        /// <returns>false if Windows refused the input.</returns>
        bool MoveTo(Point screen);
        bool Button(ClickButton button, bool down);
        bool Wheel(int notches);
        /// <param name="code">A key code without modifier flags.</param>
        bool Key(Keys code, bool down);
        bool Char(char c);
    }

    internal sealed class SendInputInputSink : IInputSink
    {
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private static readonly int InputSize = Marshal.SizeOf(typeof(Native.INPUT));

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint code, uint mapType);

        // If the cursor cannot be moved (secure desktop, say), clicking would hit whatever is under it.
        public bool MoveTo(Point screen) => Native.SetCursorPos(screen.X, screen.Y);

        public bool Button(ClickButton button, bool down)
        {
            SendInputSink.Flags(button, out uint press, out uint release);
            return Send(Native.Mouse(down ? press : release));
        }

        public bool Wheel(int notches)
        {
            Native.INPUT input = Native.Mouse(MOUSEEVENTF_WHEEL);
            input.u.mi.mouseData = unchecked((uint)(notches * 120)); // WHEEL_DELTA per notch, negative = down
            return Send(input);
        }

        public bool Key(Keys code, bool down)
        {
            var input = new Native.INPUT { type = INPUT_KEYBOARD };
            input.u.ki.wVk = (ushort)code;
            input.u.ki.wScan = (ushort)MapVirtualKey((uint)code, 0); // some programs read the scan code, not the key
            input.u.ki.dwFlags = (MacroKeys.IsExtended(code) ? KEYEVENTF_EXTENDEDKEY : 0) | (down ? 0 : KEYEVENTF_KEYUP);
            return Send(input);
        }

        /// <summary>Types the character itself, whatever the keyboard layout.</summary>
        public bool Char(char c)
        {
            var press = new Native.INPUT { type = INPUT_KEYBOARD };
            press.u.ki.wScan = c;
            press.u.ki.dwFlags = KEYEVENTF_UNICODE;
            Native.INPUT release = press;
            release.u.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
            return Native.SendInput(2, new[] { press, release }, InputSize) == 2;
        }

        private static bool Send(Native.INPUT input) => Native.SendInput(1, new[] { input }, InputSize) == 1;
    }

    internal enum MacroStopReason
    {
        Completed,
        /// <summary>Stopped by the user (hotkey, button or window close).</summary>
        Manual,
        TargetClosed,
        PauseTimeout,
        /// <summary>Reached a "password not recorded" step: the user must type it.</summary>
        Password,
        /// <summary>Windows refused the synthetic input.</summary>
        Blocked,
        /// <summary>The computer was suspended, hibernated or locked.</summary>
        Suspended,
        /// <summary>The macro has steps this version does not know.</summary>
        Unsupported,
        /// <summary>The window left the front in the middle of a drag: the button was released and the run ended.</summary>
        Interrupted
    }

    internal enum MacroPhase { Idle, Countdown, Running, Paused }

    /// <summary>
    /// Runs a macro on its own thread. Timing is an absolute schedule per repetition (waits as shown plus a minimum
    /// gap after every action), so a late step never makes the rest later; time spent paused shifts the schedule.
    /// The guard is asked right before every input; nothing is sent while it says no.
    /// </summary>
    internal sealed class MacroPlayer : IDisposable
    {
        public const int StepGapMs = 50;
        public const int CharGapMs = 50;
        public const int DragMs = 200;
        private const int DragMoves = 10;
        private const int PausePollMs = 25;

        private readonly IInputSink sink;
        private readonly object gate = new object();
        private readonly HashSet<Keys> keysDown = new HashSet<Keys>();
        private readonly HashSet<ClickButton> buttonsDown = new HashSet<ClickButton>();

        private Thread worker;
        private CancellationTokenSource cancel;
        private int phase, pauseCause, stopRequest, iteration, stepIndex;
        private long countdownEnd;

        // Run state (worker thread only).
        private Macro macro;
        private Func<MacroStep, MacroCheckResult> guard;
        private CancellationToken token;
        /// <summary>Stopwatch ticks to add to the schedule: time spent paused.</summary>
        private long shift;

        /// <summary>A pause longer than this ends the run. Settable for tests.</summary>
        internal int PauseLimitMs = 5 * 60 * 1000;

        public MacroPlayer(IInputSink sink)
        {
            this.sink = sink;
        }

        /// <summary>Raised on the worker thread when a run ends. Marshal to the UI thread yourself.</summary>
        public event Action<MacroStopReason> Stopped;

        public MacroPhase Phase => (MacroPhase)Volatile.Read(ref phase);
        public MacroCheck PauseCause => (MacroCheck)Volatile.Read(ref pauseCause);
        /// <summary>1-based repetition in progress.</summary>
        public int Iteration => Volatile.Read(ref iteration);
        /// <summary>0-based step in progress.</summary>
        public int StepIndex => Volatile.Read(ref stepIndex);

        public TimeSpan CountdownRemaining
        {
            get
            {
                if (Phase != MacroPhase.Countdown) return TimeSpan.Zero;
                long ticks = Interlocked.Read(ref countdownEnd) - Stopwatch.GetTimestamp();
                return ticks <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds((double)ticks / Stopwatch.Frequency);
            }
        }

        /// <param name="macro">Copied: editing the macro during a run changes nothing.</param>
        /// <returns>false if a run is already in progress.</returns>
        public bool Start(Macro macro, Func<MacroStep, MacroCheckResult> guard, int countdownMs = 3000)
        {
            lock (gate)
            {
                if (worker != null && worker.IsAlive && (Phase != MacroPhase.Idle || !worker.Join(250))) return false;
                Macro copy = macro.Clone();
                Volatile.Write(ref stopRequest, (int)MacroStopReason.Manual);
                Volatile.Write(ref iteration, 1);
                Volatile.Write(ref stepIndex, 0);
                Interlocked.Exchange(ref countdownEnd, Stopwatch.GetTimestamp() + Ticks(countdownMs));
                cancel?.Dispose();
                cancel = new CancellationTokenSource();
                CancellationToken runToken = cancel.Token;
                Volatile.Write(ref phase, (int)MacroPhase.Countdown); // busy before the thread runs
                worker = new Thread(() => Run(copy, guard, countdownMs, runToken))
                {
                    IsBackground = true,
                    Name = "V-Clicker macro",
                    Priority = ThreadPriority.AboveNormal
                };
                worker.Start();
                return true;
            }
        }

        /// <summary>Ends the current run; <paramref name="reason"/> is what <see cref="Stopped"/> reports (first request wins).</summary>
        public void Stop(MacroStopReason reason = MacroStopReason.Manual)
        {
            lock (gate)
            {
                if (cancel == null || cancel.IsCancellationRequested) return;
                Volatile.Write(ref stopRequest, (int)reason);
                cancel.Cancel();
            }
        }

        public bool WaitForStop(int timeoutMs)
        {
            Thread current;
            lock (gate) current = worker;
            return current == null || current.Join(timeoutMs);
        }

        /// <summary>Thrown inside a run to end it; never escapes the worker.</summary>
        private sealed class EndRun : Exception
        {
            public readonly MacroStopReason Reason;
            public EndRun(MacroStopReason reason) { Reason = reason; }
        }

        private void Run(Macro run, Func<MacroStep, MacroCheckResult> check, int countdownMs, CancellationToken runToken)
        {
            macro = run;
            guard = check;
            token = runToken;
            shift = 0;
            MacroStopReason reason = MacroStopReason.Completed;
            Native.timeBeginPeriod(1);
            IDisposable dpi = MacroTarget.PerMonitorDpi();
            try
            {
                if (macro.HasUnknown) throw new EndRun(MacroStopReason.Unsupported);
                if (macro.Steps.Count == 0) throw new EndRun(MacroStopReason.Completed);
                if (token.WaitHandle.WaitOne(Math.Max(0, countdownMs))) throw new EndRun(Requested);
                Volatile.Write(ref phase, (int)MacroPhase.Running);
                for (int pass = 1; macro.Repeat == 0 || pass <= macro.Repeat; pass++)
                {
                    Volatile.Write(ref iteration, pass);
                    RunOnce();
                }
            }
            catch (EndRun end)
            {
                reason = end.Reason;
            }
            catch (Exception)
            {
                // Never let a fault kill the app from this thread: stop and report it like refused input.
                reason = MacroStopReason.Blocked;
            }
            finally
            {
                ReleaseAll();
                dpi.Dispose();
                Native.timeEndPeriod(1);
                Volatile.Write(ref phase, (int)MacroPhase.Idle);
                Stopped?.Invoke(reason);
            }
        }

        private MacroStopReason Requested => (MacroStopReason)Volatile.Read(ref stopRequest);

        private void RunOnce()
        {
            long begin = Stopwatch.GetTimestamp();
            long due = begin; // nominal start of the next step, without pauses
            shift = 0;
            for (int i = 0; i < macro.Steps.Count; i++)
            {
                Volatile.Write(ref stepIndex, i);
                MacroStep step = macro.Steps[i];
                if (step.Kind == StepKind.Wait)
                {
                    due += Ticks(step.DurationMs);
                    SleepUntil(due);
                    continue;
                }
                SleepUntil(due);
                long start = due;
                Perform(step, start);
                due = start + Ticks(NominalMs(step) + StepGapMs);
                // A step that ran late pushes only the next one (it starts after the minimum gap), never the whole schedule.
                long now = Stopwatch.GetTimestamp() - shift;
                due = Math.Max(due, now + Ticks(StepGapMs));
            }
            // The gap after the last step separates repetitions too (and a macro of zero-length waits never spins).
            SleepUntil(Math.Max(due, begin + Ticks(StepGapMs)));
        }

        /// <summary>How long an action takes by itself on the schedule (before the gap that follows it).</summary>
        internal static int NominalMs(MacroStep step)
        {
            switch (step.Kind)
            {
                case StepKind.Type: return Math.Max(0, step.Text.Length - 1) * CharGapMs;
                case StepKind.Key: return (Math.Max(1, step.Count) - 1) * StepGapMs;
                case StepKind.Drag: return DragMs;
                default: return 0;
            }
        }

        private void Perform(MacroStep step, long start)
        {
            switch (step.Kind)
            {
                case StepKind.Click:
                case StepKind.DoubleClick:
                {
                    MacroCheckResult at = Check(step);
                    Do(sink.MoveTo(at.Screen));
                    Press(step.Button);
                    if (step.Kind == StepKind.DoubleClick) Press(step.Button);
                    break;
                }
                case StepKind.Hover:
                    Do(sink.MoveTo(Check(step).Screen));
                    break;
                case StepKind.Scroll:
                    Do(sink.MoveTo(Check(step).Screen));
                    Do(sink.Wheel(step.Notches));
                    break;
                case StepKind.Drag:
                {
                    MacroCheckResult at = Check(step);
                    var to = new Point(at.Origin.X + step.To.X, at.Origin.Y + step.To.Y);
                    Do(sink.MoveTo(at.Screen));
                    Button(step.Button, true);
                    for (int k = 1; k <= DragMoves; k++)
                    {
                        SleepUntil(start + Ticks(DragMs * k / DragMoves));
                        // No pausing with the button held: if the window is no longer right, let go and end the run.
                        if (guard(step).Status != MacroCheck.Ok) throw new EndRun(MacroStopReason.Interrupted);
                        Do(sink.MoveTo(new Point(at.Screen.X + (to.X - at.Screen.X) * k / DragMoves,
                                                 at.Screen.Y + (to.Y - at.Screen.Y) * k / DragMoves)));
                    }
                    Button(step.Button, false);
                    break;
                }
                case StepKind.Type:
                    for (int k = 0; k < step.Text.Length; k++)
                    {
                        if (k > 0) SleepUntil(start + Ticks(k * CharGapMs));
                        Check(step); // before every character: a switch mid-text pauses before the next one
                        Do(sink.Char(step.Text[k]));
                    }
                    break;
                case StepKind.Key:
                    for (int k = 0; k < Math.Max(1, step.Count); k++)
                    {
                        if (k > 0) SleepUntil(start + Ticks(k * StepGapMs));
                        Check(step);
                        Chord(step.Key);
                    }
                    break;
                case StepKind.Shortcut:
                    Check(step);
                    Chord(step.Key);
                    break;
                case StepKind.Password:
                    throw new EndRun(MacroStopReason.Password);
                default:
                    throw new EndRun(MacroStopReason.Unsupported);
            }
        }

        private void Press(ClickButton button)
        {
            Button(button, true);
            Button(button, false);
        }

        private void Button(ClickButton button, bool down)
        {
            if (down) buttonsDown.Add(button);
            bool sent = sink.Button(button, down);
            if (!down && sent) buttonsDown.Remove(button);
            Do(sent);
        }

        /// <summary>Modifiers down, the key down and up, modifiers up (in reverse).</summary>
        private void Chord(Keys key)
        {
            var modifiers = new List<Keys>(3);
            if ((key & Keys.Control) != 0) modifiers.Add(Keys.ControlKey);
            if ((key & Keys.Alt) != 0) modifiers.Add(Keys.Menu);
            if ((key & Keys.Shift) != 0) modifiers.Add(Keys.ShiftKey);
            foreach (Keys modifier in modifiers) KeyEvent(modifier, true);
            KeyEvent(key & Keys.KeyCode, true);
            KeyEvent(key & Keys.KeyCode, false);
            for (int m = modifiers.Count - 1; m >= 0; m--) KeyEvent(modifiers[m], false);
        }

        private void KeyEvent(Keys code, bool down)
        {
            if (down) keysDown.Add(code);
            bool sent = sink.Key(code, down);
            if (!down && sent) keysDown.Remove(code);
            Do(sent);
        }

        /// <summary>Lets go of every key and button this run pressed (after a stop, a fault or a refused input).</summary>
        private void ReleaseAll()
        {
            try
            {
                foreach (Keys code in keysDown) sink.Key(code, false);
                foreach (ClickButton button in buttonsDown) sink.Button(button, false);
            }
            catch (Exception)
            {
                // Best effort while ending a run.
            }
            keysDown.Clear();
            buttonsDown.Clear();
        }

        private static void Do(bool sent)
        {
            if (!sent) throw new EndRun(MacroStopReason.Blocked);
        }

        /// <summary>
        /// Asks the guard about the step; while it says no, the run is paused here until it says yes for
        /// <see cref="ClickEngine.ResumeSettleMs"/> (the user may still be switching back), the run is stopped,
        /// the window is closed or the pause limit is reached.
        /// </summary>
        private MacroCheckResult Check(MacroStep step)
        {
            if (token.IsCancellationRequested) throw new EndRun(Requested);
            MacroCheckResult result = guard(step);
            if (result.Status == MacroCheck.Ok) return result;
            if (result.Status == MacroCheck.Closed) throw new EndRun(MacroStopReason.TargetClosed);

            long pauseStart = Stopwatch.GetTimestamp();
            long limit = Ticks(PauseLimitMs), settle = Ticks(ClickEngine.ResumeSettleMs);
            long backSince = 0;
            Volatile.Write(ref pauseCause, (int)result.Status);
            Volatile.Write(ref phase, (int)MacroPhase.Paused);
            try
            {
                while (true)
                {
                    if (token.WaitHandle.WaitOne(PausePollMs)) throw new EndRun(Requested);
                    long now = Stopwatch.GetTimestamp();
                    if (now - pauseStart >= limit) throw new EndRun(MacroStopReason.PauseTimeout);
                    result = guard(step);
                    if (result.Status == MacroCheck.Ok)
                    {
                        if (backSince == 0) backSince = now;
                        if (now - backSince >= settle)
                        {
                            result = guard(step); // the point may have moved with the window while settling
                            if (result.Status == MacroCheck.Ok) return result;
                        }
                        continue;
                    }
                    backSince = 0;
                    if (result.Status == MacroCheck.Closed) throw new EndRun(MacroStopReason.TargetClosed);
                    Volatile.Write(ref pauseCause, (int)result.Status);
                }
            }
            finally
            {
                shift += Stopwatch.GetTimestamp() - pauseStart;
                Volatile.Write(ref phase, (int)MacroPhase.Running);
            }
        }

        /// <summary>Waits until <paramref name="due"/> on the schedule (plus time paused); a stop wakes it at once.</summary>
        private void SleepUntil(long due)
        {
            while (true)
            {
                long remaining = due + shift - Stopwatch.GetTimestamp();
                if (remaining <= 0) return;
                int ms = (int)Math.Min(int.MaxValue, Math.Max(1, remaining * 1000 / Stopwatch.Frequency));
                if (token.WaitHandle.WaitOne(ms)) throw new EndRun(Requested);
            }
        }

        private static long Ticks(double ms) => (long)(ms * (Stopwatch.Frequency / 1000.0));

        public void Dispose()
        {
            Stop();
            bool finished = WaitForStop(2000);
            lock (gate)
            {
                if (finished)
                {
                    cancel?.Dispose();
                    cancel = null;
                }
            }
        }
    }
}
