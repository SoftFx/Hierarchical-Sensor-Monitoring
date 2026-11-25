using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Extensions;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.TableSensors;
using HSMServer.ServerConfiguration;

namespace HSMServer.BackgroundServices
{
    /// <summary>
    /// Posts the feed sensors of table sensors (#1847): ordinary sensors in the table sensor's own product, under
    /// <c>.tables</c>, that users set their alerts on (a table sensor owns no alerts and no TTL). Every tick drains the
    /// period of <see cref="TableFeedCounters"/> (the product counters, and the table counters when a table period ended)
    /// and hands each product's values to that product's lane; lanes post on their own, so a slow product queue delays
    /// only its own product, and a tick never waits for the posts of the one before. Counts are per period, not since a
    /// start. A product or table without activity in a period posts nothing; a failure counter posts 0 once after a period
    /// in which it was above 0, and keeps that 0 pending until a write of it was accepted. A tick never throws into the
    /// host: a failure is logged and the next tick goes on.
    /// <para>Documented in <c>aicontext/features/server/table-feed-sensors/feature.md</c>.</para>
    /// </summary>
    public sealed class TableFeedSensorsService : BaseDelayedBackgroundService
    {
        internal const string Node = ".tables";
        internal const string RowsName = "Rows";
        internal const string StoredName = "Stored";
        internal const string LagName = "Delivery lag";
        internal const string RefusedName = "Tables refused";
        internal const string UnreadableName = "Blocks the merge could not read";

        internal const string Initiator = "Table feed sensors";

        /// <summary>
        /// The most periods a product's lane holds while its queue does not answer (an hour of the default product period);
        /// past it the oldest period is dropped, with an Error once per stall.
        /// </summary>
        internal const int MaxPendingPeriods = 60;

        private const int MaxLoggedPaths = 10_000;

        private readonly TableFeedCounters _counters;
        private readonly ITreeValuesCache _cache;
        private readonly IServerSensorValues _values;
        private readonly TimeSpan _productPeriod;
        private readonly TimeSpan _tablePeriod;
        private readonly bool _tableSensors;

        // The 0-once state of the failure counters, per product and counter; guarded by _stateLock.
        private readonly object _stateLock = new();
        private readonly Dictionary<(Guid Product, string Path), FailureState> _failures = [];

        private readonly object _lanesLock = new();
        private readonly Dictionary<Guid, Lane> _lanes = [];

        // Paths a failure or a clamp was logged for: once per product and path; guarded by themselves.
        private readonly HashSet<string> _warned = [];
        private readonly HashSet<string> _clamped = [];

        private DateTime _lastTableSlot = DateTime.MinValue;


        public override TimeSpan Delay => _productPeriod;


        public TableFeedSensorsService(TableBlockService tables, ITreeValuesCache cache, IServerSensorValues values, IServerConfig config)
            : this(tables.Feed, cache, values, config.TableSensors) { }

        internal TableFeedSensorsService(TableFeedCounters counters, ITreeValuesCache cache, IServerSensorValues values, TableSensorsConfig config)
        {
            _counters = counters;
            _cache = cache;
            _values = values;
            _productPeriod = TimeSpan.FromMinutes(config.FeedProductPeriodMinutes);
            _tablePeriod = TimeSpan.FromMinutes(config.FeedTablePeriodMinutes);
            _tableSensors = config.FeedTableSensors;
        }


