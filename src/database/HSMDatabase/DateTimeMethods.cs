using System;

namespace HSMDatabase
{
    internal static class DateTimeMethods
    {
        public static DateTime GetMaxDateTime(DateTime dateTime) =>
            GetMaxDateTimeFromMinDateTime(GetMinDateTime(dateTime));

        public static DateTime GetMinDateTime(DateTime dateTime)
        {
            int daysDiff = (7 + ((int)dateTime.DayOfWeek - 1)) % 7;
            var result = dateTime.AddDays(-1 * daysDiff).AddMinutes(-1 * dateTime.Minute)
                .AddSeconds(-1 * dateTime.Second).AddMilliseconds(-1 * dateTime.Millisecond)
                .AddHours(-1 * dateTime.Hour);
            return result;
        }

        public static long GetMaxDateTimeTicks(long ticks) =>
            GetMaxDateTime(new DateTime(ticks)).Ticks;

        public static long GetMinDateTimeTicks(long ticks) =>
            GetMinDateTime(new DateTime(ticks)).Ticks;

        private static DateTime GetMaxDateTimeFromMinDateTime(DateTime minDateTime) =>
            minDateTime.AddDays(7);
    }
}
