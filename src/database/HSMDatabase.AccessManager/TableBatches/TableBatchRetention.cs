using System;

namespace HSMDatabase.AccessManager.TableBatches
{
    /// <summary>
    /// When the rows of a week database of the batch store have passed their period (ADR-0021 point 12, #1850): the rows of
    /// the Monday-aligned week of their merge hour are kept for the class's whole weeks after the week has ended, so a row
    /// may outlive its period by up to a week. Retention drops a week database from that moment (part 4), and the merge
    /// leaves such rows out instead of writing them into a week that retention would drop or has dropped (a catch-up after
    /// a long downtime). Period class 0 is kept for ever.
    /// </summary>
    public static class TableBatchRetention
    {
        private const long WeekTicks = 7 * TimeSpan.TicksPerDay;

        /// <summary>
        /// Whether a period class has passed for a week: the building block of the rule below, and by itself the answer
        /// for the rows of a sensor whose class has not changed (and for "is this week database due by its own class").
        /// </summary>
        public static bool IsExpired(long weekStartTicks, int periodWeeks, DateTime utcNow) =>
            periodWeeks > 0 && utcNow.Ticks >= weekStartTicks + (periodWeeks + 1L) * WeekTicks;

        /// <summary>
        /// The one rule of expiry of a sensor's rows of a merge hour (#1850 part 4b), used by every decision that discards
        /// or deletes rows: the merge leaving out rows that have expired, the deletion of expired blocks, the retention
        /// guard and the drop of a week database.
        /// <para>
        /// WHERE the rows are stored follows the class that holds the hour (<see cref="TablePlacement.ClassOf"/>); WHETHER
        /// they have expired follows the period the sensor has now (<see cref="TablePlacement.Target"/>). Until the move
        /// has brought the rows to the databases of their target class, both are asked and a row is kept while either
        /// keeps it: expired only when the class holding the hour has passed for the hour's week and the target class has
        /// passed for it too. So a period that was lengthened protects the rows merged, and still to be merged, under the
        /// shorter one; a period that was shortened deletes nothing early (the week database of the longer class goes
        /// with its own period, or with the move).
        /// </para>
        /// </summary>
        public static bool IsExpired(TablePlacement placement, long hourTicks, DateTime utcNow)
        {
            ArgumentNullException.ThrowIfNull(placement);

            long week = StorageWeek.GetStartTicks(hourTicks);
            return IsExpired(week, placement.ClassOf(hourTicks), utcNow) && IsExpired(week, placement.Target, utcNow);
        }
    }
}
