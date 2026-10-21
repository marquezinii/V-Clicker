using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;

namespace VClicker
{
    /// <summary>Declaration order is the order of the UI segments (Segmented index == enum value).</summary>
    internal enum ClickButton { Left, Middle, Right }

    internal enum StopReason
    {
        /// <summary>Stopped by the user (hotkey, button or window close).</summary>
        Manual,
        /// <summary>The configured number of clicks was reached.</summary>
        Completed,
        /// <summary>Hold mode: the hotkey was released.</summary>
        KeyReleased,
        /// <summary>Windows refused the synthetic input (for example the target app runs elevated).</summary>
        Blocked,
        /// <summary>Window Lock in strict mode: the locked window lost the focus.</summary>
        FocusLost,
        /// <summary>Window Lock: the locked window was closed.</summary>
        TargetClosed,
        /// <summary>Window Lock: paused for longer than <see cref="ClickPlan.PauseLimitMs"/>.</summary>
        PauseTimeout,
        /// <summary>The computer was suspended, hibernated or locked.</summary>
        Suspended
    }

    internal enum EnginePhase { Idle, Countdown, Clicking, Paused }

    /// <summary>What Window Lock says about the next click.</summary>
    internal enum LockStatus
    {
        Ok,
        /// <summary>Another window is in front: pause (or stop, in strict mode).</summary>
        NotFocused,
        /// <summary>The fixed click point is not inside the locked window: pause.</summary>
        PointOutside,
        /// <summary>The locked window no longer exists: stop.</summary>
        Closed
    }

    /// <summary>Everything the engine needs for one run. Immutable once started.</summary>
    internal sealed class ClickPlan
    {
        public double IntervalMs = 100;
        public int JitterPercent;
        public ClickButton Button = ClickButton.Left;
        public bool DoubleClick;
        /// <summary>0 = unlimited.</summary>
        public long MaxClicks;
        /// <summary>null = click wherever the cursor currently is.</summary>
        public Point? FixedPosition;
        public int StartDelayMs;
        /// <summary>0 = toggle mode; otherwise the virtual key that must stay pressed.</summary>
        public int HoldKey;
        /// <summary>Window Lock, asked right before every click with the fixed position (if any). null = no lock.</summary>
        public Func<Point?, LockStatus> Lock;
        /// <summary>Window Lock: stop instead of pausing when the locked window loses the focus.</summary>
        public bool StopOnFocusLoss;
        /// <summary>A pause longer than this ends the run (<see cref="StopReason.PauseTimeout"/>).</summary>
        public int PauseLimitMs = 5 * 60 * 1000;
    }

    /// <summary>Where clicks go. The real one calls SendInput; tests use a fake.</summary>
    internal interface IClickSink
    {
        /// <returns>false if the click could not be delivered.</returns>
        bool Click(ClickButton button, bool doubleClick, Point? at);
    }

    internal sealed class SendInputSink : IClickSink
    {
        private static readonly int InputSize = Marshal.SizeOf(typeof(Native.INPUT));
        // [button][0 = single, 1 = double]
        private readonly Native.INPUT[][][] cache = new Native.INPUT[3][][];

        public bool Click(ClickButton button, bool doubleClick, Point? at)
        {
            if (at.HasValue) Native.SetCursorPos(at.Value.X, at.Value.Y);

            Native.INPUT[] inputs = Build(button, doubleClick);
            if (Native.SendInput((uint)inputs.Length, inputs, InputSize) == inputs.Length) return true;

            // Partially delivered: make sure the button does not stay held down.
            Native.INPUT release = inputs[1];
            Native.SendInput(1, new[] { release }, InputSize);
            return false;
        }

        /// <summary>The SendInput press / release flags of a mouse button.</summary>
        internal static void Flags(ClickButton button, out uint down, out uint up)
        {
            switch (button)
            {
                case ClickButton.Right:
                    down = Native.MOUSEEVENTF_RIGHTDOWN; up = Native.MOUSEEVENTF_RIGHTUP; break;
                case ClickButton.Middle:
                    down = Native.MOUSEEVENTF_MIDDLEDOWN; up = Native.MOUSEEVENTF_MIDDLEUP; break;
                default:
                    down = Native.MOUSEEVENTF_LEFTDOWN; up = Native.MOUSEEVENTF_LEFTUP; break;
            }
        }

