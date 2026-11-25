using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Extensions;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.TableSensors;
using HSMServer.ServerConfiguration;
using SensorType = HSMCommon.Model.SensorType;

namespace HSMServer.BackgroundServices
{
    /// <summary>
    /// Posts the feed sensors of table sensors (#1847): ordinary sensors in the table sensor's own product, under
    /// <c>.tables</c>, that users set their alerts on (a table sensor owns no alerts and no TTL). Drains
    /// <see cref="TableFeedCounters"/> every product period (the product's sensors) and every table period (the Rows sensor
    /// of each table sensor). Counts are per period, not since a start. A product or table without activity in a period
    /// posts nothing; a failure counter posts 0 once after a period in which it was above 0. A sensor is registered the
    /// first time a value finds none on its path. A tick never throws into the host: a failure is logged and the next tick
    /// goes on (its counts were drained and are lost with the failed tick, which the log says).
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

        private const string Initiator = "Table feed sensors";
        private const int MaxWarnedPaths = 10_000;

        private readonly TableFeedCounters _counters;
        private readonly ITreeValuesCache _cache;
        private readonly IServerSensorValues _values;
        private readonly TimeSpan _productPeriod;
        private readonly TimeSpan _tablePeriod;
        private readonly bool _tableSensors;

        // Products whose failure counter was above 0 in the last period: they post 0 once at the next period without one.
        private readonly HashSet<Guid> _refusedPending = [];
        private readonly HashSet<Guid> _unreadablePending = [];

        // Paths a failure was logged for, so a failure that repeats every period is logged once.
        private readonly HashSet<string> _warned = [];

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


