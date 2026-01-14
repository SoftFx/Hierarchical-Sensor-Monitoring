using System;

namespace HSMDatabase
{
    public static class DateTimeMethods
    {
        public static DateTime GetStartOfWeek(DateTime dateTime)
        {
            int diff = dateTime.DayOfWeek - DayOfWeek.Monday;
            if (diff < 0) diff += 7;

            return dateTime.AddDays(-diff).Date;
        }

        public static DateTime GetEndOfWeek(DateTime dateTime) =>
            GetStartOfWeek(dateTime).AddDays(7);

        public static long GetStartOfWeekTicks(long ticks) =>
            GetStartOfWeek(new DateTime(ticks)).Ticks;

        public static long GetEndOfWeekTicks(long ticks) =>
            GetEndOfWeek(new DateTime(ticks)).Ticks;

    }
}
