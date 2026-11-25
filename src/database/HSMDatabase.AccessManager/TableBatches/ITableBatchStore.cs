using System;
using System.Collections.Generic;
using System.Threading;

namespace HSMDatabase.AccessManager.TableBatches
{
    /// <summary>One stored batch to write: the encoded bytes of up to 4,096 rows of one table sensor and their range.</summary>
    public sealed record TableBatchToWrite(Guid SensorId, long FirstTime, long LastTime, int RowCount, byte[] Batch);


    /// <summary>
    /// The batches of one merged hour that go to one retention period class (#1850): the sensors whose period is
    /// <see cref="PeriodWeeks"/> whole weeks (0: kept forever). A sensor's batches are given in their row order.
    /// </summary>
    public sealed record TableBatchHourPart(int PeriodWeeks, IReadOnlyList<TableBatchToWrite> Batches);


    /// <summary>
    /// What the merge records about one merged hour of one product (#1850): the blocks it took, the blocks it could not
    /// read (they stay in the block store), and the product's producer watermarks after the hour (cumulative: every
    /// producer the product has merged, with its highest producer sequence).
    /// </summary>
    public sealed record TableBatchHourCommit
    {
        public long HourTicks { get; init; }

        public long FirstBlockSequence { get; init; }

        public long LastBlockSequence { get; init; }

        public int BlocksMerged { get; init; }

        public IReadOnlyList<long> UnreadableBlocks { get; init; } = [];

        public IReadOnlyDictionary<Guid, long> Watermarks { get; init; } = new Dictionary<Guid, long>();
    }


    /// <summary>The rows one period class of a merged hour holds: the range of their times, the batches and rows.</summary>
    public readonly record struct TableBatchClassRange(int PeriodWeeks, long MinTime, long MaxTime, int Batches, long Rows);


    /// <summary>
    /// A merged hour of a product as the store knows it in memory, loaded from its commits at open: the period classes that
    /// hold its batches with their row-time ranges (a read selects hours by them; a late block makes an hour's range reach
    /// back), and what the merge recorded. <see cref="CommitTicks"/> is when the hour was committed (UTC ticks, the store's
    /// clock when it wrote the hour); for an hour whose commit was rebuilt from the marks, the time of the rebuild.
    /// </summary>
    public sealed record TableBatchHour(long HourTicks, IReadOnlyList<TableBatchClassRange> Classes, long FirstBlockSequence, long LastBlockSequence,
        int BlocksMerged, IReadOnlyList<long> UnreadableBlocks, long CommitTicks);


    /// <summary>A week database of a product: the Monday of the week of its merge hours and its period class.</summary>
    public readonly record struct TableBatchWeek(long WeekStartTicks, int PeriodWeeks);


    /// <summary>
    /// One batch a read found: its sensor, merge hour, number within the hour's batches of its database, whether it lies in
    /// the region of moved batches, its range and its bytes. A sensor's batches of one hour are in their row order by
    /// <see cref="Ordinal"/>.
    /// </summary>
    public sealed record TableBatchRead(Guid SensorId, long HourTicks, int Ordinal, bool Moved, long FirstTime, long LastTime, int RowCount, byte[] Batch);


    /// <summary>A fault of the batch store (disk, LevelDB): a repeat can cure it. A write that throws it is not committed.</summary>
    public sealed class TableBatchStoreException(string message, Exception inner = null) : Exception(message, inner);


    /// <summary>Counters since the process started, for self-monitoring. Written with <see cref="Interlocked"/>.</summary>
    public sealed class TableBatchStoreStatistics
    {
        public long HoursCommitted;
        public long BatchesWritten;
        public long BytesWritten;
        public long DatabasesOpened;
        public long DatabasesClosed;
        public long WeeksFlushed;
        public long WeeksDropped;
        public long HoursRebuilt;
        public long ReadFailures;
        public long WriteFailures;
        /// <summary>
        /// Parts of a merged hour, left by a run that did not commit, that the repeat wrote again or deleted (their class is not
        /// in the record it commits). Counted after the write. Not posted to self-monitoring, like the other
        /// recovery counters (<see cref="HoursRebuilt"/>, <see cref="WeeksDropped"/>): each case is logged as a warning.
        /// </summary>
        public long PartsRewritten;

        public void Add(ref long counter, long value = 1) => Interlocked.Add(ref counter, value);
    }


    /// <summary>
    /// The counters and gauges of the merge job (#1850 part 3), kept with the store it writes so that self-monitoring finds
    /// them where it finds the store's. Counters since the process started, written with <see cref="Interlocked"/>.
    /// </summary>
    public sealed class TableMergeStatistics
    {
        /// <summary>Arrival hours whose every product was committed by this process.</summary>
        public long HoursMerged;
        public long RowsMerged;
        /// <summary>Stored bytes of the blocks merged (envelopes included).</summary>
        public long BlockBytesMerged;
        /// <summary>Bytes of the batches written for them.</summary>
        public long BatchBytesWritten;
        /// <summary>Product hours not written because a batch did not decode to its source rows bit for bit. Must stay 0.</summary>
        public long VerificationMismatches;
        /// <summary>Blocks the merge could not read: kept in the block store, listed in their hour's record.</summary>
        public long UnreadableBlocksKept;
        /// <summary>Rows of tables without a table sensor on their path, or of blocks numbered below the sensor's first sequence.</summary>
        public long RowsDropped;
        /// <summary>Rows left out by the merge because their period had passed for the week of their hour (a catch-up after a long downtime).</summary>
        public long RowsExpired;
        /// <summary>Faults of the merge: a product's hour not merged, an hour's blocks not deleted, a pass stopped (each tried again by a later pass).</summary>
        public long Faults;

