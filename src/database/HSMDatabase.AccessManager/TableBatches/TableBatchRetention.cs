using System;

namespace HSMDatabase.AccessManager.TableBatches
{
    /// <summary>
    /// When the rows of a week database of the batch store have passed their period (ADR-0021 point 12, #1850): the rows of
    /// the Monday-aligned week of their merge hour are kept for the class's whole weeks after the week has ended, so a row
    /// may outlive its period by up to a week. Retention drops a week database from that moment (part 4), and the merge
    /// leaves such rows out instead of writing them into a week that retention would drop or has dropped (a catch-up after
    /// a long downtime). Period class 0 is kept for ever. The rule of a sensor's rows is the three-term one below.
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
        /// Three terms, and a row is kept while any of them keeps it: expired only when all three have passed for the
        /// hour's week.
        /// <list type="number">
        /// <item>The class that holds the hour (<see cref="TablePlacement.ClassOf"/>): WHERE the rows are stored. Until the
        /// move has brought rows to their target class, a shortened period deletes nothing before this class has passed.</item>
        /// <item>The stored target (<see cref="TablePlacement.Target"/>): the period recorded by the last change written.</item>
        /// <item>The class of the sensor's history setting as read at the decision (<paramref name="settingWeeks"/>,
        /// <c>TablePeriodClass.Of</c>, the function the reconciliation of placements compares with the target). So the
        /// decision does not depend on a placement write having succeeded, or on the reconciliation having seen a change
        /// made since: a period lengthened a moment ago protects the rows at once. Null for a sensor that is no longer in
        /// the tree (it has no setting): the stored target alone decides then.</item>
        /// </list>
        /// A shortening never frees anything early: the stored class and the setting's must both have passed, and the
        /// longer one has not; except the move's expired step, which applies this rule to the placement its switch writes
        /// (the class that holds the hours is then the new target), so rows whose new class has passed are deleted before the
        /// old class has (ADR-0021 point 12; the feature doc's section "The move"). A stale or unwritable placement never
        /// causes a deletion the setting now forbids.
        /// </para>
        /// </summary>
        public static bool IsExpired(TablePlacement placement, long hourTicks, DateTime utcNow, int? settingWeeks)
        {
            ArgumentNullException.ThrowIfNull(placement);

            long week = StorageWeek.GetStartTicks(hourTicks);
            return IsExpired(week, placement.ClassOf(hourTicks), utcNow) && IsExpired(week, placement.Target, utcNow) &&
                   (settingWeeks is not int setting || IsExpired(week, setting, utcNow));
        }
    }
}
