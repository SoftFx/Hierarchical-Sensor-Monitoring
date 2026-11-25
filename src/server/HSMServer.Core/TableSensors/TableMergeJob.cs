using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using HSMDatabase.AccessManager.TableBatches;
using HSMDatabase.AccessManager.TableBlocks;
using HSMDatabase.SealedHistory;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMTableBlocks;
using NLog;

namespace HSMServer.Core.TableSensors
{
    /// <summary>What one merge pass did.</summary>
    public sealed record TableMergePassReport
    {
        /// <summary>Arrival hours committed for every product with blocks in them.</summary>
        public int HoursMerged { get; init; }

        /// <summary>Arrival hours whose blocks were deleted after their delay.</summary>
        public int HoursDeleted { get; init; }

        /// <summary>Product hours not written because a batch did not verify.</summary>
        public int Mismatches { get; init; }

        /// <summary>The pass stopped on a fault of a store; the hour it was on is merged again by a later pass.</summary>
        public string Fault { get; init; }

        /// <summary>The pass stopped because the server is stopping; the hour it was on is untouched or committed.</summary>
        public bool Stopped { get; init; }

        /// <summary>Another pass was running.</summary>
        public bool Busy { get; init; }

        /// <summary>Week databases retention dropped (#1850 part 4).</summary>
        public int WeeksDropped { get; init; }

        /// <summary>Arrival hours retention deleted with blocks never merged whose period had passed (#1850 part 4).</summary>
        public int ExpiredHoursDeleted { get; init; }

        /// <summary>
        /// Products left out of the pass because their placements could not be brought up to their sensors' periods at its
        /// start (#1850 part 4b): nothing of them was merged, dropped or judged for expiry. The next pass tries again.
        /// </summary>
        public int ProductsNotReconciled { get; init; }
    }


    /// <summary>
    /// The hourly merge of table blocks into batches (#1850 part 3, ADR-0021 point 9). One pass, run by the server's
    /// background service every minute on one thread:
    /// <list type="number">
    /// <item>The closed arrival hours (an hour after its end plus <see cref="TableBlockOptions.MergeGrace"/>) that hold blocks
    /// of a product not committed for that hour are merged one at a time, oldest first, so the job catches up after a
    /// downtime. For an hour H: <c>CloseHour(H)</c>; per product with blocks in H, the blocks are read, the refused tables
    /// skipped, each table resolved to its sensor by path, rows without a sensor or from a block below the sensor's first
    /// sequence dropped (counted); each sensor's rows sorted stably by (time, block sequence, row), cut evenly into batches of
    /// at most 4,096 rows, encoded, decoded again and compared cell by cell and null by null; then <c>WriteHour</c> into
    /// the class database of the period the sensor has now. Once every product of H is committed, the block store's
    /// watermarks are raised.</item>
    /// <item>A product's blocks of an hour it committed are deleted (except the blocks its merge could not read) once the
    /// configured delay has passed since its commit, under the write side of the read gate; another product's blocks of
    /// the same hour stay until that product has committed the hour too.</item>
    /// </list>
    /// A batch that does not verify leaves that product's hour unwritten (its blocks serve it), counted as an Error, and it
    /// is not tried again until the next start. A block that cannot be read stays, is listed in the hour's record and
    /// counted. A fault (a store, the disk, an error reading a block) leaves that product's hour for a later pass while the other
    /// products and the deletions go on; a rerun of a committed hour or of
    /// parts already written changes nothing. Nothing holds a store's lock while blocks are read or batches encoded.
    /// </summary>
    public sealed partial class TableMergeJob
    {
        /// <summary>
        /// The bytes the merge holds per row of a table of <paramref name="columns"/> columns (the time column included) while
        /// it merges the row's share: 8 per cell for the value arrays the block reader gives each column (<c>long[]</c> or
        /// <c>double[]</c>), at most 1 bit per cell for its null maps (rounded up to a byte per 8 columns), and 20 for the arrays
        /// <see cref="EncodeSensor"/> builds over the rows of the sensor it cuts (table 4, row 4, time 8, order 4). Not in it:
        /// the copy of one batch being encoded (at most 4,096 rows, freed before the next), the tables of the block being read
        /// that are not kept, and the encoded batches, held until the hour is written (5.4 bytes a row in the benchmark's hour).
        /// </summary>
        internal static long RowBytes(int columns) => 8L * columns + (columns + 7) / 8 + 20;

        /// <summary>
        /// The bytes the merge holds for one table of a block: its rows by <see cref="RowBytes"/> plus what the block reader
        /// allocates once per table, whatever its rows (64-bit runtime: an object header is 16 bytes, an array's 24, a
        /// reference 8). Per table 104 + the id's length: the <c>BlockTable</c> (16 + two references + schema id and row count, = 40),
        /// its id array (24 + the id, padded: up to 7 more, so 32), its <c>BlockColumn[]</c> (24) and its reference in the
        /// sensor's list (8). Per column 80: the value array's header (24), the <c>BlockColumn</c> (16 + three references
        /// and its type, = 48) and its reference in the column array (8). Per column with a null map 32: that array's header
        /// and its padding to 8 bytes (the bits themselves are in <see cref="RowBytes"/>). Blocks hold about six rows of a
        /// table, so for a wide schema these terms are larger than the rows.
        /// </summary>
        internal static long TableBytes(int rows, int columns, int nullMaps, int idLength) =>
            rows * RowBytes(columns) + 104 + idLength + 80L * columns + 32L * nullMaps;

