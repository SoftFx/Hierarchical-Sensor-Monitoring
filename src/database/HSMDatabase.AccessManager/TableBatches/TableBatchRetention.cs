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

        public static bool IsExpired(long weekStartTicks, int periodWeeks, DateTime utcNow) =>
            periodWeeks > 0 && utcNow.Ticks >= weekStartTicks + (periodWeeks + 1L) * WeekTicks;
    }
}
