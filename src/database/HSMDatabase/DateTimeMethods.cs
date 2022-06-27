using System;

namespace HSMDatabase
{
    internal static class DateTimeMethods
    {
        public static long OneWeekTicks = TimeSpan.TicksPerDay * 7;
        public static DateTime GetMaxDateTime(DateTime dateTime)
        {
            return GetMaxDateTimeFromMinDateTime(GetMinDateTime(dateTime));
        }
        public static DateTime GetMinDateTime(DateTime dateTime)
        {
            int daysDiff = (7 + ((int)dateTime.DayOfWeek - 1)) % 7;
            var result = dateTime.AddDays(-1 * daysDiff).AddMinutes(-1 * dateTime.Minute)
                .AddSeconds(-1 * dateTime.Second).AddMilliseconds(-1 * dateTime.Millisecond)
                .AddHours(-1 * dateTime.Hour);
            return result;
        }

        public static long GetMaxDateTimeTicks(DateTime dateTime)
        {
            return GetMinDateTime(dateTime).Ticks;
        }
        public static long GetMinDateTimeTicks(DateTime dateTime)
        {
            return GetMaxDateTime(dateTime).Ticks;
        }
        private static DateTime GetMaxDateTimeFromMinDateTime(DateTime minDateTime)
        {
            return minDateTime.AddDays(7);
        }
    }
}