        /// <summary>Gauge: closed hours due for the merge and not merged at the end of the last pass.</summary>
        public long LagHours;
        /// <summary>Gauge: milliseconds the last merged arrival hour took, all its products.</summary>
        public long LastMergeMilliseconds;
        /// <summary>Gauge: arrival hours committed for every product whose blocks wait for their deletion delay.</summary>
        public long MergedHoursHoldingBlocks;

        public void Add(ref long counter, long value = 1) => Interlocked.Add(ref counter, value);

        public void Set(ref long gauge, long value) => Interlocked.Exchange(ref gauge, value);
    }


    /// <summary>
    /// The store of batches of table sensors (#1850, ADR-0021 points 10 and 12): per product, one LevelDB database per
    /// Monday-aligned week of the merge hour and per retention period class, and one small database of the product's merged
    /// hours. A merged hour is committed by its record in the hours database: before it, the hour is served from its
    /// blocks; after it, from its batches. Closed weeks are opened only by a read or by retention, at most
    /// <see cref="MaxOpenDatabases"/> at a time. Nothing is opened or created before the first call that needs it.
    /// </summary>
    public interface ITableBatchStore
    {
        /// <summary>True once a product's store has been opened in this process.</summary>
        bool IsOpened { get; }

        TableBatchStoreStatistics Statistics { get; }

        /// <summary>The merge job's counters (the job writes them; the store only holds them).</summary>
        TableMergeStatistics MergeStatistics { get; }

        /// <summary>
        /// The most week databases open at once (default 32). A read that needs more opens and closes them in turn; the
        /// databases of the week being written and those a read holds at the moment are never closed, so the count can be
        /// above the bound while they are more than it.
        /// </summary>
        int MaxOpenDatabases { get; set; }

        /// <summary>Week databases open now.</summary>
        int OpenDatabases { get; }

        /// <summary>Week databases on disk, over the products opened in this process.</summary>
        int WeekDatabases { get; }

        /// <summary>
        /// Writes one merged hour of a product: each part into the week database of its period class (index records,
        /// batches and the hour's mark in one synced write per database), then the commit in the product's hours database.
        /// A part left by a run that did not commit is always written again in one write, and, once every part is written, a
        /// part in a class the record does not name is deleted; an hour already committed is not written again. So a repeat
        /// after a crash completes the hour, every mark of it its record byte for byte. Throws <see cref="TableBatchStoreException"/> on a fault: the hour is
        /// then not committed.
        /// </summary>
        void WriteHour(Guid productId, TableBatchHourCommit commit, IReadOnlyList<TableBatchHourPart> parts);

        /// <summary>The merged hours of a product in hour order, from memory (an empty list when it has none). Opens the product's hours database once.</summary>
        IReadOnlyList<TableBatchHour> GetHours(Guid productId);

        bool IsHourMerged(Guid productId, long hourTicks);

        /// <summary>
        /// The record of a committed hour of a product, or null when the hour is not committed. Unlike <see cref="GetHours"/>
        /// it also gives an hour without any batch (every table of its blocks refused or dropped) and an hour whose classes'
        /// week databases were dropped.
        /// </summary>
        TableBatchHour GetHour(Guid productId, long hourTicks);

        /// <summary>The newest committed hour of a product, or null when it has none. Opens the product's hours database once.</summary>
        long? GetNewestHour(Guid productId);

        /// <summary>The product's producer watermarks, from memory: per producer the highest over every merged hour of the product, whatever order the hours were committed in.</summary>
        IReadOnlyDictionary<Guid, long> GetWatermarks(Guid productId);

        /// <summary>The products with a folder in the store, opened or not.</summary>
        IReadOnlyList<Guid> GetProducts();

        /// <summary>The week databases of a product on disk.</summary>
        IReadOnlyList<TableBatchWeek> GetWeeks(Guid productId);

        /// <summary>
        /// The batches of one sensor in the given merge hours, from the database of each hour's week and of the period class
        /// given: the hour region, then the region of moved batches. In hour order, then by ordinal. A week without a
        /// database contributes nothing.
        /// </summary>
        IReadOnlyList<TableBatchRead> ReadSensor(Guid productId, Guid sensorId, int periodWeeks, IReadOnlyCollection<long> hours);

        /// <summary>
        /// Drops a week database: closes it (waiting for the reads that hold it; reads that start meanwhile find nothing
        /// there), records the drop durably in the product's record of dropped databases and deletes its folder. Its hours
        /// lose that class; an hour left with none is removed from the hours database. A write into a dropped week database
        /// is refused. A delete that fails throws <see cref="TableBatchStoreException"/> with the week already dropped; the
        /// folder left is deleted at the next open of the product.
        /// </summary>
        void DropWeek(Guid productId, TableBatchWeek week);
    }
}