        protected override Task ServiceActionAsync(CancellationToken token = default)
        {
            // Not awaited: the period is drained now, its posts go on in their lanes, and the next tick is not held up.
            try
            {
                _ = TickAsync(DateTime.UtcNow, token);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Table feed sensors: a tick failed; the counts drained in it are not posted");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Drains one period (the product counters, and the table counters when <paramref name="utcNow"/> is in a table
        /// period after the last one drained; the first tick drains both) and hands the values to the products' lanes. The
        /// values carry <paramref name="utcNow"/> as their time. The task completes when the lanes of this tick's products
        /// have posted everything they hold; the drain itself is done when the method returns.
        /// </summary>
        internal Task TickAsync(DateTime utcNow, CancellationToken token = default)
        {
            var tableSlot = utcNow.Floor(_tablePeriod);
            var drainTables = tableSlot > _lastTableSlot;

            if (drainTables)
                _lastTableSlot = tableSlot;

            var (products, tables) = _counters.Drain(drainTables);
            var posts = new Dictionary<Guid, List<FeedValue>>();

            List<Guid> pending;
            lock (_stateLock)
                pending = _failures.Keys.Select(k => k.Product).ToList();

            foreach (var productId in products.Keys.Union(pending).ToList())
                AddProductValues(posts, productId, products.GetValueOrDefault(productId), utcNow);

            if (tables is not null && _tableSensors)
                foreach (var (sensorId, rows) in tables)
                    AddTableValue(posts, sensorId, rows, utcNow);

            var lanes = new List<Task>(posts.Count);

            foreach (var (productId, values) in posts)
                lanes.Add(Enqueue(productId, values, utcNow, token));

            return Task.WhenAll(lanes);
        }

        /// <summary>The posts of a product's lane: done when it holds nothing. For tests.</summary>
        internal Task WhenPosted(Guid productId)
        {
            lock (_lanesLock)
                return _lanes.TryGetValue(productId, out var lane) ? lane.Running : Task.CompletedTask;
        }

        private void AddProductValues(Dictionary<Guid, List<FeedValue>> posts, Guid productId, TableFeedProductPeriod period, DateTime time)
        {
            // A removed product: its sensors went with it.
            if (!_cache.TryGetRootProduct(productId, out _, out _))
            {
                lock (_stateLock)
                    foreach (var key in _failures.Keys.Where(k => k.Product == productId).ToList())
                        _failures.Remove(key);

                return;
            }

            var product = $"{Node}/";
            var minutes = Minutes(_productPeriod);

            if (period is { Blocks: > 0 })
            {
                Add(posts, productId, Int(productId, product + RowsName, period.Rows, time),
                    $"Rows of the table sensors of this product the server took in the last {minutes} (rows of refused tables are not counted). " +
                    "No value in a period means no block arrived: a TTL alert on this sensor watches the feed.", Unit.Count);
                Add(posts, productId, Double(product + StoredName, period.Bytes / 1024.0 / 1024.0, time),
                    $"Size of the table blocks of this product stored in the last {minutes}, as stored (MB).", Unit.MB);
                Add(posts, productId, Double(product + LagName, period.MaxLagTicks / (double)TimeSpan.TicksPerSecond, time),
                    $"Delivery lag of the table blocks of this product, the largest in the last {minutes} (seconds): the time the server received " +
                    "a block minus the time of its last row. Measured against the clock of the provider that wrote the rows, so a clock skew is in it; " +
                    "negative when that clock is ahead of the server's. Posted only for a period in which a block was stored.", Unit.Seconds);
            }

            AddFailure(posts, productId, product + RefusedName, period?.RefusedTables ?? 0, time,
                $"Tables of the blocks of this product the server refused in the last {minutes}: unknown or blocked table sensor, " +
                "another kind of sensor, another schema, an invalid identifier. Their rows are never served; the server log has the reasons. " +
                "Posts 0 once after a period with refusals, then nothing until the next refusal.");
            AddFailure(posts, productId, product + UnreadableName, period?.UnreadableBlocks ?? 0, time,
                $"Table blocks of this product the hourly merge could not read (missing or damaged) in the hours it committed in the last {minutes}. " +
                "They are kept in the block store; their rows are not served. Posts 0 once after a period with such blocks, then nothing until the next.");
        }

        // A count above 0 is posted and leaves a 0 pending; a period without one posts the pending 0, which stays pending
        // until a write of it is accepted (and no newer count came meanwhile). One 0 in flight at a time.
        private void AddFailure(Dictionary<Guid, List<FeedValue>> posts, Guid productId, string path, long count, DateTime time, string description)
        {
            ZeroTag tag = null;

            lock (_stateLock)
            {
                var key = (productId, path);
                _failures.TryGetValue(key, out var state);

                if (count > 0)
                {
                    state ??= _failures[key] = new FailureState();
                    state.Version++;
                }
                else if (state is null || state.ZeroInFlight)
                    return;
                else
                {
                    state.ZeroInFlight = true;
                    tag = new ZeroTag(key, state.Version);
                }
            }

            if (!Add(posts, productId, Int(productId, path, count, time), description, Unit.Count, tag) && tag is not null)
                Settle(tag, accepted: false);
        }

        private void Settle(ZeroTag tag, bool accepted)
        {
            lock (_stateLock)
            {
                if (!_failures.TryGetValue(tag.Key, out var state))
                    return;

                state.ZeroInFlight = false;

                if (accepted && state.Version == tag.Version)
                    _failures.Remove(tag.Key);
            }
        }

        private void AddTableValue(Dictionary<Guid, List<FeedValue>> posts, Guid sensorId, long rows, DateTime time)
        {
            // A removed table sensor: its Rows sensor goes silent.
            if (_cache.GetSensor(sensorId) is not TableSensorModel { Root: { } root } table)
                return;

            Add(posts, root.Id, Int(root.Id, $"{Node}/{table.Path}/{RowsName}", rows, time),
                $"Rows of the table sensor '{table.Path}' the server took in the last {Minutes(_tablePeriod)}. " +
                "No value in a period means no rows arrived for it: a TTL alert on this sensor watches the table.", Unit.Count);
        }

        private bool Add(Dictionary<Guid, List<FeedValue>> posts, Guid productId, SensorValueBase value, string description, Unit unit, ZeroTag tag = null)
        {
            // The path as the server stores it, and the checks of any value's path (a table path deep enough leaves no
            // room for .tables and the sensor's name).
            var normalized = new AddSensorValueRequest(Guid.Empty, productId, value.Path, null);

            if (!normalized.TryCheckRequest(out var error))
            {
                Warn(productId, value.Path, $"not posted: {error}");
                return false;
            }

            value.Path = normalized.Path;

            if (!posts.TryGetValue(productId, out var list))
                posts[productId] = list = [];

            list.Add(new FeedValue(new ServerSensorValue(value, description, unit, Initiator), tag));
            return true;
        }

        // === Lanes: one per product with values to post, alive while it holds any ===

        private Task Enqueue(Guid productId, List<FeedValue> values, DateTime time, CancellationToken token)
        {
            List<FeedValue> dropped = null;
            Lane lane;

            lock (_lanesLock)
            {
                if (!_lanes.TryGetValue(productId, out lane))
                    _lanes[productId] = lane = new Lane();

                lane.Pending.Enqueue(new Period(time, values));

                // A lane that is not running starts with its first period taken out: the bound below counts only the
                // periods waiting behind the one being posted.
                if (!lane.Active)
                {
                    var first = lane.Pending.Dequeue();
                    lane.Active = true;
                    lane.Running = Task.Run(() => RunLaneAsync(productId, lane, first, token));
                }

                if (lane.Pending.Count > MaxPendingPeriods)
                {
                    var oldest = lane.Pending.Dequeue();
                    dropped = oldest.Values;

                    if (!lane.OverflowLogged)
                    {
                        lane.OverflowLogged = true;
                        _logger.Error($"Table feed sensors: the queue of product {productId} has not taken the values of {MaxPendingPeriods} periods; " +
                                      $"the oldest are dropped, first the period ending {oldest.Time:O} (logged once until the queue answers again)");
                    }
                }
            }

            if (dropped is not null)
                foreach (var item in dropped)
                    if (item.Tag is not null)
                        Settle(item.Tag, accepted: false);

            return lane.Running;
        }

        private async Task RunLaneAsync(Guid productId, Lane lane, Period period, CancellationToken token)
        {
            while (true)
            {
                try
                {
                    await PostAsync(productId, period, token);

                    lock (_lanesLock)
                        lane.OverflowLogged = false;
                }
                catch (Exception ex)
                {
                    foreach (var item in period.Values)
                        if (item.Tag is not null)
                            Settle(item.Tag, accepted: false);

                    if (!token.IsCancellationRequested)
                        _logger.Error(ex, $"Table feed sensors: the values of product {productId} for the period ending {period.Time:O} were not posted");
                }

                lock (_lanesLock)
                {
                    if (lane.Pending.Count == 0 || token.IsCancellationRequested)
                    {
                        // Periods left at a stop are not posted (the server stops).
                        lane.Active = false;
                        lane.Pending.Clear();
                        _lanes.Remove(productId);
                        return;
                    }

                    period = lane.Pending.Dequeue();
                }
            }
        }

        private async Task PostAsync(Guid productId, Period period, CancellationToken token)
        {
            var answer = await _values.AddServerValuesAsync(productId, period.Values.Select(v => v.Value).ToList(), token);
            var productError = answer.GetValueOrDefault(productId.ToString());

            foreach (var item in period.Values)
            {
                var path = item.Value.Value.Path;
                var error = answer.GetValueOrDefault(path) ?? productError;

                if (error is not null)
                    Warn(productId, path, $"not posted: {error}");

                if (item.Tag is not null)
                    Settle(item.Tag, accepted: error is null);
            }
        }

        // === Values and logs ===

        private void Warn(Guid productId, string path, string message)
        {
            if (Once(_warned, $"{productId}/{path}"))
                _logger.Warn($"Table feed sensor '{path}' of product {productId} {message} (logged once per sensor)");
        }

        private IntSensorValue Int(Guid productId, string path, long value, DateTime time)
        {
            // The sensor stays Integer: a count beyond it is posted as int.MaxValue, and said once (rule 8).
            if (value > int.MaxValue && Once(_clamped, $"{productId}/{path}"))
                _logger.Error($"Table feed sensor '{path}' of product {productId}: the count {value} of the period ending {time:O} is above the Integer range " +
                              $"and is posted as {int.MaxValue} (logged once per sensor)");

            return new() { Path = path, Value = (int)Math.Min(value, int.MaxValue), Time = time };
        }

        private static DoubleSensorValue Double(string path, double value, DateTime time) =>
            new() { Path = path, Value = Math.Round(value, 3), Time = time };

        private static bool Once(HashSet<string> logged, string key)
        {
            lock (logged)
            {
                if (logged.Count >= MaxLoggedPaths)
                    logged.Clear();

                return logged.Add(key);
            }
        }

        private static string Minutes(TimeSpan period) =>
            period.TotalMinutes == 1 ? "minute" : $"{period.TotalMinutes.ToString(CultureInfo.InvariantCulture)} minutes";


        private sealed record FeedValue(ServerSensorValue Value, ZeroTag Tag);

        private sealed record ZeroTag((Guid Product, string Path) Key, long Version);

        private sealed record Period(DateTime Time, List<FeedValue> Values);

        private sealed class FailureState
        {
            public long Version;
            public bool ZeroInFlight;
        }

        private sealed class Lane
        {
            public readonly Queue<Period> Pending = new();
            public Task Running = Task.CompletedTask;
            public bool Active;
            public bool OverflowLogged;
        }
    }
}