        protected override async Task ServiceActionAsync(CancellationToken token = default)
        {
            try
            {
                await TickAsync(DateTime.UtcNow, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Table feed sensors: a tick failed; the counts drained in it are not posted");
            }
        }

        /// <summary>
        /// One period of the product sensors, and of the table sensors when <paramref name="utcNow"/> is in a table period
        /// after the last one drained (the first tick drains both). The values carry <paramref name="utcNow"/> as their time.
        /// </summary>
        internal async Task TickAsync(DateTime utcNow, CancellationToken token = default)
        {
            var products = _counters.DrainProducts();

            var tableSlot = utcNow.Floor(_tablePeriod);
            Dictionary<Guid, long> tables = null;

            if (tableSlot > _lastTableSlot)
            {
                _lastTableSlot = tableSlot;
                tables = _counters.DrainTables();
            }

            var posts = new Dictionary<Guid, List<FeedValue>>();

            foreach (var productId in products.Keys.Union(_refusedPending).Union(_unreadablePending).ToList())
                AddProductValues(posts, productId, products.GetValueOrDefault(productId), utcNow);

            if (tables is not null && _tableSensors)
                foreach (var (sensorId, rows) in tables)
                    AddTableValue(posts, sensorId, rows, utcNow);

            // One product's failure costs that product's values of the period only.
            foreach (var (productId, values) in posts)
            {
                try
                {
                    await PostAsync(productId, values, token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
                {
                    _logger.Error(ex, $"Table feed sensors: the values of product {productId} for the period ending {utcNow:O} were not posted");
                }
            }
        }

        private void AddProductValues(Dictionary<Guid, List<FeedValue>> posts, Guid productId, TableFeedProductPeriod period, DateTime time)
        {
            // A removed product: its sensors went with it.
            if (!_cache.TryGetRootProduct(productId, out _, out _))
            {
                _refusedPending.Remove(productId);
                _unreadablePending.Remove(productId);
                return;
            }

            var product = $"{Node}/";
            var minutes = Minutes(_productPeriod);

            if (period is { Blocks: > 0 })
            {
                Add(posts, productId, Int(product + RowsName, period.Rows, time),
                    $"Rows of the table sensors of this product the server took in the last {minutes} (rows of refused tables are not counted). " +
                    "No value in a period means no block arrived: a TTL alert on this sensor watches the feed.", Unit.Count);
                Add(posts, productId, Double(product + StoredName, period.Bytes / 1024.0 / 1024.0, time),
                    $"Size of the table blocks of this product stored in the last {minutes}, as stored (MB).", Unit.MB);
                Add(posts, productId, Double(product + LagName, period.MaxLagTicks / (double)TimeSpan.TicksPerSecond, time),
                    $"Delivery lag of the table blocks of this product, the largest in the last {minutes} (seconds): the time a block arrived " +
                    "minus the time of its last row. Measured against the clock of the provider that wrote the rows, so a clock skew is in it; " +
                    "negative when that clock is ahead of the server's. Posted only for a period in which a block was stored.", Unit.Seconds);
            }

            AddFailure(posts, productId, product + RefusedName, period?.RefusedTables ?? 0, _refusedPending, time,
                $"Tables of the blocks of this product the server refused in the last {minutes}: unknown or blocked table sensor, " +
                "another kind of sensor, another schema, an invalid identifier. Their rows are never served; the server log has the reasons. " +
                "Posts 0 once after a period with refusals, then nothing until the next refusal.");
            AddFailure(posts, productId, product + UnreadableName, period?.UnreadableBlocks ?? 0, _unreadablePending, time,
                $"Table blocks of this product the hourly merge could not read (missing or damaged) in the hours it committed in the last {minutes}. " +
                "They are kept in the block store; their rows are not served. Posts 0 once after a period with such blocks, then nothing until the next.");
        }

        private void AddFailure(Dictionary<Guid, List<FeedValue>> posts, Guid productId, string path, long count, HashSet<Guid> pending, DateTime time, string description)
        {
            if (count > 0)
                pending.Add(productId);
            else if (!pending.Remove(productId))
                return;

            Add(posts, productId, Int(path, count, time), description, Unit.Count);
        }

        private void AddTableValue(Dictionary<Guid, List<FeedValue>> posts, Guid sensorId, long rows, DateTime time)
        {
            // A removed table sensor: its Rows sensor goes silent.
            if (_cache.GetSensor(sensorId) is not TableSensorModel { Root: { } root } table)
                return;

            Add(posts, root.Id, Int($"{Node}/{table.Path}/{RowsName}", rows, time),
                $"Rows of the table sensor '{table.Path}' the server took in the last {Minutes(_tablePeriod)}. " +
                "No value in a period means no rows arrived for it: a TTL alert on this sensor watches the table.", Unit.Count);
        }

        private void Add(Dictionary<Guid, List<FeedValue>> posts, Guid productId, SensorValueBase value, string description, Unit unit)
        {
            // The path as the server stores it, and the checks of any value's path (a table path deep enough leaves no
            // room for .tables and the sensor's name).
            var normalized = new AddSensorValueRequest(Guid.Empty, productId, value.Path, null);

            if (!normalized.TryCheckRequest(out var error))
            {
                Warn(productId, value.Path, $"not posted: {error}");
                return;
            }

            value.Path = normalized.Path;

            if (!posts.TryGetValue(productId, out var list))
                posts[productId] = list = [];

            list.Add(new FeedValue(value, description, unit));
        }

        private async Task PostAsync(Guid productId, List<FeedValue> feed, CancellationToken token)
        {
            var answer = await _values.AddServerValuesAsync(productId, feed.Select(f => f.Value).ToList(), token);
            var missing = new List<FeedValue>();

            foreach (var item in feed)
            {
                if (!answer.TryGetValue(item.Value.Path, out var error))
                    continue;

                if (error == IServerSensorValues.NoSensor)
                    missing.Add(item);
                else
                    Warn(productId, item.Value.Path, $"not posted: {error}");
            }

            if (missing.Count == 0)
                return;

            var registered = new List<FeedValue>(missing.Count);

            foreach (var item in missing)
                if (await RegisterAsync(productId, item, token))
                    registered.Add(item);

            if (registered.Count == 0)
                return;

            answer = await _values.AddServerValuesAsync(productId, registered.Select(f => f.Value).ToList(), token);

            foreach (var (path, error) in answer)
                Warn(productId, path, $"not posted after its registration: {error}");
        }

        // An ordinary sensor of the value's type, with its description and unit; it inherits the TTL of its parent as any
        // new sensor does.
        private async Task<bool> RegisterAsync(Guid productId, FeedValue item, CancellationToken token)
        {
            var request = new SensorAddOrUpdateRequest(productId, item.Value.Path)
            {
                Type = item.Value.Type == HSMSensorDataObjects.SensorType.IntSensor ? SensorType.Integer : SensorType.Double,
                TypeIsExplicit = true,
                Update = new SensorUpdate
                {
                    Id = Guid.Empty,
                    Description = item.Description,
                    SelectedUnit = item.Unit,
                    Initiator = InitiatorInfo.AsSystemInfo(Initiator),
                },
            };

            var result = await _cache.AddOrUpdateSensorAsync(request, token);

            if (!result.IsOk)
                Warn(productId, item.Value.Path, $"not registered: {result.Error}");

            return result.IsOk;
        }

        private void Warn(Guid productId, string path, string message)
        {
            if (_warned.Count >= MaxWarnedPaths)
                _warned.Clear();

            if (_warned.Add($"{productId}/{path}"))
                _logger.Warn($"Table feed sensor '{path}' of product {productId} {message} (logged once per sensor)");
        }

        private static IntSensorValue Int(string path, long value, DateTime time) =>
            new() { Path = path, Value = (int)Math.Min(value, int.MaxValue), Time = time };

        private static DoubleSensorValue Double(string path, double value, DateTime time) =>
            new() { Path = path, Value = Math.Round(value, 3), Time = time };

        private static string Minutes(TimeSpan period) =>
            period.TotalMinutes == 1 ? "minute" : $"{period.TotalMinutes.ToString(CultureInfo.InvariantCulture)} minutes";


        private sealed record FeedValue(SensorValueBase Value, string Description, Unit Unit);
    }
}