        private static readonly Logger _logger = LogManager.GetLogger("TableMerge");
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);

        private readonly IDatabaseCore _database;
        private readonly ITreeValuesCache _cache;
        private readonly TableBlockOptions _options;
        private readonly TableHourGate _gate;
        private readonly Action<Guid> _ensureState;

        private readonly object _runLock = new();

        // Product hours whose batches did not verify: not tried again until the next start (a codec defect is
        // deterministic; trying every minute would only repeat the Error).
        private readonly HashSet<(Guid Product, long Hour)> _failedVerification = [];


        internal TableMergeJob(IDatabaseCore database, ITreeValuesCache cache, TableBlockOptions options, TableHourGate gate, Action<Guid> ensureState)
        {
            _database = database;
            _cache = cache;
            _options = options;
            _gate = gate;
            _ensureState = ensureState;
        }


        /// <summary>
        /// Called at the points a test stands a crash or an event at: "reconciled" (the placements brought up to the
        /// sensors' periods, nothing merged yet), "encoded" (a product's batches built and verified, nothing written),
        /// "watermarks-raised".
        /// </summary>
        internal Action<string> Checkpoint { get; set; }

        /// <summary>Called with every decoded batch before it is compared with its rows. Test seam for a verification mismatch.</summary>
        internal Action<Guid, TableBatch> AfterDecode { get; set; }

        /// <summary>Free bytes on the disk of the batch store. Test seam; the drive's free space by default.</summary>
        internal Func<long> FreeBytes { get; set; }

        /// <summary>The memory budget in bytes; the option by default. Test seam.</summary>
        internal long? MemoryBudgetBytes { get; set; }

        /// <summary>
        /// Passes over the blocks of the last product hour merged (1 within the budget; a single pass given up at the budget is
        /// not counted). For tests.
        /// </summary>
        internal int LastPasses { get; private set; }

        /// <summary>The feed counters the blocks it could not read are counted in at a commit (#1847); none when null.</summary>
        internal TableFeedCounters Feed { get; init; }

        private ITableBlockStore Blocks => _database.TableBlocks;

        private ITableBatchStore Batches => _database.TableBatches;

        private TableMergeStatistics Stats => Batches.MergeStatistics;


        /// <summary>One pass at <paramref name="utcNow"/>. Never throws: a fault is logged and reported.</summary>
        public TableMergePassReport RunPass(DateTime utcNow, CancellationToken token = default)
        {
            if (!Monitor.TryEnter(_runLock))
                return new TableMergePassReport { Busy = true };

            int merged = 0, deleted = 0, mismatches = 0;
            string fault = null;

            try
            {
                if (Blocks is null || Batches is null)
                    return new TableMergePassReport();

                // First, and whatever the merge below does: deleting the blocks of merged hours past their delay is what
                // gives disk back when the merge cannot write (too little free disk), so a fault of the merge never holds it off.
                deleted = DeleteDue(utcNow, token, ref fault);

                // Periods changed since the last pass (on a sensor, a folder, a product) are recorded before anything is merged.
                // A product whose placements could not be brought up to date is not merged in this pass, fail closed: the
                // merge needs a placement to know WHERE an hour's rows go, and one written after a change must be on disk
                // before any hour from its cut-off on. Whether rows have expired does not depend on it: every expiry
                // decision also reads the sensor's setting as it is then (TableBatchRetention.IsExpired), so retention and
                // the deletion of expired blocks run for such a product.
                var unreconciled = Reconcile(utcNow, ref fault);
                Checkpoint?.Invoke("reconciled");

                // Products whose merge faulted in this pass: their later hours wait for the next pass, so a product's hours
                // are committed in order; the other products go on. A product not reconciled starts as one of them.
                var faulted = new HashSet<Guid>(unreconciled.Products);

                foreach (var hour in unreconciled.All ? [] : DueHours(utcNow))
                {
                    if (token.IsCancellationRequested)
                        return new TableMergePassReport { HoursMerged = merged, HoursDeleted = deleted, Mismatches = mismatches, Fault = fault, Stopped = true,
                            ProductsNotReconciled = unreconciled.Count };

                    var outcome = MergeHour(hour, utcNow, token, faulted, ref mismatches, ref fault);
                    if (outcome == HourOutcome.Stopped)
                        return new TableMergePassReport { HoursMerged = merged, HoursDeleted = deleted, Mismatches = mismatches, Fault = fault, Stopped = true,
                            ProductsNotReconciled = unreconciled.Count };

                    if (outcome == HourOutcome.Merged)
                        merged++;
                }

                Stats.Set(ref Stats.LagHours, DueHours(utcNow).Count);

                // After the merge, so that a catch-up merges what it can before retention judges what is left.
                var (weeks, expired) = RunRetention(utcNow, token, ref fault);

                return new TableMergePassReport { HoursMerged = merged, HoursDeleted = deleted, Mismatches = mismatches, Fault = fault,
                    WeeksDropped = weeks, ExpiredHoursDeleted = expired, ProductsNotReconciled = unreconciled.Count };
            }
            catch (Exception ex)
            {
                if (Batches is not null)
                    Stats.Add(ref Stats.Faults);

                _logger.Error(ex, $"Table merge pass stopped on a fault; the hour is merged again by a later pass: {ex.Message}");
                return new TableMergePassReport { HoursMerged = merged, HoursDeleted = deleted, Mismatches = mismatches, Fault = ex.Message };
            }
            finally
            {
                Monitor.Exit(_runLock);
            }
        }

