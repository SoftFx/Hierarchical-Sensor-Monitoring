using System;
using System.Collections.Generic;

namespace HSMDatabase.DatabaseWorkCore
{
    /// <summary>What the planner needs to know of one weekly shard (sensor history or journal).</summary>
    /// <param name="Name">The shard directory.</param>
    /// <param name="From">Start of the shard's week, in ticks.</param>
    /// <param name="IsCompacted">The shard carries the compaction marker: compacted and no reclaimable change since.</param>
    /// <param name="LastReclaimableChangeUtc">The last change since the server started that leaves a compaction something
    /// to reclaim: a delete, or a change made of deletes (a chunk rewrite, a step of the sealing job). A put is not one.
    /// Null when none.</param>
    /// <param name="RetryAfterUtc">A failed compaction holds the shard back until then; null when none.</param>
    public readonly record struct HistoryShardCandidate(string Name, long From, bool IsCompacted, DateTime? LastReclaimableChangeUtc, DateTime? RetryAfterUtc);


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
        /// compacted, or a reclaimable change since), it has had no reclaimable change for
        /// <paramref name="quietPeriod"/> (a shard still being trimmed would be compacted again
        /// right away) and no failed attempt holds it back. Puts do not count: a closed week
        /// that holds the row of an aggregated sensor receives one on every equal value, and
        /// would otherwise never be quiet. A shard with no reclaimable change since the start
        /// counts its quiet period from <paramref name="startedUtc"/>: what changed before the
        /// start is not known, so a shard trimmed just before a restart is not compacted right
        /// after it, and again after the next trim.
        /// </summary>
        public static bool IsDue(HistoryShardCandidate shard, DateTime utcNow, TimeSpan quietPeriod, DateTime startedUtc)
        {
            var currentWeekStart = DateTimeMethods.GetStartOfWeekTicks(utcNow.Ticks);
            var quietSince = shard.LastReclaimableChangeUtc ?? startedUtc;

            return shard.From < currentWeekStart
                && !shard.IsCompacted
                && utcNow - quietSince >= quietPeriod
                && (shard.RetryAfterUtc is not { } retryAfter || utcNow >= retryAfter);
        }

        /// <summary>The newest due shard; on the same week the first one listed. Null when none is due.</summary>
        public static HistoryShardCandidate? SelectNext(IEnumerable<HistoryShardCandidate> shards, DateTime utcNow, TimeSpan quietPeriod, DateTime startedUtc)
        {
            HistoryShardCandidate? next = null;

            foreach (var shard in shards)
                if (IsDue(shard, utcNow, quietPeriod, startedUtc) && (next is null || shard.From > next.Value.From))
                    next = shard;

            return next;
        }
    }
}
