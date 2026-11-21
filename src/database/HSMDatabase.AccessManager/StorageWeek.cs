using System;

namespace HSMDatabase.AccessManager
{
    /// <summary>
    /// The storage week of sensor history: values are kept in one database per week, from
    /// Monday 00:00 to the next Monday 00:00, chosen by the row's key time (the UTC ticks of
    /// the value's Time). The single definition of that week: the shard dictionary and the
    /// aggregation fold both use it.
    /// </summary>
    public static class StorageWeek
    {
        // Pure calendar arithmetic on the ticks: DayOfWeek, AddDays and Date never convert
        // between time zones, so the result depends on the ticks alone, whatever the Kind.
        public static DateTime GetStart(DateTime dateTime)
        {
            int diff = dateTime.DayOfWeek - DayOfWeek.Monday;
            if (diff < 0) diff += 7;

            return dateTime.AddDays(-diff).Date;
        }

        public static long GetStartTicks(long ticks) => GetStart(new DateTime(ticks)).Ticks;

        /// <summary>
        /// Whether two key times fall in the same storage week. Compares raw ticks: pass the
        /// times the database keys are built from (<c>BaseValue.Time</c>, which is UTC).
        /// </summary>
        public static bool IsSame(DateTime first, DateTime second) =>
            GetStartTicks(first.Ticks) == GetStartTicks(second.Ticks);
    }
}
