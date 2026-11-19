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
        // CompactedShardMarker file in the shard directory; any write or delete that
        // changes the shard removes it and counts in ChangeCount (since open).
        bool IsCompacted { get; }

        long ChangeCount { get; }

        DateTime? LastChangeUtc { get; }

        /// <summary>
        /// Writes the compaction marker unless the shard changed after
        /// <paramref name="changeCountBeforeCompaction"/> was read; false when it did.
        /// </summary>
        bool TryMarkCompacted(long changeCountBeforeCompaction, string content);
    }
}
