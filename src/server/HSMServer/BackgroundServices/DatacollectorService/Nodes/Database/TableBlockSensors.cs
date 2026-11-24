using System;
using System.Collections.Generic;
using System.Threading;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
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
        private readonly List<Counter> _counters = [];
        private bool _created;


        public TableBlockSensors(IDataCollector collector, ITableBlockStore store)
        {
            _collector = collector;
            _store = store;
        }


        internal bool IsCreated => _created;


        internal void SendInfo()
        {
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

        private void Add(string name, Func<long> read, bool errorWhenGrown, string description)
        {
            var sensor = _collector.CreateIntSensor($"{NodeName}/{name}", Options(description, Unit.Count));
            _counters.Add(new Counter(read, errorWhenGrown, (value, error) => sensor.AddValue((int)Math.Min(value, int.MaxValue), error ? SensorStatus.Error : SensorStatus.Ok)));
        }

        private void AddMb(string name, Func<long> read, string description)
        {
            var sensor = _collector.CreateDoubleSensor($"{NodeName}/{name}", Options(description, Unit.MB));
            _counters.Add(new Counter(read, false, (value, _) => sensor.AddValue(DatabaseSensorsBase.GetRoundedDouble(value))));
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
        // sample and goes back to Ok at the next sample without growth.
        private sealed class Counter(Func<long> read, bool errorWhenGrown, Action<long, bool> post)
        {
            private long? _posted;
            private bool _postedError;

            public void Post()
            {
                var value = read();

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
