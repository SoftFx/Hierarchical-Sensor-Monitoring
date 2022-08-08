using System;

namespace HSMDatabase
{
    internal static class DateTimeMethods
    {
        public static DateTime GetMaxDateTime(DateTime dateTime) =>
            GetMaxDateTimeFromMinDateTime(GetMinDateTime(dateTime));

        public static DateTime GetMinDateTime(DateTime dateTime)
        {
            int daysDiff = (7 + ((int)dateTime.DayOfWeek - (int)DayOfWeek.Monday)) % 7;
            var result = dateTime.AddDays(-daysDiff)
                                 .AddHours(-dateTime.Hour)
                                 .AddMinutes(-dateTime.Minute)
                                 .AddSeconds(-dateTime.Second)
                                 .AddMilliseconds(-dateTime.Millisecond);

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
