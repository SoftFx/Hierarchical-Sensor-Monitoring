using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HSMCommon.TaskResult;
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
        /// </summary>
        TaskResult<TableRows> Read(Guid sensorId, DateTime from, DateTime to, int? maxRows = null);
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
    public sealed class TableBlockService : ITableBlockService
    {
        private static readonly Logger _logger = LogManager.GetLogger("TableBlocks");
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);
        private static readonly TimeSpan RefusalLogInterval = TimeSpan.FromMinutes(1);

        private readonly IDatabaseCore _database;
        private readonly ITreeValuesCache _cache;
        private readonly TableBlockOptions _options;

        private readonly TableHourGate _gate = new();

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
            Merge = new TableMergeJob(_database, _cache, _options, _gate, EnsureMergeState);
        }


        /// <summary>The hourly merge of blocks into batches (#1850), run by the server's background service.</summary>
        public TableMergeJob Merge { get; }


        private ITableBlockStore Store => _database.TableBlocks;

        // Set once the sequence floor from the sensors is in the store; guarded by _floorLock.
        private readonly object _floorLock = new();
        private volatile bool _floorRaised;

        // Called with the reserved sequence before the tables are matched. Test seam for a sensor created during a put.
        internal Action<long> AfterReserve { get; set; }


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

                var refusals = Match(productId, directory, ticket.Sequence);
                var envelope = new TableBlockEnvelope
                {
                    ProductId = productId,
                    ProducerId = producerId,
                    ProducerSequence = producerSequence,
                    Refusals = refusals.Select(r => new TableRefusal(r.Index, r.Reason)).ToArray(),
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
                            Refusals = (appended.Envelope?.Refusals ?? []).Select(r => new TableRefusalInfo(r.Index, TableIdText(directory, r.Index), r.Reason)).ToList(),
                            Message = appended.Envelope is null ? "The stored block's envelope could not be read; its refusals are not known." : null,
                            RefusalsKnown = appended.Envelope is not null,
                        };

                    case TableBlockAppendOutcome.NotStored:
                        CountRefusals(refusals);
                        return new TableBlockPutResult
                        {
                            Outcome = TableBlockPutOutcome.AllRefused,
                            TableCount = directory.Entries.Count,
                            Refusals = refusals,
                            Message = "Every table of the block was refused; nothing was stored.",
                        };

                    default:
                        CountRefusals(refusals);
                        return new TableBlockPutResult
                        {
                            Outcome = TableBlockPutOutcome.Stored,
                            Sequence = appended.Entry.Sequence,
                            TableCount = directory.Entries.Count,
                            Refusals = refusals,
                        };
                }
            }
            finally
            {
                if (!ticket.IsDone)
                    store.Skip(ticket);
            }
        }

        public TaskResult<TableRows> Read(Guid sensorId, DateTime from, DateTime to, int? maxRows = null)
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
                    return TaskResult<TableRows>.FromValue(ReadRows(table, fromTicks, toTicks, limit));
            }
            catch (TableBatchStoreException ex)
            {
                // One kind of fault for the caller: the store cannot be used, answered 5xx.
                throw new TableBlockStoreException(ex.Message, ex);
            }
        }

        // Under the read side of the gate, so no hour's blocks are deleted meanwhile.
        //
        // The read rule (#1850): an arrival hour is read from its batches when it is committed for the product, otherwise from
        // its blocks; never both, never neither. The blocks are listed first; whether each hour that has candidate blocks is
        // committed is asked next; the committed hours with their row ranges are listed last. An hour found not committed is
        // read from its blocks even when the list taken after it says committed (a commit in between): its blocks stay until
        // the delay after the commit has passed, and the gate holds their deletion off. An hour found committed is read from
        // its batches, which the later list has (commits are never taken back, except by retention of a whole week).
        //
        // Sources (the blocks of one arrival hour, or one merged hour) are visited in the order of their earliest row in the
        // range, and the read stops once it holds the limit, has dropped a row past it, and no source left can start before
        // the latest row it keeps.
        private TableRows ReadRows(TableSensorModel table, long fromTicks, long toTicks, int limit)
        {
            var product = table.Root.Id;
            var store = Store;
            var batches = _database.TableBatches;
            var tableId = Encoding.UTF8.GetBytes(table.Path);

            var blocks = store.GetBlocks(product);
            var committed = new Dictionary<long, bool>();
            var blockHours = new SortedDictionary<long, List<TableBlockIndexEntry>>();

            for (int b = 0; b < blocks.Count; b++)
            {
                var entry = blocks[b];
                if (entry.Sequence < table.FirstSequence || entry.FirstTime >= toTicks || entry.LastTime < fromTicks)
                    continue;

                var hour = entry.ArrivalHourTicks;
                if (!committed.TryGetValue(hour, out var isCommitted))
                    committed[hour] = isCommitted = batches?.IsHourMerged(product, hour) ?? false;

                if (isCommitted)
                    continue;

                if (!blockHours.TryGetValue(hour, out var list))
                    blockHours[hour] = list = [];
                list.Add(entry);
            }

            var sources = new List<(long MinTime, long Hour, List<TableBlockIndexEntry> Blocks)>();

            foreach (var (hour, list) in blockHours)
            {
                long min = long.MaxValue;
                foreach (var entry in list)
                    min = Math.Min(min, entry.FirstTime);

                sources.Add((Math.Max(min, fromTicks), hour, list));
            }

            int period = 0;
            if (batches is not null)
            {
                period = TablePeriodClass.Of(table, DateTime.UtcNow);

                foreach (var hour in batches.GetHours(product))
                {
                    // Taken from its blocks above: it was not committed when that was decided.
                    if (committed.TryGetValue(hour.HourTicks, out var isCommitted) && !isCommitted)
                        continue;

                    // Every block of the hour is below the sensor's first sequence: none of its rows are the sensor's.
                    if (hour.LastBlockSequence < table.FirstSequence)
                        continue;

                    foreach (var range in hour.Classes)
                        if (range.PeriodWeeks == period && range.Batches > 0 && range.MinTime < toTicks && range.MaxTime >= fromTicks)
                            sources.Add((Math.Max(range.MinTime, fromTicks), hour.HourTicks, null));
                }
            }

            sources.Sort((a, b) => a.MinTime != b.MinTime ? a.MinTime.CompareTo(b.MinTime) : a.Hour.CompareTo(b.Hour));

            var collector = new TableRowCollector(table.Schema, fromTicks, toTicks, limit);

            for (int i = 0; i < sources.Count; i++)
            {
                var source = sources[i];

                if (i > 0 && collector.CanStopBefore(source.MinTime))
                    break;

                if (source.Blocks is not null)
                {
                    foreach (var entry in source.Blocks)
                        collector.AddBlock(store, entry, tableId);
                }
                else
                    ReadBatches(batches, collector, product, table.Id, period, source.Hour, fromTicks, toTicks);
            }

            return collector.Build();
        }

        private static void ReadBatches(ITableBatchStore batches, TableRowCollector collector, Guid product, Guid sensorId, int period, long hour, long fromTicks, long toTicks)
        {
            foreach (var read in batches.ReadSensor(product, sensorId, period, [hour]))
            {
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


        // The refused tables of a block, in directory order. Outside any lock of the store: the put holds a reserved
        // ticket, which only makes later puts wait for their turn to write.
        private List<TableRefusalInfo> Match(Guid productId, TableBlockDirectory directory, long sequence)
        {
            var refusals = new List<TableRefusalInfo>();

            for (int i = 0; i < directory.Entries.Count; i++)
            {
                var entry = directory.Entries[i];
                var reason = Refuse(productId, entry, sequence, out var path);

                if (reason is { } refused)
                {
                    refusals.Add(new TableRefusalInfo(i, path ?? Printable(entry.TableId), refused));
                    _logger.Debug($"Table '{path ?? Printable(entry.TableId)}' of block sequence {sequence} refused: {refused}");
                }
            }

            return refusals;
        }

        private TableRefusalReason? Refuse(Guid productId, TableDirectoryEntry entry, long sequence, out string path)
        {
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

            if (sensor is not TableSensorModel table)
                return TableRefusalReason.NotATableSensor;

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
        private void CountRefusals(List<TableRefusalInfo> refusals)
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
                _logger.Warn($"Table blocks: tables refused since the last report: {string.Join(", ", _refusalsSinceLog.Select(r => $"{r.Key} {r.Value}"))}; last: '{refusals[^1].TableId}' {refusals[^1].Reason}");
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
