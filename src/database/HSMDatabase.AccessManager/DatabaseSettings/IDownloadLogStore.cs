using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager.DatabaseSettings
{
    // The download log database (ADR-0022 point 15): a LevelDB folder of its own, not the journal. Like the change-set
    // table, WRITE failures propagate: a download that cannot be recorded is refused, so a lost record must never be silent.
    public interface IDownloadLogStore
    {
        // Throws when the record cannot be written (the database cannot be opened, or the write fails).
        void Put(DownloadRecordEntity entity);

        // Records with fromTicks <= time <= toTicks, newest first, at most `limit`; only the product's when productId is set.
        // A row that cannot be parsed is skipped.
        List<DownloadRecordEntity> Read(long fromTicks, long toTicks, Guid? productId, int limit);

        // Removes every record strictly older than the ticks; returns how many.
        int RemoveOlderThan(long ticksExclusive);
    }
}