        // Whether a product's hour is committed; false when its batches cannot be opened now (its merge then faults on its
        // own, and nothing of the hour is deleted).
        private bool IsMerged(Guid product, long hour)
        {
            try
            {
                return Batches.IsHourMerged(product, hour);
            }
            catch (TableBatchStoreException)
            {
                return false;
            }
        }


        // === Which hours ===

        // Product -> its blocks, per arrival hour, in sequence order: a snapshot of the block store's index.
        private SortedDictionary<long, Dictionary<Guid, List<TableBlockIndexEntry>>> BlocksByHour()
        {
            var byHour = new SortedDictionary<long, Dictionary<Guid, List<TableBlockIndexEntry>>>();

            foreach (var product in Blocks.GetProducts())
                foreach (var entry in Blocks.GetBlocks(product))
                {
                    if (!byHour.TryGetValue(entry.ArrivalHourTicks, out var products))
                        byHour[entry.ArrivalHourTicks] = products = [];

                    if (!products.TryGetValue(product, out var list))
                        products[product] = list = [];

                    list.Add(entry);
                }

            return byHour;
        }

        // Closed hours with a product not committed (and not failed verification in this process), oldest first.
        private List<long> DueHours(DateTime utcNow)
        {
            var due = new List<long>();

            foreach (var (hour, products) in BlocksByHour())
            {
                if (hour + TimeSpan.TicksPerHour + _options.MergeGrace.Ticks > utcNow.Ticks)
                    break;

                if (products.Keys.Any(p => !IsMerged(p, hour) && !_failedVerification.Contains((p, hour))))
                    due.Add(hour);
            }

            return due;
        }


        // === One hour ===

        private enum HourOutcome { Merged, NotComplete, Stopped }

        // A fault of one product's hour (a store, the disk, a block that could not be read) leaves that product's hour
        // untouched or committed, is logged and counted, and the product's later hours wait for the next pass; the other
        // products of the hour and of later hours go on. A product's blocks are deleted only once the product has committed
        // their hour, so a product's hour left on its blocks keeps them, whatever later hours and other products do.
        private HourOutcome MergeHour(long hour, DateTime utcNow, CancellationToken token, HashSet<Guid> faulted, ref int mismatches, ref string fault)
        {
            var watch = Stopwatch.StartNew();

            // From here on no block is appended to the hour; the listing taken after it is the whole hour.
            Blocks.CloseHour(hour);

            var products = new List<(Guid Product, List<TableBlockIndexEntry> Entries)>();
            foreach (var product in Blocks.GetProducts())
            {
                var entries = Blocks.GetBlocks(product).Where(e => e.ArrivalHourTicks == hour).ToList();
                if (entries.Count > 0)
                    products.Add((product, entries));
            }

            foreach (var (product, entries) in products)
            {
                if (faulted.Contains(product) || _failedVerification.Contains((product, hour)))
                    continue;

                if (token.IsCancellationRequested)
                    return HourOutcome.Stopped;

                try
                {
                    if (Batches.IsHourMerged(product, hour))
                        continue;

                    _ensureState(product);

                    if (!MergeProduct(product, hour, entries, utcNow, token, out var stopped))
                    {
                        if (stopped)
                            return HourOutcome.Stopped;

                        _failedVerification.Add((product, hour));
                        mismatches++;
                    }
                }
                catch (Exception ex) when (token.IsCancellationRequested)
                {
                    // Stopping: the store may have closed under the call ("closed" fault); the hour is untouched or committed.
                    _logger.Info($"Table merge stopped during the hour {new DateTime(hour, DateTimeKind.Utc):yyyy-MM-dd HH:mm} of product {product}: {ex.Message}");
                    return HourOutcome.Stopped;
                }
                catch (Exception ex)
                {
                    faulted.Add(product);
                    fault = ex.Message;
                    Stats.Add(ref Stats.Faults);
                    _logger.Error(ex, $"Table hour {new DateTime(hour, DateTimeKind.Utc):yyyy-MM-dd HH:mm} of product {product} was not merged; " +
                                      $"a later pass merges it again: {ex.Message}");
                }
            }

            if (!products.All(p => IsMerged(p.Product, hour)))
                return HourOutcome.NotComplete;

            // Every product of the hour is committed: a resend of one of its blocks is a duplicate by the watermark from here
            // on, also once the blocks are deleted. (Until then the duplicate map answers it as well.)
            Blocks.RaiseWatermarks(Watermarks(products.Select(p => p.Product)));
            Checkpoint?.Invoke("watermarks-raised");

            Stats.Add(ref Stats.HoursMerged);
            Stats.Set(ref Stats.LastMergeMilliseconds, watch.ElapsedMilliseconds);
            _logger.Info($"Table hour {new DateTime(hour, DateTimeKind.Utc):yyyy-MM-dd HH:mm} merged: {products.Count} products, " +
                         $"{products.Sum(p => p.Entries.Count)} blocks, {watch.ElapsedMilliseconds} ms");

            return HourOutcome.Merged;
        }

