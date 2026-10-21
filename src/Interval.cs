using System;

namespace VClicker
{
    /// <summary>
    /// Conversions between a total interval in milliseconds (fractional, so 15 clicks/s is exactly 66.667 ms),
    /// the h / min / s / ms fields and clicks per second.
    /// </summary>
    internal static class Interval
    {
        public const double MinMs = 1;
        public const double DefaultMs = 100;
        public const double MaxMs = 24 * 60 * 60 * 1000 - 1;
        public const double MinCps = 0.1;
        public const double MaxCps = 1000;

        public static double Clamp(double totalMs) =>
            double.IsNaN(totalMs) ? DefaultMs : Math.Max(MinMs, Math.Min(MaxMs, totalMs)); // never the fastest by accident

        public static double Join(int hours, int minutes, int seconds, double milliseconds) =>
            Clamp(((hours * 60.0 + minutes) * 60 + seconds) * 1000 + milliseconds);

        public static void Split(double totalMs, out int hours, out int minutes, out int seconds, out double milliseconds)
        {
            totalMs = Clamp(totalMs);
            long whole = (long)Math.Floor(totalMs);
            milliseconds = whole % 1000 + (totalMs - whole);
            seconds = (int)(whole / 1000 % 60);
            minutes = (int)(whole / 60000 % 60);
            hours = (int)(whole / 3600000);
        }

        public static double ClicksPerSecond(double totalMs) => 1000.0 / Clamp(totalMs);

        public static double FromCps(double clicksPerSecond) =>
            Clamp(1000.0 / Math.Max(MinCps, Math.Min(MaxCps, clicksPerSecond)));
    }
}
