using System;
using System.Collections.Generic;
using System.Threading;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMDatabase.AccessManager.TableBatches;
using HSMDatabase.AccessManager.TableBlocks;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;

namespace HSMServer.BackgroundServices
{
    /// <summary>
    /// Self-monitoring of the block store of table sensors (#1844), under <c>Database/Tables</c>, posted from the 5-minute
    /// self-monitoring sample. Inert until the store has been opened in this process: a server that never sees a table
    /// sensor creates none of these sensors and posts nothing. The values are counters since the process started, posted
    /// when they change. A failure counter is Error in the sample in which it grew and Ok again at the next sample without
    /// a new failure (posted once more with the same value), so one transient failure does not stay Error until a restart.
    /// </summary>
    public sealed class TableBlockSensors
    {
        internal const string NodeName = "Database/Tables";

        private readonly IDataCollector _collector;
        private readonly ITableBlockStore _store;
        private readonly ITableBatchStore _batches;
        private readonly List<Counter> _counters = [];
        private readonly List<Counter> _batchCounters = [];
        private bool _created, _batchesCreated;


        public TableBlockSensors(IDataCollector collector, ITableBlockStore store, ITableBatchStore batches = null)
        {
            _collector = collector;
            _store = store;
            _batches = batches;
        }


        internal bool IsCreated => _created;


        internal void SendInfo()
        {
            // The batch store (#1850), once a product's batches were opened in this process.
            if (_batches is not null && _batches.IsOpened)
            {
                if (!_batchesCreated)
                    CreateBatches();

                foreach (var counter in _batchCounters)
                    counter.Post();
            }

            if (_store is null || !_store.IsOpened)
                return;

            if (!_created)
                Create();

            foreach (var counter in _counters)
                counter.Post();
        }

        private void Create()
        {
            var stats = _store.Statistics;

            Add("Blocks stored", () => Interlocked.Read(ref stats.BlocksStored), false,
                "Blocks of table sensors stored since the server started.");
            Add("Rows stored", () => Interlocked.Read(ref stats.RowsStored), false,
                "Rows in the blocks stored since the server started (all tables of a block, refused ones included).");
            AddMb("Stored", () => Interlocked.Read(ref stats.BytesStored),
                "Bytes written for the blocks stored since the server started, envelopes included (MB).");
            Add("Blocks in store", () => _store.BlockCount, false,
                "Blocks in the store now.");
            Add("Duplicate blocks", () => Interlocked.Read(ref stats.DuplicateBlocks), false,
                "Blocks sent again (same producer and producer sequence) and not stored twice since the server started.");
            Add("Duplicates by watermark", () => Interlocked.Read(ref stats.MergedDuplicateBlocks), false,
                "Of the duplicate blocks, those recognised by their producer's watermark because their hour was merged (#1850): their sequence and refusals are not known any more.");
            Add("Tables refused", () => Interlocked.Read(ref stats.TablesRefused), false,
                "Tables of blocks the server refused since the server started: unknown or blocked sensor, another kind, another schema, an invalid identifier. Their rows are never served; the log has the reasons.");
            Add("Blocks not stored", () => Interlocked.Read(ref stats.BlocksNotStored), false,
                "Blocks every table of which was refused, so nothing was stored, since the server started.");
            Add("Invalid blocks", () => Interlocked.Read(ref stats.InvalidBlocks), true,
                "Blocks refused since the server started because they break the block format or the configured limits. Error in the sample in which it grew.");
            Add("Write failures", () => Interlocked.Read(ref stats.WriteFailures), true,
                "Puts that failed since the server started (disk, LevelDB, too little free space): the blocks were not stored and not acknowledged, so their collectors keep them. A failed open of the store counts here too, whether a put or a read started it (the store cannot take puts until it opens). Error in the sample in which it grew, Ok again at the next sample without a new failure.");
            Add("Read failures", () => Interlocked.Read(ref stats.ReadFailures), true,
                "Stored blocks that could not be read since the server started (missing or damaged); their rows are not served. Error in the sample in which it grew.");
            Add("Index rebuilds", () => Interlocked.Read(ref stats.IndexRebuilds), false,
                "Times the block index was rebuilt from the blocks at an open since the server started (it was missing, damaged or lost a write).");

            _created = true;
        }