        private List<TableBlockWatermark> Watermarks(IEnumerable<Guid> products) =>
            products.SelectMany(p => Batches.GetWatermarks(p).Select(w => new TableBlockWatermark(p, w.Key, w.Value))).ToList();


        // === One product's hour ===

        // The rows of one sensor in one hour: the tables of its blocks, in block sequence order, unchanged; and the class that
        // holds the hour for the sensor (its placement's ClassOf).
        private sealed class SensorRows(TableSensorModel sensor, int period)
        {
            public readonly TableSensorModel Sensor = sensor;
            public readonly int Period = period;
            public readonly List<BlockTable> Tables = [];
            public long Rows;
        }

        // False when a batch did not verify (nothing written; stopped false) or the server is stopping (stopped true).
        // Throws on a fault of a store: nothing of this product's hour is committed.
        private bool MergeProduct(Guid product, long hour, List<TableBlockIndexEntry> entries, DateTime utcNow, CancellationToken token, out bool stopped)
        {
            stopped = false;

            // Before the work: nothing could be written anyway (checked again before the write).
            CheckDisk();

            var budget = MemoryBudgetBytes ?? (long)_options.MergeMemoryBudgetMb * 1024 * 1024;
            var rows = entries.Sum(e => (long)e.RowCount);
            var resolved = new Dictionary<string, (TableSensorModel Sensor, TablePlacement Placement)>();
            // Whether the rows of a sensor of this hour are left out: decided once per sensor (IsLeftOut).
            var leftOut = new Dictionary<Guid, bool>();
            var unreadable = new List<long>();
            long dropped = 0, expired = 0;
            long weekStart = HSMDatabase.AccessManager.StorageWeek.GetStartTicks(hour);
            // The first hour of the placement of a sensor that gets its placement in this merge.
            long firstHour = FirstUncommittedHour(product, hour);

            // The shares of the tables, by path: one share (null: every table) while the rows held fit the budget, several
            // otherwise, each table weighed by TableBytes. The index gives the hour's rows but not their width, so
            // one pass is tried when the rows fit at the narrowest weight; it weighs each table it keeps and gives up as soon
            // as the budget is passed, before anything is encoded, and the tables are then split by their directories.
            List<HashSet<string>> shares = rows * RowBytes(1) <= budget ? [null] : Shares(product, entries, unreadable, budget);

            LastPasses = shares.Count;

            var encoded = new List<(TableSensorModel Sensor, int Period, List<TableBatchToWrite> Batches)>();
            var unreadableSet = new HashSet<long>(unreadable);
            var firstPass = true;

            for (int s = 0; s < shares.Count; s++)
            {
                var share = shares[s];

                if (token.IsCancellationRequested)
                {
                    stopped = true;
                    return false;
                }

                var sensors = new Dictionary<Guid, SensorRows>();
                long held = 0;
                var overBudget = false;

                foreach (var entry in entries)
                {
                    if (unreadableSet.Contains(entry.Sequence))
                        continue;

                    var block = ReadBlock(entry);
                    if (block is null)
                    {
                        // Read in the first pass (or the only one) and not now: the same bytes cannot be read twice
                        // differently, so it is a fault, not damage: the hour is merged again later.
                        if (!firstPass || shares.Count > 1)
                            throw new TableBlockStoreException($"Table block {entry.Sequence} read before could not be read again during the merge.");

                        unreadable.Add(entry.Sequence);
                        unreadableSet.Add(entry.Sequence);
                        continue;
                    }

                    var (envelope, table) = block.Value;

                    for (int i = 0; i < table.Tables.Count; i++)
                    {
                        if (envelope.IsRefused(i))
                            continue;

                        var t = table.Tables[i];
                        var path = Path(t.TableId);

                        if (share is not null && (path is null || !share.Contains(path)))
                            continue;

                        var (sensor, placement) = Resolve(product, path, resolved, utcNow, createFrom: firstHour);

                        if (sensor is null || entry.Sequence < sensor.FirstSequence || !Matches(sensor, t))
                        {
                            // Counted once: in the share that holds the path, or in the first pass when the path has none.
                            if (share is not null || firstPass)
                                dropped += t.RowCount;
                            continue;
                        }

                        // The sensor's rows of this hour have expired (a catch-up after a long downtime), or the week database
                        // they would go into was dropped: they are left out and counted.
                        if (!leftOut.TryGetValue(sensor.Id, out var skip))
                            leftOut[sensor.Id] = skip = IsLeftOut(product, sensor, placement, hour, weekStart, utcNow);

                        if (skip)
                        {
                            if (share is not null || firstPass)
                                expired += t.RowCount;
                            continue;
                        }

                        int period = placement.ClassOf(hour);

                        held += TableBytes(t.RowCount, t.Columns.Count, t.Columns.Count(c => c.NullMap is not null), t.TableId.Length);
                        if (share is null && held > budget)
                        {
                            overBudget = true;
                            break;
                        }

                        if (!sensors.TryGetValue(sensor.Id, out var sensorRows))
                            sensors[sensor.Id] = sensorRows = new SensorRows(sensor, period);

                        sensorRows.Tables.Add(t);
                        sensorRows.Rows += t.RowCount;
                    }

                    if (overBudget)
                        break;
                }

                if (overBudget)
                {
                    // The tables are wider than the try assumed: split them by their directories, weighed the same way, and
                    // start again. Nothing was encoded; the blocks found unreadable stay noted, the rows dropped are counted
                    // again by the shares.
                    dropped = expired = 0;
                    shares = Shares(product, entries, unreadable, budget);
                    unreadableSet.UnionWith(unreadable);
                    LastPasses = shares.Count;
                    s = -1;
                    continue;
                }

                firstPass = false;

                foreach (var sensorRows in sensors.Values)
                {
                    var batches = EncodeSensor(sensorRows);
                    if (batches is null)
                        return false;

                    encoded.Add((sensorRows.Sensor, sensorRows.Period, batches));
                }
            }

            Checkpoint?.Invoke("encoded");

            if (token.IsCancellationRequested)
            {
                stopped = true;
                return false;
            }

            // The parts: per class, the sensors by id, each sensor's batches in row order. The same for any number of passes.
            var parts = encoded.GroupBy(e => e.Period).OrderBy(g => g.Key)
                               .Select(g => new TableBatchHourPart(g.Key, g.OrderBy(e => e.Sensor.Id).SelectMany(e => e.Batches).ToList()))
                               .ToList();

            var watermarks = new Dictionary<Guid, long>(Batches.GetWatermarks(product));
            foreach (var entry in entries)
                if (!watermarks.TryGetValue(entry.ProducerId, out var current) || entry.ProducerSequence > current)
                    watermarks[entry.ProducerId] = entry.ProducerSequence;

            var commit = new TableBatchHourCommit
            {
                HourTicks = hour,
                FirstBlockSequence = entries.Min(e => e.Sequence),
                LastBlockSequence = entries.Max(e => e.Sequence),
                BlocksMerged = entries.Count - unreadable.Count,
                UnreadableBlocks = unreadable.OrderBy(s => s).ToArray(),
                Watermarks = watermarks,
            };

            CheckDisk();
            Batches.WriteHour(product, commit, parts);

            // Counted once committed.
            var unreadableSorted = new HashSet<long>(unreadable);
            Stats.Add(ref Stats.RowsMerged, parts.Sum(p => p.Batches.Sum(b => (long)b.RowCount)));
            Stats.Add(ref Stats.BatchBytesWritten, parts.Sum(p => p.Batches.Sum(b => (long)b.Batch.Length)));
            Stats.Add(ref Stats.BlockBytesMerged, entries.Where(e => !unreadableSorted.Contains(e.Sequence)).Sum(e => (long)e.StoredLength));
            Stats.Add(ref Stats.RowsDropped, dropped);
            Stats.Add(ref Stats.RowsExpired, expired);
            Stats.Add(ref Stats.UnreadableBlocksKept, unreadable.Count);
            Feed?.AddUnreadable(product, unreadable.Count);

            if (unreadable.Count > 0)
                _logger.Error($"Table hour {new DateTime(hour, DateTimeKind.Utc):yyyy-MM-dd HH:mm} of product {product}: {unreadable.Count} blocks could not be read " +
                              $"and are kept in the block store ({string.Join(", ", unreadable)})");

            return true;
        }

