using System;
using HSMDatabase.AccessManager;

namespace HSMServer.Core.Tests.Infrastructure
{
    // A folded run of equal values ends at the storage-week boundary (Monday 00:00 UTC). A test
    // that folds values at times relative to UtcNow would see its run split once a week, when
    // the run straddles that boundary; this keeps such a run inside one week.
    internal static class StorageWeekTestTime
    {
        // Returns time when [time - before, time + after] lies in one storage week; otherwise a
        // time whose window ends a minute before the Monday the window crossed.
        internal static DateTime WithinOneWeek(DateTime time, TimeSpan before, TimeSpan after)
        {
            if (StorageWeek.IsSame(time - before, time + after))
                return time;

            return StorageWeek.GetStart(time + after) - after - TimeSpan.FromMinutes(1);
        }
    }
}
