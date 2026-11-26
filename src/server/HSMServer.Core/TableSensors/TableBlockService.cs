using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using HSMCommon.TaskResult;
using HSMDatabase.AccessManager;
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
    /// <summary>
    /// Puts the blocks of table sensors into the block store and reads the rows of one sensor back (ADR-0021 points 8 and
    /// 13, #1844). The internal interface the transport (#1845) calls.
    /// </summary>
    public interface ITableBlockService
    {
        /// <summary>
        /// Checks a block and stores it unchanged. The block is verified (header, checksum, frame, directory, limits); each
        /// table is matched against the table sensor with the same path in <paramref name="productId"/>, and a table that
        /// does not match is refused and recorded with the block (its rows are never served). A block whose
        /// <paramref name="producerId"/> and <paramref name="producerSequence"/> are stored already for
        /// <paramref name="productId"/> is not stored again and answered as it was the first time. A block every table of
        /// which is refused is not stored.
        /// <para>Refusals are results. Throws <see cref="TableBlockStoreException"/> when the store cannot be used: a fault,
        /// the caller answers 5xx; the block is not stored and must not be acknowledged.</para>
        /// </summary>
        TableBlockPutResult Put(Guid productId, Guid producerId, long producerSequence, byte[] block);

        /// <summary>
        /// The rows of a table sensor with a time in <c>[from, to)</c>, sorted by time, equal times in the order they
        /// arrived; at most <paramref name="maxRows"/> (null: the configured maximum), the earliest ones, marked truncated
        /// when more matched. An error for an unknown id or a sensor that is not a table sensor. An arrival hour committed
        /// by the merge (#1850) is read from its batches only, any other from its blocks only.
        /// <para>Times are UTC: a <see cref="DateTimeKind.Unspecified"/> time is taken as UTC (as the ticks of a row are),
        /// a <see cref="DateTimeKind.Local"/> one is converted.</para>
        /// <para>Throws <see cref="TableBlockStoreException"/> when the store cannot be used: a fault, the caller answers
        /// 5xx. It is not turned into an error result, which says the request is wrong.</para>
        /// <para>Throws <see cref="TableReadChangedException"/> when the store changed (an hour committed, a week dropped)
        /// while each of <see cref="TableBlockService.MaxChartPlans"/> plans was taken: the same request later is answered,
        /// the caller answers 503 (#1847).</para>
        /// <para>A truncated result ends at a time boundary and says where the next page starts
        /// (<see cref="TableRows.NextFromTicks"/>, #1845). <paramref name="token"/> is checked before every block: a
        /// cancelled read throws <see cref="OperationCanceledException"/>.</para>
        /// </summary>
        TaskResult<TableRows> Read(Guid sensorId, DateTime from, DateTime to, int? maxRows = null, CancellationToken token = default);
    }


    /// <summary>
    /// <para>
    /// <b>A sensor created during a put.</b> A put reserves its block's sequence before it matches the tables against the
    /// sensors; a new table sensor records, before it can be seen, the sequence the next reservation will get
    /// (<see cref="TableSensorModel.FirstSequence"/>). A read of a sensor takes only blocks at or above its first sequence,
    /// and a put refuses a table whose sensor's first sequence is above the block's (<see cref="TableRefusalReason.SensorCreatedAfterBlock"/>).
    /// So a block numbered before a sensor existed is never read for it (its tables were matched against an earlier
    /// sensor of the path or against none), and every table a put accepts for a sensor is in a block the sensor reads.
    /// A sensor's removal happens before a new sensor on the same path is created, and a removed sensor is never read.
    /// </para>
    /// </summary>
    public sealed class TableBlockService : ITableBlockService, ITableChartReader
    {
        private static readonly Logger _logger = LogManager.GetLogger("TableBlocks");
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);
        private static readonly TimeSpan RefusalLogInterval = TimeSpan.FromMinutes(1);

        private readonly IDatabaseCore _database;
        private readonly ITreeValuesCache _cache;
        private readonly TableBlockOptions _options;

        private readonly TableHourGate _gate = new();

        // The gate, for tests.
        internal TableHourGate HourGate => _gate;

        private readonly object _refusalLogLock = new();
        private readonly Dictionary<TableRefusalReason, long> _refusalsSinceLog = [];
        private DateTime _lastRefusalLog = DateTime.MinValue;


        public TableBlockService(IDatabaseCore database, ITreeValuesCache cache, TableBlockOptions options = null)
        {
            _database = database;
            _cache = cache;
            _options = options ?? new TableBlockOptions();

            // Mandatory: a database without a block store cannot take table blocks, and saying so at construction is
            // clearer than a null reference at the first put.
            var store = _database.TableBlocks ?? throw new ArgumentException("The database has no table block store.", nameof(database));
            store.MinFreeBytes = _options.MinFreeDiskMb * 1024 * 1024;

            if (_database.TableBatches is { } batches)
                batches.MaxOpenDatabases = _options.MaxOpenBatchDatabases;

            // Nothing is opened here: a store that cannot open costs table puts only, never the construction of this
            // service or of whatever it is injected into. The sequence floor is raised by the first put (EnsureSequenceFloor).
            Merge = new TableMergeJob(_database, _cache, _options, _gate, EnsureMergeState) { Feed = Feed };
        }


        /// <summary>The counters the feed sensors of table sensors are posted from (#1847).</summary>
        public TableFeedCounters Feed { get; } = new();


        /// <summary>The hourly merge of blocks into batches (#1850), run by the server's background service.</summary>
        public TableMergeJob Merge { get; }


        private ITableBlockStore Store => _database.TableBlocks;

        // Set once the sequence floor from the sensors is in the store; guarded by _floorLock.
        private readonly object _floorLock = new();
        private volatile bool _floorRaised;

        // Called with the reserved sequence before the tables are matched. Test seam for a sensor created during a put.
        internal Action<long> AfterReserve { get; set; }

        // Called before a read decompresses a block. Test seam for a read that is cancelled between blocks.
        internal Action<TableBlockIndexEntry> BeforeBlockRead { get; set; }

        // Called when a chart finds the batch store changed since its plan (the generation), with the hour of the source it
        // was about to read or to stop before, before it plans again. Test seam.
        internal Action<long> BeforeChartReplan { get; set; }

        // Called after a chart planned its read (with the number of the plan, from 1), before the first source. Test seam for
        // a commit between the plan and the read.
        internal Action<int> AfterChartPlan { get; set; }

        /// <summary>The plans one chart or one read makes at most before it gives up with <see cref="TableReadChangedException"/>.</summary>
        internal const int MaxChartPlans = 3;

        // Called inside a plan: "hours" after the merged hours were listed, "blocks" after the blocks were listed. Test seam
        // for a merge between the steps of a plan.
        internal Action<string> PlanStep { get; set; }

        // Called at the end of every plan with whether it is stale. Test seam.
        internal Action<bool> PlanTaken { get; set; }


        public TableBlockPutResult Put(Guid productId, Guid producerId, long producerSequence, byte[] block)
        {
            TableBlockDirectory directory;

            try
            {
                directory = TableBlockReader.ReadDirectory(block, _options.Limits);
            }
            catch (TableBlockFormatException ex)
            {
                Store.Statistics.Add(ref Store.Statistics.InvalidBlocks);
                _logger.Debug($"Table block {producerId}/{producerSequence} of product {productId} refused ({ex.Error}): {ex.Message}");

                return new TableBlockPutResult { Outcome = TableBlockPutOutcome.Invalid, FormatError = ex.Error, Message = ex.Message };
            }

            var store = Store;
            var header = directory.Header;

            EnsureSequenceFloor(store);
            EnsureMergeState(productId);
            var ticket = store.Reserve();

            try
            {
                AfterReserve?.Invoke(ticket.Sequence);

                var refusals = Match(productId, directory, ticket.Sequence, out var detailed, out var accepted);
                var envelope = new TableBlockEnvelope
                {
                    ProductId = productId,
                    ProducerId = producerId,
                    ProducerSequence = producerSequence,
                    Refusals = refusals.ToArray(),
                };

                var allRefused = refusals.Count == directory.Entries.Count;
                var appended = store.Append(ticket, envelope, block, header.FirstTime, header.LastTime, header.RowCount, header.TableCount, store: !allRefused);

                switch (appended.Outcome)
                {
                    case TableBlockAppendOutcome.Duplicate when appended.Merged:
                        return new TableBlockPutResult
                        {
                            Outcome = TableBlockPutOutcome.Duplicate,
                            TableCount = directory.Entries.Count,
                            RefusalsKnown = false,
                            Message = "The block was stored before and its hour has been merged; its sequence and refusals are not known any more.",
                        };

                    case TableBlockAppendOutcome.Duplicate:
                        return new TableBlockPutResult
                        {
                            Outcome = TableBlockPutOutcome.Duplicate,
                            Sequence = appended.Entry.Sequence,
                            TableCount = appended.Entry.TableCount,
                            Refusals = (appended.Envelope?.Refusals ?? []).Take(TableBlockPutResult.MaxDetailedRefusals)
                                .Select(r => new TableRefusalInfo(r.Index, TableIdText(directory, r.Index), r.Reason)).ToList(),
                            RefusedCount = appended.Envelope?.Refusals.Count ?? 0,
                            RefusalsKnown = appended.Envelope is not null,
                            Message = appended.Envelope is null ? "The stored block's envelope could not be read; its refusals are not known." : null,
                        };

                    case TableBlockAppendOutcome.NotStored:
                        CountRefusals(refusals, detailed);
                        Feed.AddRefused(productId, refusals.Count);
                        return new TableBlockPutResult
                        {
                            Outcome = TableBlockPutOutcome.AllRefused,
                            TableCount = directory.Entries.Count,
                            Refusals = detailed,
                            RefusedCount = refusals.Count,
                            Message = "Every table of the block was refused; nothing was stored.",
                        };

                    default:
                        CountRefusals(refusals, detailed);
                        Feed.AddStored(productId, accepted, appended.Entry.StoredLength, refusals.Count, header.LastTime, appended.Entry.ArrivalTicks);
                        return new TableBlockPutResult
                        {
                            Outcome = TableBlockPutOutcome.Stored,
                            Sequence = appended.Entry.Sequence,
                            TableCount = directory.Entries.Count,
                            Refusals = detailed,
                            RefusedCount = refusals.Count,
                        };
                }
            }
            finally
            {
                if (!ticket.IsDone)
                    store.Skip(ticket);
            }
        }

        public TaskResult<TableRows> Read(Guid sensorId, DateTime from, DateTime to, int? maxRows = null, CancellationToken token = default)
        {
            if (_cache.GetSensor(sensorId) is not { } sensor)
                return TaskResult<TableRows>.FromError($"Sensor {sensorId} was not found.");

            if (sensor is not TableSensorModel table)
                return TaskResult<TableRows>.FromError($"Sensor '{sensor.FullPath}' is not a table sensor.");

            var limit = maxRows is > 0 ? Math.Min(maxRows.Value, _options.MaxReadRows) : _options.MaxReadRows;
            var fromTicks = Utc(from).Ticks;
            var toTicks = Utc(to).Ticks;

            try
            {
                using (_gate.Read())
                    return TaskResult<TableRows>.FromValue(ReadRows(table, fromTicks, toTicks, limit, token));
            }
            catch (TableBatchStoreException ex)
            {
                // One kind of fault for the caller: the store cannot be used, answered 5xx.
                throw new TableBlockStoreException(ex.Message, ex);
            }
        }

        public TaskResult<TableChart> Reduce(Guid sensorId, DateTime from, DateTime to, IReadOnlyList<int> columns, int buckets, CancellationToken token = default)
        {
            if (_cache.GetSensor(sensorId) is not { } sensor)
                return TaskResult<TableChart>.FromError($"Sensor {sensorId} was not found.");

            if (sensor is not TableSensorModel table)
                return TaskResult<TableChart>.FromError($"Sensor {sensorId} is not a table sensor.");

            var fromTicks = Utc(from).Ticks;
            var toTicks = Utc(to).Ticks;

            if (fromTicks >= toTicks)
                return TaskResult<TableChart>.FromError("From must be before To: the range is [From, To).");

            if (buckets < 1 || buckets > TableChartReducer.MaxBuckets)
                return TaskResult<TableChart>.FromError($"The number of buckets must be 1..{TableChartReducer.MaxBuckets}; it is {buckets}.");

            columns ??= [];
            var schema = table.Schema.Columns;
            var seen = new HashSet<int>();

            foreach (var column in columns)
            {
                if (column < 1 || column >= schema.Count)
                    return TaskResult<TableChart>.FromError($"Column {column} is not a value column: the value columns are 1..{schema.Count - 1}.");

                // A time column after the first is legal in a schema, but it is not a value to plot: a chart has minima and
                // maxima of int64 and double columns only.
                if (schema[column].Type == TableColumnType.Time)
                    return TaskResult<TableChart>.FromError($"Column {column} is not a value column: it is a time column.");

                if (!seen.Add(column))
                    return TaskResult<TableChart>.FromError($"Column {column} is named twice.");
            }

            try
            {
                return TaskResult<TableChart>.FromValue(ReduceRows(table, fromTicks, toTicks, columns, buckets, token));
            }
            catch (TableBatchStoreException ex)
            {
                throw new TableBlockStoreException(ex.Message, ex);
            }
        }

        // A chart reads for seconds, so it does not hold the gate for its whole range (a deletion waiting for the gate would
        // stall every other read behind it): it holds it per source. The plan is taken outside the gate. Under the gate, no
        // source is read once the sensor's generation in the batch store is not the plan's any more (ChangedSincePlan): an
        // hour of the plan's blocks may have been committed (its blocks may be gone, its batches may hold rows the plan did
        // not know, earlier ones too, which would make the plan's order of sources wrong, and the commit's record may list
        // blocks it could not read), a week database of the plan may have been dropped (the rest of the week would read
        // nothing, an answer with part of a week), or the sensor's placement written (an hour's rows may be in another
        // class). A placement or a move of another sensor of the product does not count (#1850 part 4c). The chart then
        // drops what it has and plans again, so every answer comes from one plan whose hours are each read from the source
        // the plan chose; a plan is never patched in place. An hour still not committed keeps its blocks while the gate is
        // held (they are deleted only after the commit, under the write side). After MaxChartPlans plans that each met a
        // change it gives up: TableReadChangedException, never a partial answer.
        private TableChart ReduceRows(TableSensorModel table, long fromTicks, long toTicks, IReadOnlyList<int> columns, int buckets, CancellationToken token)
        {
            for (int attempt = 1; ; attempt++)
            {
                var plan = Plan(table, fromTicks, toTicks);
                AfterChartPlan?.Invoke(attempt);

                var reducer = new TableChartReducer(table.Schema, fromTicks, toTicks, columns, buckets, _options.MaxChartScanRows);

                if (!plan.Stale && TryReduce(plan, reducer, fromTicks, toTicks, token))
                    return reducer.Build();

                // A chart cancelled meanwhile (while it waited for the gate, say) is cancelled, not a store that changed.
                token.ThrowIfCancellationRequested();

                if (attempt == MaxChartPlans)
                    throw new TableReadChangedException($"The table store changed during the chart of '{table.FullPath}': {MaxChartPlans} plans each met a change. Ask again.");
            }
        }

        // Whether the batch store changed since the plan was taken: the sensor's generation is not the plan's. The one test,
        // as in Plan: a commit (of any hour of the product), a week database created by the write of an hour or dropped,
        // hours forgotten, the sensor's placement written, the sensor's moved batches written (a placement or moved batches of
        // another sensor do not count, #1850 part 4c). Called with the source the chart is about to read or to stop before.
        private bool ChangedSincePlan(ReadPlan plan, ReadSourceItem source)
        {
            if (plan.Batches is null || plan.Batches.GetGeneration(plan.Product, plan.SensorId) == plan.Generation)
                return false;

            BeforeChartReplan?.Invoke(source.Hour);
            return true;
        }

        // False when the batch store changed since the plan: the plan is stale.
        private bool TryReduce(ReadPlan plan, TableChartReducer reducer, long fromTicks, long toTicks, CancellationToken token)
        {
            reducer.CountKeptUnreadableBlocks(plan.KeptUnreadable);

            for (int i = 0; i < plan.Sources.Count; i++)
            {
                var source = plan.Sources[i];

                // A source the stop decision names must still be what the plan says: an hour of blocks merged since may hold
                // rows earlier than the plan knew.
                if (ChangedSincePlan(plan, source))
                    return false;

                if (i > 0 && reducer.CanStopBefore(source.MinTime))
                    break;

                token.ThrowIfCancellationRequested();

                using (_gate.Read())
                {
                    // Again under the gate: the blocks of an hour stay only while it is not merged, and a week database only
                    // until retention drops it (under the write side). The read of a source checks the token before every
                    // block and every merged hour, so a chart cancelled while it waited for the gate reads nothing.
                    if (ChangedSincePlan(plan, source))
                        return false;

                    ReadSource(plan, source, reducer, fromTicks, toTicks, token);
                }
            }

            return true;
        }

        // Under the read side of the gate, so no hour's blocks are deleted meanwhile.
        //
        // The read rule (#1850): an arrival hour is read from its batches when it is committed for the product, otherwise from
        // its blocks; never both, never neither. The plan (Plan) is taken at one generation of the batch store, so its list of
        // merged hours, its block listing and its answers to "is this hour committed" are of one moment; a plan during which
        // the generation changed is stale and taken again. An hour of the plan's blocks committed after the plan keeps its
        // blocks: they are deleted only after the commit, under the write side of the gate this read holds.
        //
        // Sources (the blocks of one arrival hour, or one merged hour) are visited in the order of their earliest row in the
        // range, and the read stops once it holds the limit, has dropped a row past it, and no source left can start before
        // the latest row it keeps.
        //
        // The token is checked before every block and every merged hour (#1845): a read whose caller went away stops
        // decompressing.
        private TableRows ReadRows(TableSensorModel table, long fromTicks, long toTicks, int limit, CancellationToken token)
        {
            // A stale plan (the store changed while it was taken) is taken again and never read: an hour committed during it
            // could be in neither of its sources. The gate is held, so only a commit or a drop can come between the steps.
            // After MaxChartPlans stale plans the read gives up with TableReadChangedException.
            var plan = Plan(table, fromTicks, toTicks);
            for (int attempt = 1; plan.Stale; attempt++)
            {
                token.ThrowIfCancellationRequested();

                if (attempt == MaxChartPlans)
                    throw new TableReadChangedException($"The table store changed during the read of '{table.FullPath}': {MaxChartPlans} plans each met a change. Ask again.");

                plan = Plan(table, fromTicks, toTicks);
            }

            var collector = new TableRowCollector(table.Schema, fromTicks, toTicks, limit);
            collector.CountKeptUnreadableBlocks(plan.KeptUnreadable);

            for (int i = 0; i < plan.Sources.Count; i++)
            {
                var source = plan.Sources[i];

                if (i > 0 && collector.CanStopBefore(source.MinTime))
                    break;

                ReadSource(plan, source, collector, fromTicks, toTicks, token);
            }

            return collector.Build();
        }

        // One source of a read: the blocks of one arrival hour not merged, or one merged hour and the class that holds the
        // sensor's rows of it (its placement, #1850 part 4b).
        private readonly record struct ReadSourceItem(long MinTime, long Hour, List<TableBlockIndexEntry> Blocks, int Period = 0);

        // What a read of one table sensor visits, and in which order (the read rule above ReadRows); the blocks in its range
        // that committed hours list as unreadable (kept): counted as failed blocks, as they were before their hour was merged;
        // the sensor's generation in the batch store the plan was taken at; and Stale: that generation changed while the plan
        // was taken (an hour committed, a week dropped, the sensor's placement written), so its block listing, its committed
        // hours, its week databases and the sensor's placement may not be of one moment.
        private sealed record ReadPlan(Guid Product, ITableBlockStore Store, ITableBatchStore Batches, byte[] TableId, Guid SensorId,
            List<ReadSourceItem> Sources, int KeptUnreadable, long Generation, bool Stale);

        // The sensor's generation is read first, then the merged hours and the week databases, the blocks, whether each hour
        // of a candidate block is committed, the sensor's placement, and the generation once more last. The batch store
        // changes it under the product's lock with every write this plan can see (ITableBatchStore.GetGeneration(product,
        // sensor)): every commit (one without any batch class too, which no list of merged hours shows), every week database
        // created by the write of an hour or dropped, hours forgotten, a rebuild of its hours or of its placements, and the
        // sensor's own placement or moved batches written. A placement or moved batches of another sensor do not change it
        // (#1850 part 4c): the plan reads no other sensor's placement, and another sensor's moved batches hold none of this
        // sensor's rows. The generation is the only test of staleness: no list is compared. When the two readings are equal,
        // nothing changed while the plan was taken: an hour the plan reads from its batches was committed before the blocks
        // were listed, so its batches hold nothing newer than that listing; an hour it reads from its blocks was not committed
        // when it was asked; an hour found committed is in the list of merged hours; the class of each hour is the one that
        // holds the sensor's rows, and its database is in the list of week databases. Otherwise the plan is stale: a commit
        // between the steps could have made an hour's batches hold rows newer than the listing (earlier in time than the
        // plan's sources, which would make its order and early stop wrong), or left an hour in neither source (committed
        // after the list of merged hours and before it was asked about, or committed with no class while a row of the range
        // arrived in a later hour after the listing); a week dropped or the sensor's placement written between the steps
        // could have made the plan look for an hour's rows where they are not.
        private ReadPlan Plan(TableSensorModel table, long fromTicks, long toTicks)
        {
            var product = table.Root.Id;
            var store = Store;
            var batches = _database.TableBatches;
            var tableId = Encoding.UTF8.GetBytes(table.Path);

            var generation = batches?.GetGeneration(product, table.Id) ?? 0;

            IReadOnlyList<TableBatchHour> hours = batches?.GetHours(product) ?? [];
            // The week databases of the same generation: a class database dropped is not in the plan (an hour keeps the
            // classes of its record after one of them is dropped, #1850 part 4b, so the hours alone do not show it).
            IReadOnlyList<TableBatchWeek> weeks = batches?.GetWeeks(product) ?? [];
            PlanStep?.Invoke("hours");

            var blocks = store.GetBlocks(product);
            PlanStep?.Invoke("blocks");

            // The record of a committed hour, null for an hour that is not: asked once per hour with candidate blocks.
            var committed = new Dictionary<long, TableBatchHour>();
            var blockHours = new SortedDictionary<long, List<TableBlockIndexEntry>>();
            int keptUnreadable = 0;

            for (int b = 0; b < blocks.Count; b++)
            {
                var entry = blocks[b];
                if (entry.Sequence < table.FirstSequence || entry.FirstTime >= toTicks || entry.LastTime < fromTicks)
                    continue;

                var hour = entry.ArrivalHourTicks;
                if (!committed.TryGetValue(hour, out var record))
                    committed[hour] = record = batches?.GetHour(product, hour);

                if (record is not null)
                {
                    // A block the merge could not read stays, listed in the hour's record: its rows are not in the batches.
                    // Whether it held rows of this sensor is not known, so it counts, as it did before the merge (in range
                    // and at or above the sensor's first sequence, the same test as for the blocks of an unmerged hour).
                    if (record.UnreadableBlocks.Contains(entry.Sequence))
                        keptUnreadable++;
                    continue;
                }

                if (!blockHours.TryGetValue(hour, out var list))
                    blockHours[hour] = list = [];
                list.Add(entry);
            }

            var sources = new List<ReadSourceItem>();

            foreach (var (hour, list) in blockHours)
            {
                long min = long.MaxValue;
                foreach (var entry in list)
                    min = Math.Min(min, entry.FirstTime);

                sources.Add(new ReadSourceItem(Math.Max(min, fromTicks), hour, list));
            }

            // A sensor without a placement has no batch (the merge writes the placement before its first batch).
            if (batches?.GetPlacement(product, table.Id) is { } placement)
            {
                var weekSet = weeks.ToHashSet();

                foreach (var hour in hours)
                {
                    // Taken from its blocks above: it was not committed when that was decided.
                    if (committed.TryGetValue(hour.HourTicks, out var record) && record is null)
                        continue;

                    // Every block of the hour is below the sensor's first sequence: none of its rows are the sensor's.
                    if (hour.LastBlockSequence < table.FirstSequence)
                        continue;

                    // The class that holds the sensor's rows of the hour; its database of the week must exist.
                    int period = placement.ClassOf(hour.HourTicks);
                    if (!weekSet.Contains(new TableBatchWeek(StorageWeek.GetStartTicks(hour.HourTicks), period)))
                        continue;

                    var (min, max) = RowTimes(hour);

                    if (min < toTicks && max >= fromTicks)
                        sources.Add(new ReadSourceItem(Math.Max(min, fromTicks), hour.HourTicks, null, period));
                }
            }

            // The only test of staleness: the sensor's generation, changed by every write this plan can see.
            var stale = batches is not null && batches.GetGeneration(product, table.Id) != generation;
            PlanTaken?.Invoke(stale);

            sources.Sort((a, b) => a.MinTime != b.MinTime ? a.MinTime.CompareTo(b.MinTime) : a.Hour.CompareTo(b.Hour));

            return new ReadPlan(product, store, batches, tableId, table.Id, sources, keptUnreadable, generation, stale);
        }

        // The row times a merged hour can hold for a sensor: the union of the ranges of every class its record names
        // (dropped classes included), whatever class holds the sensor's rows now (#1850 part 4c). The sensor's rows were
        // merged into one class of the record, and a move copies them unchanged into another class, which the record may
        // name too: rows moved from P1 into P5 are within P1's range, not P5's own, so P5's own range would miss them. The
        // hour is read only when the union meets the range asked for. An hour selected through the gap between two classes
        // (rows of P1 only at 10:00 and of P5 only at 12:00, a read of the P1 sensor for 11:00) reports nothing: the store
        // skips every index record whose rows cannot meet the range before it fetches the batch (ReadBatches), so a record
        // whose batch is missing is not counted as failed for a range that can hold none of its rows
        // (TableChartReadTests.An_hour_selected_through_the_gap_between_two_classes_returns_no_row_and_reports_no_failed_batch).
        // It is not free: such an hour is a source whose MinTime is the read's from (so the early stop never stops before
        // it), and costs the gate, two readings of the generation (the chart), the acquire of its week database (an open
        // when it is not cached), an iterator and two seeks, all answering nothing. One sensor with wide row times in any
        // class makes every other class's reads select its hours (feature doc, Known Issues). Part 4b answered a named
        // class's own range instead (the union already for a class the record does not name); the move needs the union.
        private static (long Min, long Max) RowTimes(TableBatchHour hour)
        {
            long min = long.MaxValue, max = long.MinValue;

            foreach (var range in hour.Classes)
                if (range.Batches > 0)
                {
                    min = Math.Min(min, range.MinTime);
                    max = Math.Max(max, range.MaxTime);
                }

            return (min, max);
        }

        private void ReadSource(ReadPlan plan, ReadSourceItem source, ITableRowSink sink, long fromTicks, long toTicks, CancellationToken token)
        {
            if (source.Blocks is not null)
            {
                foreach (var entry in source.Blocks)
                {
                    token.ThrowIfCancellationRequested();
                    BeforeBlockRead?.Invoke(entry);

                    sink.AddBlock(plan.Store, entry, plan.TableId);
                }
            }
            else
            {
                token.ThrowIfCancellationRequested();
                ReadBatches(plan.Batches, sink, plan.Product, plan.SensorId, source.Period, source.Hour, fromTicks, toTicks);
            }
        }

        private static void ReadBatches(ITableBatchStore batches, ITableRowSink collector, Guid product, Guid sensorId, int period, long hour, long fromTicks, long toTicks)
        {
            // Only the records whose rows can meet the range: one out of it is skipped before its batch is fetched, so a
            // missing batch there is no failure (#1850 part 4c).
            var reads = batches.ReadSensor(product, sensorId, period, [hour], fromTicks, toTicks, out var failed);

            // Index records in the range without their batch, or that do not decode: their rows are missing (#1850 part 4b).
            for (int i = 0; i < failed; i++)
                collector.BatchFailed();

            foreach (var read in reads)
            {
                // Defensive against another ITableBatchStore: the LevelDB store already skips a record out of the range.
                if (read.LastTime < fromTicks || read.FirstTime >= toTicks)
                    continue;

                TableBatch batch;
                try
                {
                    batch = TableBatchCodec.Decode(read.Batch);
                }
                catch (TableBatchFormatException ex)
                {
                    collector.BatchFailed();
                    batches.Statistics.Add(ref batches.Statistics.ReadFailures);
                    _logger.Error($"A table batch of sensor {sensorId}, hour {new DateTime(hour, DateTimeKind.Utc):O}, ordinal {read.Ordinal} cannot be read ({ex.Error}): {ex.Message}");
                    continue;
                }

                if (!collector.AddBatch(batch, hour, read.Ordinal, read.Moved))
                {
                    batches.Statistics.Add(ref batches.Statistics.ReadFailures);
                    _logger.Error($"A table batch of sensor {sensorId}, hour {new DateTime(hour, DateTimeKind.Utc):O}, ordinal {read.Ordinal} does not have its sensor's columns");
                }
            }
        }


        // The refused tables of a block, in directory order: every one (for the envelope), and the first
        // MaxDetailedRefusals with their identifiers (for the result). Outside any lock of the store: the put holds a
        // reserved ticket, which only makes later puts wait for their turn to write. `accepted` is the sensor and the rows
        // of every table taken, for the feed counters (#1847).
        private List<TableRefusal> Match(Guid productId, TableBlockDirectory directory, long sequence, out List<TableRefusalInfo> detailed,
            out List<(Guid SensorId, int Rows)> accepted)
        {
            var refusals = new List<TableRefusal>();
            detailed = new List<TableRefusalInfo>();
            accepted = new List<(Guid, int)>(directory.Entries.Count);

            for (int i = 0; i < directory.Entries.Count; i++)
            {
                var entry = directory.Entries[i];
                var reason = Refuse(productId, entry, sequence, out var path, out var table);

                if (reason is not { } refused)
                {
                    accepted.Add((table.Id, entry.RowCount));
                    continue;
                }

                refusals.Add(new TableRefusal(i, refused));

                if (detailed.Count < TableBlockPutResult.MaxDetailedRefusals)
                    detailed.Add(new TableRefusalInfo(i, path ?? Printable(entry.TableId), refused));

                if (_logger.IsDebugEnabled)
                    _logger.Debug($"Table '{path ?? Printable(entry.TableId)}' of block sequence {sequence} refused: {refused}");
            }

            return refusals;
        }

        private TableRefusalReason? Refuse(Guid productId, TableDirectoryEntry entry, long sequence, out string path, out TableSensorModel table)
        {
            table = null;

            try
            {
                path = StrictUtf8.GetString(entry.TableId);
            }
            catch (DecoderFallbackException)
            {
                path = null;
                return TableRefusalReason.InvalidIdentifier;
            }

            if (!_cache.TryGetSensorByPath(productId, path, out var sensor))
                return TableRefusalReason.UnknownSensor;

            if (sensor is not TableSensorModel found)
                return TableRefusalReason.NotATableSensor;

            table = found;

            if (table.State is SensorState.Blocked)
                return TableRefusalReason.SensorBlocked;

            if (table.FirstSequence > sequence)
                return TableRefusalReason.SensorCreatedAfterBlock;

            if (entry.SchemaId != table.Schema.Id)
                return TableRefusalReason.SchemaIdMismatch;

            if (!table.Schema.HasColumnTypes(entry.ColumnTypes))
                return TableRefusalReason.ColumnTypesMismatch;

            return null;
        }

        // Every refused table is counted; the log gets a Warn line with the reasons at most once a minute (Debug per table).
        private void CountRefusals(List<TableRefusal> refusals, List<TableRefusalInfo> detailed)
        {
            if (refusals.Count == 0)
                return;

            Store.Statistics.Add(ref Store.Statistics.TablesRefused, refusals.Count);

            lock (_refusalLogLock)
            {
                foreach (var refusal in refusals)
                    _refusalsSinceLog[refusal.Reason] = _refusalsSinceLog.GetValueOrDefault(refusal.Reason) + 1;

                var now = DateTime.UtcNow;
                if (now - _lastRefusalLog < RefusalLogInterval)
                    return;

                _lastRefusalLog = now;
                _logger.Warn($"Table blocks: tables refused since the last report: {string.Join(", ", _refusalsSinceLog.Select(r => $"{r.Key} {r.Value}"))}; first of the last block: '{detailed[0].TableId}' {detailed[0].Reason}");
                _refusalsSinceLog.Clear();
            }
        }

        // The sequence floor from the sensors, raised once, by the first put, before its reservation. If the block store was
        // deleted (or restored from an older copy), its sequence would start below the first sequences of the registered
        // table sensors and all their tables would be refused; raising it to the largest one keeps them taking rows.
        // - The tree is loaded by then: the cache loads it in its constructor, and this service is built from the cache.
        // - Every put goes through this service, so no block is numbered before the floor is in place. A read needs none.
        // - A sensor registered before the first put reads the store's next sequence directly and may get a small first
        //   sequence: harmless, the floor only has to be at or above every first sequence.
        // - If the raise throws (the store cannot open), the flag stays unset: the put fails as the fault it is, and the
        //   next put tries again. Without table sensors nothing is raised and nothing is opened here.
        private void EnsureSequenceFloor(ITableBlockStore store)
        {
            if (_floorRaised)
                return;

            lock (_floorLock)
            {
                if (_floorRaised)
                    return;

                var floor = -1L;

                foreach (var sensor in _cache.GetSensors())
                    if (sensor is TableSensorModel table && table.FirstSequence > floor)
                        floor = table.FirstSequence;

                if (floor >= 0)
                    store.RaiseNextSequence(floor);

                _floorRaised = true;
            }
        }

        // The products whose merge state is in the block store since the start; written under _mergeStateLock.
        private readonly object _mergeStateLock = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, bool> _mergeStateLoaded = new();

        // The merge's state of one product after a start (#1850), loaded once per product, before its first put and before
        // the merge of its first hour:
        // - its producers' watermarks from the batch store (durable there, in memory only in the block store): a block sent
        //   again after its hour was merged and deleted is a duplicate;
        // - its newest merged hour closed in the block store (CloseHour is in memory only): with the clock behind, a block of
        //   the product must never be appended to an hour already committed for it, whose blocks a read ignores. (A block of
        //   another product in an hour committed for this one is that product's hour, merged for it on its own.)
        // Per product: a product whose batches cannot be opened (a held LOCK, permissions) faults its own puts and merges
        // only, and is tried again on its next use. If it throws, the product stays unloaded: the put fails as the fault it
        // is (a TableBlockStoreException), and the next one tries again.
        internal void EnsureMergeState(Guid productId)
        {
            if (_mergeStateLoaded.ContainsKey(productId))
                return;

            lock (_mergeStateLock)
            {
                if (_mergeStateLoaded.ContainsKey(productId))
                    return;

                if (_database.TableBatches is { } batches)
                {
                    try
                    {
                        Store.RaiseWatermarks(batches.GetWatermarks(productId).Select(w => new TableBlockWatermark(productId, w.Key, w.Value)).ToList());

                        if (batches.GetNewestHour(productId) is { } newest)
                            Store.CloseHour(newest);
                    }
                    catch (TableBatchStoreException ex)
                    {
                        throw new TableBlockStoreException($"The merged hours of product {productId} could not be loaded: {ex.Message}", ex);
                    }
                }

                _mergeStateLoaded[productId] = true;
            }
        }

        // Unspecified is UTC here: ToUniversalTime would take it as local time and shift the range by the server's offset.
        private static DateTime Utc(DateTime time) => time.Kind switch
        {
            DateTimeKind.Local => time.ToUniversalTime(),
            _ => DateTime.SpecifyKind(time, DateTimeKind.Utc),
        };

        private static string TableIdText(TableBlockDirectory directory, int index) =>
            index >= 0 && index < directory.Entries.Count ? Printable(directory.Entries[index].TableId) : $"#{index}";

        private static string Printable(byte[] id) => Encoding.UTF8.GetString(id);
    }
}