        // The tables of the hour split into shares whose rows fit the budget, by path: a pass over the blocks' directories
        // weighs each path's tables by TableBytes, as the pass that holds them; paths are given to shares heaviest
        // first, each to the first share it fits (a path heavier than the budget gets a share of its own). Blocks that cannot
        // be read are noted (a block noted already is not read again).
        private List<HashSet<string>> Shares(Guid product, List<TableBlockIndexEntry> entries, List<long> unreadable, long budget)
        {
            var bytesByPath = new Dictionary<string, long>();
            var noted = new HashSet<long>(unreadable);

            foreach (var entry in entries)
            {
                if (noted.Contains(entry.Sequence))
                    continue;

                var record = Blocks.ReadBlock(entry, readErrorIsFault: true);
                TableBlockDirectory directory = null;

                if (record is not null)
                {
                    try
                    {
                        directory = TableBlockReader.ReadDirectory(record.Block.Span, TableRowCollector.StoredBlockLimits);
                    }
                    catch (TableBlockFormatException ex)
                    {
                        Blocks.Statistics.Add(ref Blocks.Statistics.ReadFailures);
                        _logger.Error($"Stored table block {entry.Sequence} cannot be read by the merge ({ex.Error}): {ex.Message}");
                    }
                }

                if (directory is null)
                {
                    unreadable.Add(entry.Sequence);
                    continue;
                }

                for (int i = 0; i < directory.Entries.Count; i++)
                {
                    if (record.Envelope.IsRefused(i) || Path(directory.Entries[i].TableId) is not { } path)
                        continue;

                    var e = directory.Entries[i];
                    var nullMaps = Enumerable.Range(0, e.ColumnTypes.Count).Count(e.HasNulls);
                    bytesByPath[path] = bytesByPath.GetValueOrDefault(path) + TableBytes(e.RowCount, e.ColumnTypes.Count, nullMaps, e.TableId.Length);
                }
            }

            var shares = new List<(HashSet<string> Paths, long Bytes)>();

            foreach (var (path, bytes) in bytesByPath.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal))
            {
                var index = shares.FindIndex(s => s.Bytes + bytes <= budget);
                if (index < 0)
                {
                    shares.Add(([path], bytes));
                    continue;
                }

                shares[index].Paths.Add(path);
                shares[index] = (shares[index].Paths, shares[index].Bytes + bytes);
            }

