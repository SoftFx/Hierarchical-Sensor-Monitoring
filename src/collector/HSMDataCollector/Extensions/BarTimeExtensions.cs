using System;


namespace HSMDataCollector.Extensions
{
    internal static class BarTimeHelper
    {
        internal static DateTime GetOpenTime(TimeSpan timerPeriod) =>
            new DateTime(GetStartPeriod(timerPeriod));

        internal static TimeSpan GetTimerDueTime(TimeSpan timerPeriod)
        {
            var start = GetStartPeriod(timerPeriod);
            var end = start + timerPeriod.Ticks;
            var dueTime = new DateTime(end) - DateTime.UtcNow;

            return dueTime >= TimeSpan.Zero ? dueTime : TimeSpan.Zero;
        }

        private static long GetStartPeriod(TimeSpan timerPeriod)
        {
            var now = DateTime.UtcNow.Ticks;
            var period = timerPeriod.Ticks;

            return now / period * period;
        }
    }
}