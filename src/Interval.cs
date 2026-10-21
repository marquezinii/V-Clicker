using System;

namespace VClicker
{
    /// <summary>Conversions between a total interval in milliseconds and the h / min / s / ms fields.</summary>
    internal static class Interval
    {
        public const int MinMs = 1;
        public const int MaxMs = 24 * 60 * 60 * 1000 - 1;

        public static int Clamp(long totalMs) => (int)Math.Max(MinMs, Math.Min(MaxMs, totalMs));

        public static int Join(int hours, int minutes, int seconds, int milliseconds) =>
            Clamp(((hours * 60L + minutes) * 60 + seconds) * 1000 + milliseconds);

        public static void Split(int totalMs, out int hours, out int minutes, out int seconds, out int milliseconds)
        {
            totalMs = Clamp(totalMs);
            milliseconds = totalMs % 1000;
            seconds = totalMs / 1000 % 60;
            minutes = totalMs / 60000 % 60;
            hours = totalMs / 3600000;
        }

        public static double ClicksPerSecond(int totalMs) => 1000.0 / Clamp(totalMs);
    }
}
