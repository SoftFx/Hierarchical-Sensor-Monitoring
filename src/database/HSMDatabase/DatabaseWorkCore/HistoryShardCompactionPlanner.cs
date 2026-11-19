using System;
using System.Collections.Generic;

namespace HSMDatabase.DatabaseWorkCore
{
    /// <summary>What the planner needs to know of one weekly shard (sensor history or journal).</summary>
    /// <param name="Name">The shard directory.</param>
    /// <param name="From">Start of the shard's week, in ticks.</param>
    /// <param name="IsCompacted">The shard carries the compaction marker: compacted and unchanged since.</param>
    /// <param name="LastChangeUtc">The last write or delete since the server started; null when none.</param>
    /// <param name="RetryAfterUtc">A failed compaction holds the shard back until then; null when none.</param>
    public readonly record struct HistoryShardCandidate(string Name, long From, bool IsCompacted, DateTime? LastChangeUtc, DateTime? RetryAfterUtc);


    /// <summary>
    /// Which closed weekly shard the background compaction takes next (#1801). Pure: the
    /// caller supplies the shard states and the clock, nothing here touches LevelDB.
    /// </summary>
    public static class HistoryShardCompactionPlanner
    {
        /// <summary>How long a shard whose compaction failed waits before the next attempt.</summary>
        public static readonly TimeSpan FailureBackoff = TimeSpan.FromDays(1);


        /// <summary>
        /// A shard is due when its week ended before the current week began (the current
        /// week and any later one are never due), it carries no compaction marker (never
        /// compacted, or changed since), it has not changed for <paramref name="quietPeriod"/>
        /// (a shard still being written to or trimmed would be compacted again right away)
        /// and no failed attempt holds it back.
        /// </summary>
        public static bool IsDue(HistoryShardCandidate shard, DateTime utcNow, TimeSpan quietPeriod)
        {
            var currentWeekStart = DateTimeMethods.GetStartOfWeekTicks(utcNow.Ticks);

            return shard.From < currentWeekStart
                && !shard.IsCompacted
                && (shard.LastChangeUtc is not { } lastChange || utcNow - lastChange >= quietPeriod)
                && (shard.RetryAfterUtc is not { } retryAfter || utcNow >= retryAfter);
        }

        /// <summary>The newest due shard; on the same week the first one listed. Null when none is due.</summary>
        public static HistoryShardCandidate? SelectNext(IEnumerable<HistoryShardCandidate> shards, DateTime utcNow, TimeSpan quietPeriod)
        {
            HistoryShardCandidate? next = null;

            foreach (var shard in shards)
                if (IsDue(shard, utcNow, quietPeriod) && (next is null || shard.From > next.Value.From))
                    next = shard;

            return next;
        }
    }
}