        private void CreateBatches()
        {
            var stats = _batches.Statistics;

            Add("Batch databases open", () => _batches.OpenDatabases, false,
                "Week databases of the batch store open now (the bound is TableSensors.MaxOpenBatchDatabases; the weeks being written and those a read holds can take it above).", _batchCounters);
            Add("Batch week databases", () => _batches.WeekDatabases, false,
                "Week databases of the batch store on disk, over the products opened since the server started.", _batchCounters);
            Add("Merged hours", () => Interlocked.Read(ref stats.HoursCommitted), false,
                "Hours of products merged into batches since the server started.", _batchCounters);
            Add("Batch write failures", () => Interlocked.Read(ref stats.WriteFailures), true,
                "Writes and opens of the batch store that failed since the server started; a merged hour that failed is not committed and is merged again. Error in the sample in which it grew.", _batchCounters);
            Add("Batch read failures", () => Interlocked.Read(ref stats.ReadFailures), true,
                "Batch records and batches that could not be read since the server started; their rows are not served. Error in the sample in which it grew.", _batchCounters);

            // The merge job (#1850 part 3).
            var merge = _batches.MergeStatistics;

            Add("Arrival hours merged", () => Interlocked.Read(ref merge.HoursMerged), false,
                "Arrival hours of table blocks merged into batches for every product with blocks in them since the server started.", _batchCounters);
            Add("Merge lag", () => Interlocked.Read(ref merge.LagHours), false,
                "Closed arrival hours due for the merge and not merged at the end of the last merge pass (0 when the merge keeps up; hours whose batches did not verify are not counted here).", _batchCounters, Unit.Count);
            Add("Last merge duration", () => Interlocked.Read(ref merge.LastMergeMilliseconds), false,
                "How long the last merged arrival hour took, all its products (ms).", _batchCounters, Unit.Milliseconds);
            Add("Rows merged", () => Interlocked.Read(ref merge.RowsMerged), false,
                "Rows written into batches by the merge since the server started.", _batchCounters);
            AddMb("Merged blocks size", () => Interlocked.Read(ref merge.BlockBytesMerged),
                "Stored size of the blocks merged since the server started (MB): the bytes before the merge.", _batchCounters);
            AddMb("Merged batches size", () => Interlocked.Read(ref merge.BatchBytesWritten),
                "Size of the batches the merge wrote since the server started (MB): the bytes after the merge.", _batchCounters);
            Add("Verification mismatches", () => Interlocked.Read(ref merge.VerificationMismatches), false,
                "Batches that did not decode to their rows bit for bit since the server started: their product's hour was not written and is served from its blocks. Must stay 0; Error while above 0.", _batchCounters, errorWhenNonZero: true);
            Add("Unreadable blocks kept", () => Interlocked.Read(ref merge.UnreadableBlocksKept), true,
                "Blocks the merge could not read since the server started: kept in the block store and listed in their hour's record; their rows are not served once the hour is merged. Error in the sample in which it grew.", _batchCounters);
            Add("Rows of removed sensors dropped", () => Interlocked.Read(ref merge.RowsDropped), false,
                "Rows the merge dropped since the server started because no table sensor is on their path any more, or the sensor there was created after their block.", _batchCounters);
            Add("Rows expired before the merge", () => Interlocked.Read(ref merge.RowsExpired), false,
                "Rows the merge left out since the server started because their sensor's period had already passed for the week of their hour (a catch-up after a long downtime).", _batchCounters);
            Add("Merged hours holding blocks", () => Interlocked.Read(ref merge.MergedHoursHoldingBlocks), false,
                "Arrival hours merged for every product whose blocks are still in the block store, waiting for TableSensors.DeleteMergedBlocksAfterHours.", _batchCounters);

            _batchesCreated = true;
        }

        private void Add(string name, Func<long> read, bool errorWhenGrown, string description, List<Counter> counters = null, Unit unit = Unit.Count,
            bool errorWhenNonZero = false)
        {
            var sensor = _collector.CreateIntSensor($"{NodeName}/{name}", Options(description, unit));
            (counters ?? _counters).Add(new Counter(read, errorWhenGrown, (value, error) => sensor.AddValue((int)Math.Min(value, int.MaxValue), error ? SensorStatus.Error : SensorStatus.Ok), errorWhenNonZero));
        }

        private void AddMb(string name, Func<long> read, string description, List<Counter> counters = null)
        {
            var sensor = _collector.CreateDoubleSensor($"{NodeName}/{name}", Options(description, Unit.MB));
            (counters ?? _counters).Add(new Counter(read, false, (value, _) => sensor.AddValue(DatabaseSensorsBase.GetRoundedDouble(value))));
        }

        private static InstantSensorOptions Options(string description, Unit unit) => new()
        {
            Alerts = [],
            TTL = TimeSpan.MaxValue,
            EnableForGrafana = true,
            SensorUnit = unit,
            Description = description + " See the table block store feature documentation (#1844).",
        };

        // Posts a counter when it changes. A failure counter (errorWhenGrown) is Error when it grew since the previous
        // sample and goes back to Ok at the next sample without growth. One that must stay 0 (errorWhenNonZero) is Error
        // whenever it is above 0.
        private sealed class Counter(Func<long> read, bool errorWhenGrown, Action<long, bool> post, bool errorWhenNonZero = false)
        {
            private long? _posted;
            private bool _postedError;

            public void Post()
            {
                var value = read();

                if (errorWhenNonZero)
                {
                    if (value != _posted)
                    {
                        _posted = value;
                        post(value, value > 0);
                    }

                    return;
                }

                if (value == _posted)
                {
                    if (_postedError)
                    {
                        _postedError = false;
                        post(value, false);
                    }

                    return;
                }

                _postedError = errorWhenGrown && value > (_posted ?? 0);
                _posted = value;
                post(value, _postedError);
            }
        }
    }
}
