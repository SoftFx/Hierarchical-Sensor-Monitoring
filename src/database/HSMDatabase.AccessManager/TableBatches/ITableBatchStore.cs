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


    /// <summary>
    /// A sensor's batches in a run of hours of one week database (#1850 part 4c), counted from their index records: the
    /// records, the rows they give and the batches' stored lengths (from the index value, no batch fetched). What
    /// <see cref="ITableBatchStore.CopySensorHours"/> wrote, <see cref="ITableBatchStore.DeleteSensorHours"/> deleted and
    /// <see cref="ITableBatchStore.GetSensorHourBytes"/> found.
    /// </summary>
    public readonly record struct TableBatchSensorHours(long Batches, long Rows, long Bytes);


    /// <summary>
    /// The bytes of batches deleted from a week database since its last compaction (the database's deleted-bytes counter,
    /// #1850 part 4c) and the size of its folder now.
    /// </summary>
    public readonly record struct TableBatchDeletedShare(long DeletedBytes, long FolderBytes)
    {
        /// <summary>The deleted bytes as a share of the folder: 0 for an empty or absent folder.</summary>
        public double Share => FolderBytes <= 0 ? 0 : (double)DeletedBytes / FolderBytes;
    }


    /// <summary>
    /// The step of the move between period classes a product has in flight (#1850 part 4c): the sensor's hours
    /// <c>[FirstHour, EndHour)</c> of the week starting <see cref="WeekStartTicks"/> are being copied from class
    /// <see cref="FromClass"/> into <see cref="ToClass"/>. It says only which other class may hold a copy: the sensor's
    /// placement is the authority on where its rows are.
    /// </summary>
    public sealed record TableMoveInFlight(Guid SensorId, long WeekStartTicks, int FromClass, int ToClass, long FirstHour, long EndHour);


    /// <summary>
    /// A removal the tree recorded (#1850 part 4d): of a table sensor (<see cref="SensorId"/>, of the root product
    /// <see cref="ProductId"/>, <see cref="Guid.Empty"/> when the tree did not know it) or of a root product
    /// (<see cref="SensorId"/> null), whose stored rows the merge job deletes once it is gone from the tree.
    /// <see cref="Earlier"/>: read from the store's record at its load, so written by an earlier run of the server; false for
    /// a mark written in this run (a removal the tree may still be making: the mark is written before the entity goes).
    /// <see cref="Confirmed"/>: a mark of this run whose entity the tree has removed from its database since
    /// (<see cref="ITableBatchStore.ConfirmRemoved"/>, in memory only). A mark is acted on only when <see cref="Ready"/>: one
    /// of this run that is not confirmed may still be undone by a crash before the entity leaves the database, after which
    /// the sensor or product would come back.
    /// </summary>
    public sealed record TableRemoval(Guid ProductId, Guid? SensorId, bool Earlier, bool Confirmed = false)
    {
        /// <summary>The entity is gone from the database: the mark was read at the store's load, or confirmed in this run.</summary>
        public bool Ready => Earlier || Confirmed;
    }


    /// <summary>A fault of the batch store (disk, LevelDB): a repeat can cure it. A write that throws it is not committed.</summary>
    public sealed class TableBatchStoreException(string message, Exception inner = null) : Exception(message, inner);


    /// <summary>
    /// A refusal of the batch store (#1850 part 4c): what is on disk does not allow the call, and a repeat does not change
    /// that (a target database retention dropped, rows of the sensor in the target's hour region, a source damaged). Nothing
    /// was written. Not a <see cref="TableBatchStoreException"/> and not counted as a write failure: a caller that repeats
    /// store faults does not repeat it, and the diagnostics of the disk do not show it.
    /// </summary>
    public sealed class TableBatchRefusedException(string message, Exception inner = null) : Exception(message, inner);


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
        /// <summary>Products whose placements were rebuilt from the indexes of their week databases (#1850 part 4b): missing or damaged. Logged as a warning.</summary>
        public long PlacementsRebuilt;
        public long ReadFailures;
        public long WriteFailures;
        /// <summary>
        /// Parts of a merged hour, left by a run that did not commit, that the repeat wrote again or deleted (their class is not
        /// in the record it commits). Counted after the write. Not posted to self-monitoring, like the other
        /// recovery counter <see cref="HoursRebuilt"/>: each case is logged as a warning.
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

        /// <summary>Faults of retention (#1850 part 4): a week database not dropped, expired blocks not deleted (tried again an hour later).</summary>
        public long RetentionFaults;
        /// <summary>Blocks never merged that retention deleted because the period of every table sensor with rows in them had passed.</summary>
        public long ExpiredBlocksDeleted;
        /// <summary>Rows of those blocks (from the block index, every table of a block).</summary>
        public long ExpiredBlockRowsDeleted;
        /// <summary>
        /// Gauge (#1850 part 4b): week databases whose class has passed and that retention keeps because they hold rows of a
        /// sensor whose period changed since and whose new period has not passed (owed to the move), at the last retention run.
        /// </summary>
        public long MoveWeeksPending;
        /// <summary>
        /// Products left out of a merge pass because their placements could not be brought up to their sensors' periods at
        /// its start (#1850 part 4b): no merge, no retention and no expiry decision for them in that pass. Each also counts
        /// in <see cref="Faults"/>.
        /// </summary>
        public long ProductsNotReconciled;

        // The move between period classes (#1850 part 4c).

        /// <summary>Gauge: sensor-weeks of table sensors in the tree still in a segment of an old class, at the end of the last pass.</summary>
        public long MoveStepsPending;
        /// <summary>
        /// Gauge: of those, the sensor-weeks of products whose steps could not run in the last pass (placements not reconciled,
        /// a fault of the move, a merge that lags, which holds their copy steps).
        /// </summary>
        public long MoveStepsBlocked;
        /// <summary>Gauge: steps the store refused (or whose target was dropped) that are not tried again in this process while they stay the same.</summary>
        public long MoveStepsRefusedNow;
        /// <summary>Rows the move copied into their new class.</summary>
        public long RowsMoved;
        /// <summary>Bytes of batches the move copied into their new class.</summary>
        public long MovedBytes;
        /// <summary>Rows the move deleted because the sensor's new period had passed for their week (expired steps).</summary>
        public long RowsExpiredByMove;
        /// <summary>Faults of the move: a step that did not complete, a step in flight not recovered, a compaction that failed (each tried again by a later pass).</summary>
        public long MoveFaults;
        /// <summary>Steps that could not run now: a copy into a database retention dropped, too little free disk, a copy the store refused.</summary>
        public long MoveStepsRefused;
        /// <summary>Week databases compacted after a tenth of them was deleted.</summary>
        public long DatabasesCompacted;
        /// <summary>Copy steps that first deleted rows of the sensor a move had left in their target (a move whose in-flight record was lost).</summary>
        public long MoveLeftoversCleaned;

        // The removal of table sensors and products (#1850 part 4d).

        /// <summary>Removals of table sensors and root products completed: every stored row deleted and the record of the removal cleared.</summary>
        public long RemovalsDone;
        /// <summary>
        /// Gauge: placements whose sensor is not in the tree and has no record of its removal (a record that could not be
        /// written, a sensor whose entity does not load), at the end of the last pass. Their rows are kept, never deleted by
        /// the job; logged as a warning once a day with the ids.
        /// </summary>
        public long OrphanPlacements;

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

        /// <summary>
        /// The merged hours of a product that hold batches and whose week still has a database (of any class), in hour order,
        /// from memory (an empty list when it has none). An hour keeps the classes its record names, dropped ones included: the
        /// rows of a sensor may have been moved from a dropped class into another one, and their times are within the record's
        /// ranges. Opens the product's hours database once.
        /// </summary>
        IReadOnlyList<TableBatchHour> GetHours(Guid productId);

        /// <summary>The committed hours of a product no database of whose week is left (#1850 part 4b): retention forgets them once their blocks are gone.</summary>
        IReadOnlyList<long> GetHoursWithoutDatabases(Guid productId);

        /// <summary>
        /// Removes the records of committed hours no database of whose week is left (hours with a database are skipped), in
        /// one synced write. Retention calls it once the block store holds no block of those hours: until then the record
        /// keeps the hour merged, so the merge never takes it again. Throws <see cref="TableBatchStoreException"/> on a fault.
        /// </summary>
        void ForgetHours(Guid productId, IReadOnlyCollection<long> hours);

        /// <summary>The placement of a sensor's batches, or null when the store has none for it (it has no batch). From memory.</summary>
        TablePlacement GetPlacement(Guid productId, Guid sensorId);

        /// <summary>Every placement of a product, from memory.</summary>
        IReadOnlyDictionary<Guid, TablePlacement> GetPlacements(Guid productId);

        /// <summary>
        /// Stores the placement of a sensor durably (one synced write into the product's placements database) and then in
        /// memory; null removes it. Creates the product's folder. Throws <see cref="TableBatchStoreException"/> on a fault, with
        /// nothing changed.
        /// </summary>
        void SetPlacement(Guid productId, Guid sensorId, TablePlacement placement);

        bool IsHourMerged(Guid productId, long hourTicks);

        /// <summary>
        /// The product's generation: a number that changes, under the product's lock, with every change of what a read's
        /// plan of any of its sensors can see in the store: every change <see cref="GetGeneration(Guid, Guid)"/> of any
        /// sensor of the product makes. Read under the same lock; it never goes back. A product without a folder answers 0.
        /// </summary>
        long GetGeneration(Guid productId);

        /// <summary>
        /// The generation of one sensor's plan (#1850 part 4c): the product's term plus the sensor's own, read together under
        /// the product's lock. Both only grow, so two equal readings mean nothing a plan of this sensor sees changed between
        /// them (#1847, #1850 part 4b): it is the only test of a plan's staleness, so a new write that changes what a plan
        /// sees must change it as well.
        /// <para>
        /// The product's term changes with every write any plan of the product can see: every commit of an hour (one
        /// without any batch class included), a week database created by the write of an hour (also one that does not reach
        /// its commit), every <see cref="DropWeek"/>, every <see cref="ForgetHours"/> that removes an hour, a rebuild of the
        /// hours database or of the placements. The sensor's term changes with a write that only this sensor's plan sees:
        /// <see cref="SetPlacement"/> of this sensor (a placement gives the class of each of its hours; a plan reads no other
        /// sensor's placement), <see cref="CopySensorHours"/> and <see cref="DeleteSensorHours"/> of this sensor (every key
        /// they write or delete carries the sensor's id or the ordinal of one of its own index records; a week database the
        /// copy creates holds no row of another sensor). So a placement write or a move of one sensor never makes a plan of
        /// another one stale. A product without a folder answers 0.
        /// </para>
        /// </summary>
        long GetGeneration(Guid productId, Guid sensorId);

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
        /// Whether a week database was dropped by retention and its drop is still on the product's record: a write into it
        /// is refused (<see cref="WriteHour"/>). The merge leaves out the rows of an hour that would go there: they went
        /// with their week when the period they had then passed.
        /// </summary>
        bool IsWeekDropped(Guid productId, TableBatchWeek week);

        /// <summary>
        /// The batches of one sensor in the given merge hours, from the database of each hour's week and of the period class
        /// given: the hour region, then the region of moved batches. In hour order, then by ordinal. A week without a
        /// database contributes nothing.
        /// </summary>
        IReadOnlyList<TableBatchRead> ReadSensor(Guid productId, Guid sensorId, int periodWeeks, IReadOnlyCollection<long> hours);

        /// <summary>
        /// <see cref="ReadSensor(Guid, Guid, int, IReadOnlyCollection{long})"/>, and in <paramref name="failed"/> the batches it could not
        /// read: an index record that does not decode or whose batch is missing (each also counted in <c>ReadFailures</c> and logged).
        /// A reader adds them to its answer's <c>BatchesFailed</c> (#1850 part 4b): their rows are missing.
        /// </summary>
        IReadOnlyList<TableBatchRead> ReadSensor(Guid productId, Guid sensorId, int periodWeeks, IReadOnlyCollection<long> hours, out int failed);

        /// <summary>
        /// <see cref="ReadSensor(Guid, Guid, int, IReadOnlyCollection{long}, out int)"/> of the batches whose rows can meet
        /// <c>[fromTicks, toTicks)</c> (#1850 part 4c): an index record whose range (first to last time) does not meet it is
        /// skipped before its batch is fetched, so it is neither returned nor counted in <paramref name="failed"/>, even when
        /// its batch is missing (it would add no row to the answer). A record that does not decode is counted: its range is
        /// not known.
        /// </summary>
        IReadOnlyList<TableBatchRead> ReadSensor(Guid productId, Guid sensorId, int periodWeeks, IReadOnlyCollection<long> hours, long fromTicks, long toTicks, out int failed);

        /// <summary>
        /// Drops a week database: closes it (waiting for the reads that hold it; reads that start meanwhile find nothing
        /// there), records the drop durably in the product's record of dropped databases and deletes its folder. Its hours
        /// keep their records (see <see cref="ForgetHours"/>). A write into a dropped week database is refused. A delete that fails throws <see cref="TableBatchStoreException"/> with the week already dropped; the
        /// folder left is deleted at the next open of the product.
        /// </summary>
        void DropWeek(Guid productId, TableBatchWeek week);


        // === The move between period classes and the removal of a sensor (#1850 parts 4c and 4d): the store's primitives ===

        /// <summary>
        /// Copies a sensor's batches of the hours <c>[firstHour, endHour)</c> (inside the week of <paramref name="from"/>)
        /// from the database <paramref name="from"/> into the region of moved batches of the database of the same week and
        /// class <paramref name="toClass"/>, in one synced write into that database. Only the hours the source holds records of
        /// the sensor in are touched: a copy never touches the target for an hour the source has nothing of (there the target's
        /// moved rows may be the sensor's only copy). For each such hour, every moved key of the sensor there is deleted first
        /// (a repeat leaves exactly one copy), then the sensor's index records and batches of both regions of the source are put,
        /// numbered again 0..n-1 in the source's rank order (hour region before moved region, each by its ordinal), as bytes
        /// (an index record whose batch is missing, or a batch that does not decode, is carried as it is), and the hour's mark,
        /// the source's bytes, where the target has none. With no such hour nothing is written at all: no database created, no
        /// generation changed. Creates the target database when there is something to write. The source is not changed. Changes
        /// only the sensor's generation. Bytes deleted in the target count in its deleted-bytes counter
        /// (<see cref="GetDeletedShare"/>). Answers what was written.
        /// <para>
        /// The order of the rows is kept by the new ordinals, not by the order of the keys: a read of the target visits the moved
        /// index records by their first time and then their ordinal (the key is <c>0xFF | hour | sensor | 0x00 | first | ordinal</c>),
        /// and the reader ranks the rows of equal time by region, ordinal and row, so the rows read after a copy are the rows
        /// read before it, in the same order.
        /// </para>
        /// <para>
        /// A copy holds every batch of its whole range in memory and writes them in one synced write under the product's lock:
        /// the caller bounds the range. The move job cuts a step at <c>TableMergeJob.MoveStepBytes</c> (64 MB) of the sensor's
        /// batches counted with <see cref="GetSensorHourBytes"/> (at least one hour) and never asks for more than one week.
        /// </para>
        /// <para>
        /// Refuses with <see cref="TableBatchRefusedException"/>, nothing written, no generation changed: a target database
        /// retention dropped; a source hour with the sensor's records and no mark, a mark that does not decode, or an index key
        /// of the sensor that does not parse; and a target that holds index records of the sensor of a copied hour in its hour
        /// region (they may be the sensor's only rows of the hour, in the class that holds it, or a source a move left behind:
        /// the store cannot tell, so it deletes nothing, and the caller, which knows from the placement that the target is not
        /// the class of those hours, deletes them first with <see cref="DeleteSensorHours"/>; copied beside them, they would be
        /// read twice). A fault of the disk or of LevelDB is a <see cref="TableBatchStoreException"/>.
        /// </para>
        /// </summary>
        TableBatchSensorHours CopySensorHours(Guid productId, Guid sensorId, TableBatchWeek from, int toClass, long firstHour, long endHour);

        /// <summary>
        /// Deletes a sensor's keys of the hours <c>[firstHour, endHour)</c> (inside its week) from one week database, in one
        /// synced write: in the hour region its index records and the batches they name (an hour-region batch key carries no
        /// sensor id: only the ordinal of one of this sensor's own index records deletes one), in the region of moved batches
        /// every key of the sensor; never an hour's mark, never a key of another sensor. The batches' stored bytes are added to
        /// the database's deleted-bytes counter in the same write. Changes only the sensor's generation. A database that does
        /// not exist (or is dropped) holds nothing: nothing is written. Answers what was deleted. The caller holds the write
        /// side of the read gate (<c>TableHourGate</c>): a full read that planned this database reads it under the read side
        /// without checking the generation again, and must end first.
        /// </summary>
        TableBatchSensorHours DeleteSensorHours(Guid productId, Guid sensorId, TableBatchWeek week, long firstHour, long endHour);

        /// <summary>
        /// A sensor's batches of the hours <c>[firstHour, endHour)</c> in one week database, both regions, from the index
        /// records only (the stored length is in the index value; no batch is fetched). Nothing for a database that does not exist.
        /// </summary>
        TableBatchSensorHours GetSensorHourBytes(Guid productId, Guid sensorId, TableBatchWeek week, long firstHour, long endHour);

        /// <summary>
        /// The deleted-bytes counter of a week database (the batches' bytes <see cref="DeleteSensorHours"/> and
        /// <see cref="CopySensorHours"/> deleted there since its last <see cref="CompactWeek"/>, durable in the database
        /// itself) and the size of its folder. Zero for a database that does not exist.
        /// </summary>
        TableBatchDeletedShare GetDeletedShare(Guid productId, TableBatchWeek week);

        /// <summary>
        /// Compacts a whole week database (LevelDB's <c>CompactRange</c> over every key: deleted keys give their space back),
        /// holding a lease on it and not the product's lock while it compacts (reads and writes of the product go on), then
        /// lowers its deleted-bytes counter by the value it had before the compaction, in one synced write. A crash in between
        /// leaves the counter as it was (the compaction is repeated). Nothing for a database that does not exist. Others wait
        /// for it while they hold the product's lock: a <see cref="DropWeek"/> of the same database (for the lease), the flush
        /// of the same database while it is the newest week's (LevelDB runs one manual compaction at a time), and a dispose
        /// (for the lease, without a bound). So the caller never compacts the newest week's databases and compacts on its own
        /// thread, never beside a drop of the same database. Throws <see cref="TableBatchStoreException"/> on a fault.
        /// </summary>
        void CompactWeek(Guid productId, TableBatchWeek week);

        /// <summary>The week databases of a product due for a compaction (the durable list <c>&lt;product&gt;/Compact</c>), from memory.</summary>
        IReadOnlyList<TableBatchWeek> GetCompactList(Guid productId);

        /// <summary>
        /// Adds a week database to the product's compaction list, durably (a small file written atomically, before the
        /// compaction is made, so a crash does not forget it). Nothing for a product without a folder. Throws
        /// <see cref="TableBatchStoreException"/> on a fault, with the list in memory unchanged; when the fault is the sync of
        /// the folder after the rename (Linux), the file on disk already holds the new list, so memory and disk may differ
        /// until the next open. Harmless: the list is a hint (a database's counter says again whether it is due).
        /// </summary>
        void AddToCompactList(Guid productId, TableBatchWeek week);

        /// <summary>Removes a week database from the product's compaction list, durably. Throws <see cref="TableBatchStoreException"/> on a fault.</summary>
        void RemoveFromCompactList(Guid productId, TableBatchWeek week);

        /// <summary>The product's step of the move in flight (the durable record <c>&lt;product&gt;/Move</c>), or null; from memory.</summary>
        TableMoveInFlight GetMoveInFlight(Guid productId);

        /// <summary>
        /// Writes the product's step of the move in flight durably (a small file written atomically and synced, strict), or
        /// deletes it (null), then in memory. A record's hours lie inside its week and its classes differ
        /// (<see cref="ArgumentException"/>). A record for a product without a folder is a fault. Throws
        /// <see cref="TableBatchStoreException"/> on a fault, with the record in memory unchanged; when the fault is the sync
        /// of the folder after the rename or the delete (Linux), the disk already holds the new state, so memory and disk may
        /// differ until the next open. Harmless: the recovery of a step takes every decision from the placement, and the
        /// record only names the other class that may hold a copy.
        /// </summary>
        void SetMoveInFlight(Guid productId, TableMoveInFlight record);


        // === The removal of table sensors and root products (#1850 part 4d) ===

        /// <summary>
        /// Records the removal of table sensors (each with its root product, <see cref="Guid.Empty"/> when not known) and of
        /// root products, durably, in one write (the store-wide file <c>Removed</c>, written atomically and synced, strict), and
        /// then in memory. The tree calls it before it removes the entities: a crash in between leaves a mark of a sensor or
        /// product that is still in the tree, which the merge job clears without deleting anything. A sensor marked again
        /// keeps the product named before when this call does not know it. Throws <see cref="TableBatchStoreException"/> on a
        /// fault (a file that cannot be written, or a record that cannot be read: it is never written over unread), with
        /// nothing changed: the caller logs it and goes on with the removal, whose rows are then kept and reported as an
        /// orphan placement.
        /// </summary>
        void MarkRemoved(IReadOnlyCollection<(Guid Product, Guid Sensor)> sensors, IReadOnlyCollection<Guid> products);

        /// <summary>
        /// The removals recorded and not cleared: root products first, then sensors, each by id. Read from the file once, at
        /// the first call; a file that cannot be read is a <see cref="TableBatchStoreException"/> at every call until it can
        /// (nothing is taken as removed while the record is not known). A line that does not read is logged as an Error and
        /// skipped.
        /// </summary>
        IReadOnlyList<TableRemoval> GetRemovals();

        /// <summary>
        /// The removal recorded now for the same sensor as <paramref name="removal"/> (for a root product's mark, the same
        /// product), or null: a keyed lookup, for a caller that checks marks one by one. Throws as <see cref="GetRemovals"/>.
        /// </summary>
        TableRemoval FindRemoval(TableRemoval removal);

        /// <summary>
        /// Confirms marks of this run once the tree has removed their entities from its database (in memory only: after a
        /// restart every mark read from the file is <see cref="TableRemoval.Earlier"/>, and the tree loaded from the database
        /// says whether its entity is gone). A sensor or product without a mark of this run is skipped.
        /// </summary>
        void ConfirmRemoved(IReadOnlyCollection<Guid> sensors, IReadOnlyCollection<Guid> products);

        /// <summary>
        /// Clears removals, durably, then in memory: each one only while it is the same mark (a sensor marked again since,
        /// in this run, keeps its new mark). Answers whether every one given was cleared. Throws
        /// <see cref="TableBatchStoreException"/> on a fault, with nothing changed.
        /// </summary>
        bool ClearRemovals(IReadOnlyCollection<TableRemoval> removals);

        /// <summary>
        /// Removes a product's batches whole: from the call on, a read finds nothing of it (as for a product without a
        /// folder) and a write into it is refused for the rest of the process; every week database of it is closed once the
        /// reads that hold it end, and its <c>Hours</c> and <c>Placements</c>; then its folder is renamed to
        /// <c>&lt;product&gt;.removed-&lt;ticks&gt;</c> (atomic, never listed as a product) and deleted. A renamed folder whose
        /// delete fails, or that a crash left, is deleted by the store's first listing of its products in a later run. A
        /// product without a folder: nothing on disk. Throws <see cref="TableBatchStoreException"/> when the rename fails (the
        /// folder stays; a repeat renames it).
        /// </summary>
        void RemoveProduct(Guid productId);
    }
}
