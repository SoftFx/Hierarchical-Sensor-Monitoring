using System;

namespace HSMDataCollector.Extensions
{
    internal static class BarTimeExtensions
    {
        internal static DateTime GetOpenTime(this TimeSpan timerPeriod) =>
            new DateTime(GetStartPeriod(timerPeriod));

        internal static TimeSpan GetTimerDueTime(this TimeSpan timerPeriod)
        {
            var start = timerPeriod.GetStartPeriod();
            var end = start + timerPeriod.Ticks;
            var dueTime = new DateTime(end) - DateTime.UtcNow;

            return dueTime >= TimeSpan.Zero ? dueTime : TimeSpan.Zero;
        }

        private static long GetStartPeriod(this TimeSpan timerPeriod)
        {
            var now = DateTime.UtcNow.Ticks;
            var period = timerPeriod.Ticks;

            return now / period * period;
        }
    }
}