        private Native.INPUT[] Build(ClickButton button, bool doubleClick)
        {
            Flags(button, out uint down, out uint up);

            // One cached array per (button, single/double): the click loop allocates nothing.
            int slot = (int)button;
            if (cache[slot] == null) cache[slot] = new Native.INPUT[2][];
            int variant = doubleClick ? 1 : 0;
            if (cache[slot][variant] == null)
            {
                cache[slot][variant] = doubleClick
                    ? new[] { Native.Mouse(down), Native.Mouse(up), Native.Mouse(down), Native.Mouse(up) }
                    : new[] { Native.Mouse(down), Native.Mouse(up) };
            }
            return cache[slot][variant];
        }
    }

    /// <summary>
    /// Runs the click loop on a dedicated high-priority thread. Timing uses an absolute schedule
    /// (no drift), a coarse wait plus a short spin for sub-millisecond accuracy, and never
    /// "catches up" with a burst of clicks after a stall or a Window Lock pause.
    /// </summary>
    internal sealed class ClickEngine : IDisposable
    {
        /// <summary>How often a paused run looks at the locked window again.</summary>
        private const int PausePollMs = 25;

        private readonly IClickSink sink;
        private readonly Func<int, bool> isKeyDown;
        private readonly object gate = new object();

        private Thread worker;
        private CancellationTokenSource cancel;
        private long clicks;
        private int phase;
        private int pauseCause;
        private int stopRequest;
        private long sessionSince;
        private long clickingSince;
        private long countdownEnd;
        private long pausedTicks;
        private long pauseSince;
        private long finalElapsedTicks;
        private long finalSessionTicks;

        static ClickEngine()
        {
            Native.DisablePowerThrottling();
        }

        public ClickEngine(IClickSink sink, Func<int, bool> isKeyDown = null)
        {
            this.sink = sink;
            this.isKeyDown = isKeyDown ?? (key => (Native.GetAsyncKeyState(key) & 0x8000) != 0);
        }

        /// <summary>Raised on the worker thread when a run ends. Marshal to the UI thread yourself.</summary>
        public event Action<StopReason> Stopped;

        public EnginePhase Phase => (EnginePhase)Volatile.Read(ref phase);

        /// <summary>Why the run is paused; meaningful while <see cref="Phase"/> is <see cref="EnginePhase.Paused"/>.</summary>
        public LockStatus PauseCause => (LockStatus)Volatile.Read(ref pauseCause);

        public long Clicks => Interlocked.Read(ref clicks);

        /// <summary>Time spent clicking in the current (or last) run: no start countdown, no pauses.</summary>
        public TimeSpan Elapsed
        {
            get
            {
                long since = Interlocked.Read(ref clickingSince);
                if (since == 0) return TimeSpan.Zero;
                if (Phase == EnginePhase.Idle) return Span(Interlocked.Read(ref finalElapsedTicks));
                long now = Stopwatch.GetTimestamp();
                long pausedNow = Interlocked.Read(ref pauseSince);
                long ticks = now - since - Interlocked.Read(ref pausedTicks) - (pausedNow == 0 ? 0 : now - pausedNow);
                return Span(Math.Max(0, ticks));
            }
        }

        /// <summary>Time since the run was started, countdown and pauses included.</summary>
        public TimeSpan SessionElapsed
        {
            get
            {
                long since = Interlocked.Read(ref sessionSince);
                if (since == 0) return TimeSpan.Zero;
                return Span(Phase == EnginePhase.Idle ? Interlocked.Read(ref finalSessionTicks) : Stopwatch.GetTimestamp() - since);
            }
        }

        public TimeSpan CountdownRemaining
        {
            get
            {
                if (Phase != EnginePhase.Countdown) return TimeSpan.Zero;
                long ticks = Interlocked.Read(ref countdownEnd) - Stopwatch.GetTimestamp();
                return ticks <= 0 ? TimeSpan.Zero : Span(ticks);
            }
        }

        /// <returns>false if a run is already in progress.</returns>
        public bool Start(ClickPlan plan)
        {
            lock (gate)
            {
                if (worker != null && worker.IsAlive)
                {
                    // A run that just reported Idle may still be returning from its thread; only a live run refuses.
                    if (Phase != EnginePhase.Idle || !worker.Join(250)) return false;
                }
                Interlocked.Exchange(ref clicks, 0);
                Interlocked.Exchange(ref clickingSince, 0);
                Interlocked.Exchange(ref pausedTicks, 0);
                Interlocked.Exchange(ref pauseSince, 0);
                Interlocked.Exchange(ref finalElapsedTicks, 0);
                Interlocked.Exchange(ref finalSessionTicks, 0);
                Interlocked.Exchange(ref sessionSince, Stopwatch.GetTimestamp());
                Volatile.Write(ref stopRequest, (int)StopReason.Manual);
                cancel?.Dispose();
                cancel = new CancellationTokenSource();
                CancellationToken token = cancel.Token;
                // Mark busy before the thread runs so a second Start() cannot slip in.
                Volatile.Write(ref phase, (int)EnginePhase.Countdown);
                worker = new Thread(() => Run(plan, token))
                {
                    IsBackground = true,
                    Name = "V-Clicker engine",
                    Priority = ThreadPriority.AboveNormal
                };
                worker.Start();
                return true;
            }
        }

