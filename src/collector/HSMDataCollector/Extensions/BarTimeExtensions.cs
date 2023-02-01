using System;

namespace HSMDataCollector.Extensions
{
    internal static class BarTimeExtensions
    {
        internal static DateTime CalculateOpenTime(this TimeSpan timerPeriod) =>
            new DateTime(CalculateStartPeriod(timerPeriod));

        internal static TimeSpan CalculateTimerDueTime(this TimeSpan timerPeriod)
        {
            var start = timerPeriod.CalculateStartPeriod();
            var end = start + timerPeriod.Ticks;
            var dueTime = new DateTime(end) - DateTime.UtcNow;

            return dueTime >= TimeSpan.Zero ? dueTime : TimeSpan.Zero;
        }

        private static long CalculateStartPeriod(this TimeSpan timerPeriod)
        {
            var now = DateTime.UtcNow.Ticks;
            var period = timerPeriod.Ticks;

            return now / period * period;
        }
    }
}
