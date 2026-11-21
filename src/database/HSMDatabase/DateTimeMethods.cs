using System;
using HSMDatabase.AccessManager;

namespace HSMDatabase
{
    public static class DateTimeMethods
    {
        // The week formula lives in StorageWeek, where HSMServer.Core can reach it too.
        public static DateTime GetStartOfWeek(DateTime dateTime) => StorageWeek.GetStart(dateTime);

        public static DateTime GetEndOfWeek(DateTime dateTime) =>
            GetStartOfWeek(dateTime).AddDays(7);

        public static long GetStartOfWeekTicks(long ticks) =>
            StorageWeek.GetStartTicks(ticks);

        public static long GetEndOfWeekTicks(long ticks) =>
            GetEndOfWeek(new DateTime(ticks)).Ticks;

    }
}