        /// <summary>Ends the current run; <paramref name="reason"/> is what <see cref="Stopped"/> reports (first request wins).</summary>
        public void Stop(StopReason reason = StopReason.Manual)
        {
            lock (gate)
            {
                if (cancel == null || cancel.IsCancellationRequested) return;
                Volatile.Write(ref stopRequest, (int)reason);
                cancel.Cancel();
            }
        }

        /// <summary>Blocks until the current run (if any) has fully finished.</summary>
        public bool WaitForStop(int timeoutMs)
        {
            Thread current;
            lock (gate) current = worker;
            return current == null || current.Join(timeoutMs);
        }

        private StopReason Requested => (StopReason)Volatile.Read(ref stopRequest);

        private void Run(ClickPlan plan, CancellationToken token)
        {
            StopReason reason = StopReason.Manual;
            Native.timeBeginPeriod(1);
            try
            {
                if (plan.StartDelayMs > 0)
                {
                    StopReason? interrupted = Countdown(plan, token);
                    if (interrupted.HasValue) { reason = interrupted.Value; return; }
                }

                long start = Stopwatch.GetTimestamp();
                Interlocked.Exchange(ref clickingSince, start);
                Volatile.Write(ref phase, (int)EnginePhase.Clicking);

                var random = new Random();
                long next = start;
                while (true)
                {
                    if (token.IsCancellationRequested) { reason = Requested; break; }
                    if (plan.HoldKey != 0 && !isKeyDown(plan.HoldKey)) { reason = StopReason.KeyReleased; break; }

                    // Window Lock is asked right before every click; a pause blocks inside AwaitLock.
                    StopReason? locked = AwaitLock(plan, token, out bool resumed);
                    if (locked.HasValue) { reason = locked.Value; break; }
                    if (resumed) next = Stopwatch.GetTimestamp(); // clicks missed while paused are dropped, not replayed
                    else if (Stopwatch.GetTimestamp() < next)
                    {
                        // The wait ended early on a lock miss that cleared by itself: finish the wait, never click early.
                        StopReason? early = WaitUntil(next, plan, token);
                        if (early.HasValue) { reason = early.Value; break; }
                        continue;
                    }

                    if (!sink.Click(plan.Button, plan.DoubleClick, plan.FixedPosition))
                    {
                        reason = StopReason.Blocked;
                        break;
                    }

                    long count = Interlocked.Increment(ref clicks);
                    if (plan.MaxClicks > 0 && count >= plan.MaxClicks) { reason = StopReason.Completed; break; }

                    next += NextIntervalTicks(plan, random);
                    long now = Stopwatch.GetTimestamp();
                    if (next < now) next = now; // fell behind (system stall): do not burst to catch up

                    StopReason? interrupted = WaitUntil(next, plan, token);
                    if (interrupted.HasValue) { reason = interrupted.Value; break; }
                }
            }
            finally
            {
                long end = Stopwatch.GetTimestamp();
                long since = Interlocked.Read(ref clickingSince);
                Interlocked.Exchange(ref finalElapsedTicks,
                    since == 0 ? 0 : Math.Max(0, end - since - Interlocked.Read(ref pausedTicks)));
                Interlocked.Exchange(ref finalSessionTicks, end - Interlocked.Read(ref sessionSince));
                Volatile.Write(ref phase, (int)EnginePhase.Idle);
                Native.timeEndPeriod(1);
                Stopped?.Invoke(reason);
            }
        }

        /// <summary>The start delay. Time spent paused by Window Lock does not count: the countdown freezes.</summary>
        private StopReason? Countdown(ClickPlan plan, CancellationToken token)
        {
            long remaining = MsToTicks(plan.StartDelayMs);
            long last = Stopwatch.GetTimestamp();
            Interlocked.Exchange(ref countdownEnd, last + remaining);
            while (true)
            {
                StopReason? locked = AwaitLock(plan, token, out bool resumed);
                if (locked.HasValue) return locked;
                long now = Stopwatch.GetTimestamp();
                if (!resumed) remaining -= now - last;
                last = now;
                if (remaining <= 0) return null;
                Interlocked.Exchange(ref countdownEnd, now + remaining);
                if (token.WaitHandle.WaitOne((int)Math.Max(1, Math.Min(TicksToMs(remaining), 20)))) return Requested;
            }
        }

