using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSMDatabase.AccessManager
{
    public interface IIntervalDatabase : IDisposable
    {
        string Name { get; }

        long From { get; }

        long To { get; }


        bool Contains(long time);

        bool Overlaps(long from, long to);


        void Compact();

        // Compaction bookkeeping of a weekly shard (#1801). IsCompacted mirrors the
        // CompactedShardMarker file in the shard directory. Only a reclaimable change
        // removes it: a delete, or a change made of deletes (a range delete, a chunk
        // rewrite, the sealing job's removal, fold and unseal). A plain put does not: a
        // new key leaves no garbage, and the one stale version an overwrite leaves (the
        // row an aggregated sensor rewrites under its first key) is dropped by LevelDB's
        // own compaction; what piles up in the bottom levels of a closed week comes from
        // deletes.
        bool IsCompacted { get; }

        /// <summary>Reclaimable changes since the shard was opened.</summary>
        long ReclaimableChangeCount { get; }

        /// <summary>The last reclaimable change since the shard was opened, by the database core's clock; null when none.</summary>
        DateTime? LastReclaimableChangeUtc { get; }

        /// <summary>
        /// Writes the compaction marker unless a reclaimable change happened after
        /// <paramref name="reclaimableChangeCountBeforeCompaction"/> was read; false when one did.
        /// </summary>
        bool TryMarkCompacted(long reclaimableChangeCountBeforeCompaction, string content);
    }
}