            return shares.Count == 0 ? [new HashSet<string>()] : shares.Select(s => s.Paths).ToList();
        }

        // The envelope and every table of a stored block; null when it cannot be read (counted, logged).
        private (TableBlockEnvelope Envelope, TableBlock Block)? ReadBlock(in TableBlockIndexEntry entry)
        {
            // A read error is a fault (thrown), not damage: the hour is merged again later, never committed without the block.
            var record = Blocks.ReadBlock(entry, readErrorIsFault: true);
            if (record is null)
                return null;

            try
            {
                return (record.Envelope, TableBlockReader.Read(record.Block.Span, TableRowCollector.StoredBlockLimits));
            }
            catch (TableBlockFormatException ex)
            {
                Blocks.Statistics.Add(ref Blocks.Statistics.ReadFailures);
                _logger.Error($"Stored table block {entry.Sequence} cannot be read by the merge ({ex.Error}): {ex.Message}");
                return null;
            }
        }

        // Whether the merge leaves out a sensor's rows of an hour.
        // - They have expired, by the one rule (TableBatchRetention.IsExpired of the placement and the sensor's setting as
        //   read now): the class that holds the hour, the stored target and the setting have all passed for its week. A
        //   sensor whose period was lengthened before its week expired keeps the rows of an hour that waited for its merge
        //   past the old period: they are written into the class that holds the hour, whose week database the retention
        //   guard keeps.
        // - Or the week database they would go into was dropped by retention (its record): the rows merged there went
        //   when the period the sensor had then passed, a change of period after that does not bring them back, and a
        //   write into a dropped database is refused (it would hold the product's hour, and every later one, back for ever).
        private bool IsLeftOut(Guid product, TableSensorModel sensor, TablePlacement placement, long hour, long weekStart, DateTime utcNow) =>
            TableBatchRetention.IsExpired(placement, hour, utcNow, TablePeriodClass.Of(sensor, utcNow)) ||
            Batches.IsWeekDropped(product, new TableBatchWeek(weekStart, placement.ClassOf(hour)));

        // The table sensor on a path and its placement (#1850 part 4b), for the merge: it writes the rows of an hour into the
        // class the placement gives it (ClassOf) and every read looks there; whether rows have expired is asked of the
        // placement and the sensor's setting (TableBatchRetention.IsExpired). The merge creates the placement of a sensor that
        // has none (createFrom: the first hour of such a placement). Retention writes no placement (StoredPlacement).
        private (TableSensorModel Sensor, TablePlacement Placement) Resolve(Guid product, string path,
            Dictionary<string, (TableSensorModel, TablePlacement)> resolved, DateTime utcNow, long? createFrom)
        {
            if (path is null)
                return (null, null);

            if (!resolved.TryGetValue(path, out var found))
            {
                found = _cache.TryGetSensorByPath(product, path, out var sensor) && sensor is TableSensorModel table
                    ? (table, PlaceSensor(product, table, utcNow, createFrom))
                    : (null, null);

                resolved[path] = found;
            }

            return found;
        }

        private static string Path(byte[] tableId)
        {
            try
            {
                return StrictUtf8.GetString(tableId);
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
        }

        // The put checked the types against the schema; a table that differs is not the sensor's (a damaged store).
        private static bool Matches(TableSensorModel sensor, BlockTable table)
        {
            if (table.Columns.Count != sensor.Schema.Columns.Count)
                return false;

            for (int c = 0; c < table.Columns.Count; c++)
                if (table.Columns[c].Type != sensor.Schema.Columns[c].Type)
                    return false;

            return true;
        }


        // === Encoding and verification ===

        // The sensor's batches of the hour in row order, each verified; null when one does not verify (logged, counted).
        private List<TableBatchToWrite> EncodeSensor(SensorRows rows)
        {
            int count = checked((int)rows.Rows);
            var schema = rows.Sensor.Schema;

            // Flattened in block sequence order, then row order: the index order is (block sequence, row).
            var tableOf = new int[count];
            var rowOf = new int[count];
            var time = new long[count];
            int n = 0;

            for (int t = 0; t < rows.Tables.Count; t++)
            {
                var ticks = rows.Tables[t].Time;
                for (int r = 0; r < ticks.Length; r++, n++)
                {
                    tableOf[n] = t;
                    rowOf[n] = r;
                    time[n] = ticks[r];
                }
            }

            var order = new int[count];
            for (int i = 0; i < count; i++)
                order[i] = i;

            var sorted = true;
            for (int i = 1; i < count && sorted; i++)
                sorted = time[i] >= time[i - 1];

            // Stable by (time, index): equal times keep (block sequence, row).
            if (!sorted)
                Array.Sort(order, (a, b) => time[a] != time[b] ? time[a].CompareTo(time[b]) : a.CompareTo(b));

            int batchCount = (count + TableBatchCodec.MaxRows - 1) / TableBatchCodec.MaxRows;
            int baseSize = count / batchCount, larger = count % batchCount;
            var result = new List<TableBatchToWrite>(batchCount);
            int start = 0;

            for (int b = 0; b < batchCount; b++)
            {
                int size = baseSize + (b < larger ? 1 : 0);
                var batch = Slice(rows, schema, order, tableOf, rowOf, time, start, size);
                start += size;

                byte[] bytes;
                string failure;

                try
                {
                    bytes = TableBatchCodec.Encode(batch);
                    var decoded = TableBatchCodec.Decode(bytes);
                    AfterDecode?.Invoke(rows.Sensor.Id, decoded);
                    failure = Compare(batch, decoded);
                }
                catch (Exception ex) when (ex is TableBatchFormatException or ArgumentException)
                {
                    bytes = null;
                    failure = ex.Message;
                }

                if (failure is not null)
                {
                    Stats.Add(ref Stats.VerificationMismatches);
                    _logger.Error($"Table merge: a batch of sensor {rows.Sensor.Id} ({rows.Sensor.FullPath}) does not verify ({failure}); " +
                                  "nothing of its product's hour is written, its blocks stay, and it is tried again after a restart");
                    return null;
                }

                result.Add(new TableBatchToWrite(rows.Sensor.Id, batch.Time[0], batch.Time[^1], size, bytes));
            }

            return result;
        }

        private static TableBatch Slice(SensorRows rows, TableSchema schema, int[] order, int[] tableOf, int[] rowOf, long[] time, int start, int size)
        {
            var columns = new TableBatchColumn[schema.Columns.Count];
            var ticks = new long[size];

            for (int i = 0; i < size; i++)
                ticks[i] = time[order[start + i]];

            columns[0] = TableBatchColumn.Time(ticks);

            for (int c = 1; c < columns.Length; c++)
            {
                byte[] nulls = null;
                var isDouble = schema.Columns[c].Type == TableColumnType.Double;
                var doubles = isDouble ? new double[size] : null;
                var longs = isDouble ? null : new long[size];

                for (int i = 0; i < size; i++)
                {
                    var index = order[start + i];
                    var column = rows.Tables[tableOf[index]].Columns[c];
                    var row = rowOf[index];

                    if (column.NullMap is { } map && TableNullMap.IsNull(map, row))
                    {
                        TableNullMap.SetNull(nulls ??= new byte[TableNullMap.ByteLength(size)], i);
                        continue;
                    }

                    if (isDouble)
                        doubles[i] = column.DoubleValues[row];
                    else
                        longs[i] = column.Int64Values[row];
                }

                columns[c] = new TableBatchColumn(schema.Columns[c].Type, longs, doubles, nulls);
            }

            return new TableBatch(schema.Id, columns);
        }

        // Null when the decoded batch holds the same rows bit for bit: every value of a row with one (doubles by their bits)
        // and every null.
        internal static string Compare(TableBatch expected, TableBatch decoded)
        {
            if (decoded.SchemaId != expected.SchemaId)
                return $"schema id {decoded.SchemaId}, not {expected.SchemaId}";

            if (decoded.Columns.Count != expected.Columns.Count || decoded.RowCount != expected.RowCount)
                return $"{decoded.Columns.Count} columns of {decoded.RowCount} rows, not {expected.Columns.Count} of {expected.RowCount}";

            for (int c = 0; c < expected.Columns.Count; c++)
            {
                var e = expected.Columns[c];
                var d = decoded.Columns[c];

                if (d.Type != e.Type)
                    return $"column {c} of type {d.Type}, not {e.Type}";

                for (int row = 0; row < expected.RowCount; row++)
                {
                    var isNull = e.IsNull(row);
                    if (d.IsNull(row) != isNull)
                        return $"column {c}, row {row}: null {d.IsNull(row)}, not {isNull}";

                    if (isNull)
                        continue;

                    var same = e.Type == TableColumnType.Double
                        ? BitConverter.DoubleToInt64Bits(d.DoubleValues[row]) == BitConverter.DoubleToInt64Bits(e.DoubleValues[row])
                        : d.Int64Values[row] == e.Int64Values[row];

                    if (!same)
                        return $"column {c}, row {row}: another value";
                }
            }

            return null;
        }

        private void CheckDisk()
        {
            if (_options.MinFreeDiskMb <= 0)
                return;

            var minimum = _options.MinFreeDiskMb * 1024 * 1024;
            var folder = _database.DatabaseSettings.PathToTableBlocks;
            var free = FreeBytes?.Invoke() ?? new DriveInfo(System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(folder))).AvailableFreeSpace;

            if (free < minimum)
                throw new TableBatchStoreException($"Not enough free disk space to write table batches: {free} bytes free, {minimum} required.");
        }


        // === Deleting merged hours ===

        // Per arrival hour and per product that committed it: the product's blocks of the hour, except the blocks its merge
        // could not read, once the delay has passed since its commit. A product that has not committed the hour keeps its
        // own blocks and holds no other product's: the block store deletes by sequence (DeleteMergedHour's keep), so a
        // product that never merges (a batch that never verifies, placements that cannot be written) cannot hold the
        // blocks of another product of the same hour for ever.
        private int DeleteDue(DateTime utcNow, CancellationToken token, ref string fault)
        {
            int deleted = 0, holding = 0;
            var delay = TimeSpan.FromHours(_options.DeleteMergedBlocksAfterHours).Ticks;
            SortedDictionary<long, Dictionary<Guid, List<TableBlockIndexEntry>>> byHour;

            try
            {
                byHour = BlocksByHour();
            }
            catch (Exception ex)
            {
                fault = ex.Message;
                Stats.Add(ref Stats.Faults);
                _logger.Error(ex, $"Table merge: the blocks to delete could not be listed: {ex.Message}");
                return 0;
            }

            foreach (var (hour, products) in byHour)
            {
                try
                {
                    // The sequences of the hour that stay, and the products whose blocks go.
                    var keep = new HashSet<long>();
                    var due = new List<Guid>();
                    var waiting = false;

                    foreach (var (product, entries) in products)
                    {
                        TableBatchHour record;
                        try
                        {
                            record = Batches.GetHour(product, hour);
                        }
                        catch (Exception ex)
                        {
                            // This product's batches cannot be read now: its blocks stay, the other products' go on.
                            keep.UnionWith(entries.Select(e => e.Sequence));
                            fault = ex.Message;
                            Stats.Add(ref Stats.Faults);
                            _logger.Error(ex, $"Table merge: the blocks of product {product} in the hour {new DateTime(hour, DateTimeKind.Utc):yyyy-MM-dd HH:mm} were not " +
                                              $"deleted, its merged hours could not be read; the next pass tries again: {ex.Message}");
                            continue;
                        }

                        // Not committed for this product: its blocks serve its reads and wait for its merge.
                        if (record is null)
                        {
                            keep.UnionWith(entries.Select(e => e.Sequence));
                            continue;
                        }

                        var unreadable = record.UnreadableBlocks.ToHashSet();
                        keep.UnionWith(entries.Where(e => unreadable.Contains(e.Sequence)).Select(e => e.Sequence));

                        if (entries.All(e => unreadable.Contains(e.Sequence)))
                            continue;

                        if (token.IsCancellationRequested || utcNow.Ticks < record.CommitTicks + delay)
                        {
                            keep.UnionWith(entries.Select(e => e.Sequence));
                            waiting = true;
                            continue;
                        }

                        due.Add(product);
                    }

                    if (waiting)
                        holding++;

                    if (due.Count == 0)
                        continue;

                    using (_gate.Delete())
                        Blocks.DeleteMergedHour(hour, keep, Watermarks(due));

                    deleted++;
                }
                catch (Exception ex)
                {
                    // One hour that cannot be deleted (a product's batches that cannot be opened, a store fault) does not hold
                    // the others off; it is tried again by the next pass.
                    fault = ex.Message;
                    Stats.Add(ref Stats.Faults);
                    _logger.Error(ex, $"Table merge: the blocks of the merged hour {new DateTime(hour, DateTimeKind.Utc):yyyy-MM-dd HH:mm} were not deleted; " +
                                      $"the next pass tries again: {ex.Message}");
                }
            }

            Stats.Set(ref Stats.MergedHoursHoldingBlocks, holding);
            return deleted;
        }
    }
}