        /// <summary>
        /// Asks Window Lock whether the next click may go out. While it says no, the run is paused here
        /// (polling, not spinning) until the window is back, the run is stopped, or the pause limit is reached.
        /// </summary>
        /// <returns>null to go on (<paramref name="resumed"/> tells whether a pause happened); otherwise why the run ends.</returns>
        private StopReason? AwaitLock(ClickPlan plan, CancellationToken token, out bool resumed)
        {
            resumed = false;
            if (plan.Lock == null) return null;
            LockStatus status = plan.Lock(plan.FixedPosition);
            if (status == LockStatus.Ok) return null;
            StopReason? end = Verdict(status, plan);
            if (end.HasValue) return end;

            EnginePhase before = Phase;
            long pauseStart = Stopwatch.GetTimestamp();
            long limit = MsToTicks(plan.PauseLimitMs);
            if (before == EnginePhase.Clicking) Interlocked.Exchange(ref pauseSince, pauseStart);
            Volatile.Write(ref pauseCause, (int)status);
            Volatile.Write(ref phase, (int)EnginePhase.Paused);
            try
            {
                while (true)
                {
                    if (token.WaitHandle.WaitOne(PausePollMs)) return Requested;
                    if (plan.HoldKey != 0 && !isKeyDown(plan.HoldKey)) return StopReason.KeyReleased;
                    // Measured from when the pause began; repeated focus changes do not restart it.
                    if (Stopwatch.GetTimestamp() - pauseStart >= limit) return StopReason.PauseTimeout;
                    status = plan.Lock(plan.FixedPosition);
                    if (status == LockStatus.Ok)
                    {
                        resumed = true;
                        return null;
                    }
                    end = Verdict(status, plan);
                    if (end.HasValue) return end;
                    Volatile.Write(ref pauseCause, (int)status);
                }
            }
            finally
            {
                if (before == EnginePhase.Clicking)
                {
                    Interlocked.Add(ref pausedTicks, Stopwatch.GetTimestamp() - pauseStart);
                    Interlocked.Exchange(ref pauseSince, 0);
                }
                if (resumed) Volatile.Write(ref phase, (int)before);
            }
        }

        /// <summary>The Window Lock answers that end the run instead of pausing it.</summary>
        private static StopReason? Verdict(LockStatus status, ClickPlan plan)
        {
            if (status == LockStatus.Closed) return StopReason.TargetClosed;
            if (status == LockStatus.NotFocused && plan.StopOnFocusLoss) return StopReason.FocusLost;
            return null;
        }

        /// <summary>Interval to the next click in Stopwatch ticks, with optional random jitter.</summary>
        internal static long NextIntervalTicks(ClickPlan plan, Random random)
        {
            double ticks = MsToTicks(plan.IntervalMs);
            if (plan.JitterPercent > 0)
            {
                double spread = plan.JitterPercent / 100.0;
                ticks *= 1.0 + (random.NextDouble() * 2.0 - 1.0) * spread;
            }
            return Math.Max(1, (long)ticks);
        }

        private StopReason? WaitUntil(long target, ClickPlan plan, CancellationToken token)
        {
            while (true)
            {
                if (token.IsCancellationRequested) return Requested;
                if (plan.HoldKey != 0 && !isKeyDown(plan.HoldKey)) return StopReason.KeyReleased;

                double remainingMs = (target - Stopwatch.GetTimestamp()) * 1000.0 / Stopwatch.Frequency;
                if (remainingMs <= 0) return null;
                if (remainingMs > 2.5)
                {
                    // Lost the locked window mid-wait: hand back to the loop so it pauses now, not at the next click.
                    if (plan.Lock != null && plan.Lock(plan.FixedPosition) != LockStatus.Ok) return null;
                    // Sleep in slices so a cancel or a released key is noticed quickly,
                    // and wake ~2 ms early to finish with the spin below.
                    token.WaitHandle.WaitOne((int)Math.Max(1, Math.Min(remainingMs - 2, 20)));
                }
                else
                    Thread.SpinWait(40);
            }
        }

        private static long MsToTicks(double ms) => (long)(ms * (Stopwatch.Frequency / 1000.0));

        private static double TicksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        private static TimeSpan Span(long ticks) => TimeSpan.FromSeconds((double)ticks / Stopwatch.Frequency);

        public void Dispose()
        {
            Stop();
            // If the worker somehow outlives the wait, leave the token source alone rather than
            // pull it out from under a thread that is still waiting on it.
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
