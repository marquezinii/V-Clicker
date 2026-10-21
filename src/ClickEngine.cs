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
        Blocked
    }

    internal enum EnginePhase { Idle, Countdown, Clicking }

    /// <summary>Everything the engine needs for one run. Immutable once started.</summary>
    internal sealed class ClickPlan
    {
        public int IntervalMs = 100;
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
    /// "catches up" with a burst of clicks after a stall.
    /// </summary>
    internal sealed class ClickEngine : IDisposable
    {
        private readonly IClickSink sink;
        private readonly Func<int, bool> isKeyDown;
        private readonly object gate = new object();

        private Thread worker;
        private CancellationTokenSource cancel;
        private long clicks;
        private int phase;
        private long clickingSince;
        private long countdownEnd;
        private long finalElapsedTicks;

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

        public long Clicks => Interlocked.Read(ref clicks);

        /// <summary>Time spent clicking in the current (or last) run, excluding the start countdown.</summary>
        public TimeSpan Elapsed
        {
            get
            {
                long since = Interlocked.Read(ref clickingSince);
                if (since == 0) return TimeSpan.Zero;
                long ticks = Phase == EnginePhase.Clicking
                    ? Stopwatch.GetTimestamp() - since
                    : Interlocked.Read(ref finalElapsedTicks);
                return TimeSpan.FromSeconds((double)ticks / Stopwatch.Frequency);
            }
        }

        public TimeSpan CountdownRemaining
        {
            get
            {
                if (Phase != EnginePhase.Countdown) return TimeSpan.Zero;
                long ticks = Interlocked.Read(ref countdownEnd) - Stopwatch.GetTimestamp();
                return ticks <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds((double)ticks / Stopwatch.Frequency);
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
                Interlocked.Exchange(ref finalElapsedTicks, 0);
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

        public void Stop()
        {
            lock (gate)
            {
                cancel?.Cancel();
            }
        }

        /// <summary>Blocks until the current run (if any) has fully finished.</summary>
        public bool WaitForStop(int timeoutMs)
        {
            Thread current;
            lock (gate) current = worker;
            return current == null || current.Join(timeoutMs);
        }

        private void Run(ClickPlan plan, CancellationToken token)
        {
            StopReason reason = StopReason.Manual;
            Native.timeBeginPeriod(1);
            try
            {
                if (plan.StartDelayMs > 0)
                {
                    Interlocked.Exchange(ref countdownEnd,
                        Stopwatch.GetTimestamp() + MsToTicks(plan.StartDelayMs));
                    if (token.WaitHandle.WaitOne(plan.StartDelayMs)) return;
                }

                long start = Stopwatch.GetTimestamp();
                Interlocked.Exchange(ref clickingSince, start);
                Volatile.Write(ref phase, (int)EnginePhase.Clicking);

                var random = new Random();
                long next = start;
                while (true)
                {
                    if (token.IsCancellationRequested) break;
                    if (plan.HoldKey != 0 && !isKeyDown(plan.HoldKey)) { reason = StopReason.KeyReleased; break; }

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
                long since = Interlocked.Read(ref clickingSince);
                Interlocked.Exchange(ref finalElapsedTicks, since == 0 ? 0 : Stopwatch.GetTimestamp() - since);
                Volatile.Write(ref phase, (int)EnginePhase.Idle);
                Native.timeEndPeriod(1);
                Stopped?.Invoke(reason);
            }
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
                if (token.IsCancellationRequested) return StopReason.Manual;
                if (plan.HoldKey != 0 && !isKeyDown(plan.HoldKey)) return StopReason.KeyReleased;

                double remainingMs = (target - Stopwatch.GetTimestamp()) * 1000.0 / Stopwatch.Frequency;
                if (remainingMs <= 0) return null;
                if (remainingMs > 2.5)
                    // Sleep in slices so a cancel or a released key is noticed quickly,
                    // and wake ~2 ms early to finish with the spin below.
                    token.WaitHandle.WaitOne((int)Math.Max(1, Math.Min(remainingMs - 2, 20)));
                else
                    Thread.SpinWait(40);
            }
        }

        private static long MsToTicks(int ms) => (long)(ms * (Stopwatch.Frequency / 1000.0));

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
