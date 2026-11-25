using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMDatabase.AccessManager.TableBlocks;
using HSMServer.BackgroundServices;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.TableSensors;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using HSMServer.ServerConfiguration;
using HSMTableBlocks;
using Xunit;

namespace HSMServer.Core.Tests.TableSensors
{
    public sealed class TableFeedSensorsFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(TableFeedSensorsTests);
    }


    /// <summary>
    /// The feed sensors of table sensors (#1847): the counters a put and the merge's commit add to, and the service that
    /// drains them into ordinary sensors of the table's product. No timing: the service's tick takes its time as a
    /// parameter, the counters' clock is a seam.
    /// </summary>
    [Collection("Database collection")]
    public class TableFeedSensorsTests : MonitoringCoreTestsBase<TableFeedSensorsFixture>
    {
        // On the 5-minute grid: the first tick at T0 + 1 minute drains the tables too (the first tick always does).
        private static readonly DateTime T0 = new(2026, 11, 24, 9, 0, 0, DateTimeKind.Utc);

        private static readonly TableSchema Quotes = new([
            new TableSchemaColumn("time", TableColumnType.Time),
            new TableSchemaColumn("bid", TableColumnType.Double),
        ]);

        private readonly Guid _product = TestProductsManager.ProductId;
        private readonly Guid _producer = Guid.NewGuid();
        private readonly string _root = $"f{Guid.NewGuid():N}"[..12];
        private readonly TableBlockService _service;
        private long _producerSequence;
        private DateTime _now = T0;


        public TableFeedSensorsTests(TableFeedSensorsFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture)
        {
            _service = new TableBlockService(_databaseCoreManager.DatabaseCore, _valuesCache, new TableBlockOptions { MinFreeDiskMb = 0 });
            _service.Feed.Clock = () => _now;
        }


        // === Counters ===

        [Fact]
        public async Task A_stored_block_counts_rows_per_table_and_rows_bytes_and_refused_tables_per_product_and_a_duplicate_adds_nothing()
        {
            var a = await RegisterAsync("a");
            var b = await RegisterAsync("b");

            var block = Block(("a", 3, T0), ("b", 2, T0), ("unknown", 4, T0));
            var stored = _service.Put(_product, _producer, 1, block);
            Assert.Equal(TableBlockPutOutcome.Stored, stored.Outcome);
            Assert.Equal(1, stored.RefusedCount);

            var product = Assert.Single(_service.Feed.DrainProducts());
            Assert.Equal(_product, product.Key);
            Assert.Equal(1, product.Value.Blocks);
            Assert.Equal(5, product.Value.Rows);
            Assert.Equal(1, product.Value.RefusedTables);
            Assert.Equal(0, product.Value.UnreadableBlocks);
            Assert.Equal(_databaseCoreManager.DatabaseCore.TableBlocks.GetBlocks(_product).Single().StoredLength, product.Value.Bytes);

            var tables = _service.Feed.DrainTables();
            Assert.Equal(2, tables.Count);
            Assert.Equal(3, tables[a.Id]);
            Assert.Equal(2, tables[b.Id]);

            // The same producer and sequence again: a duplicate, nothing counted.
            Assert.Equal(TableBlockPutOutcome.Duplicate, _service.Put(_product, _producer, 1, block).Outcome);
            Assert.Empty(_service.Feed.DrainProducts());
            Assert.Empty(_service.Feed.DrainTables());
        }

        [Fact]
        public async Task A_block_with_every_table_refused_counts_its_refusals_only()
        {
            await RegisterAsync("a");

            var result = _service.Put(_product, _producer, 1, Block(("x", 2, T0), ("y", 1, T0)));
            Assert.Equal(TableBlockPutOutcome.AllRefused, result.Outcome);

            var product = Assert.Single(_service.Feed.DrainProducts()).Value;
            Assert.Equal((0L, 0L, 0L, 2L), (product.Blocks, product.Rows, product.Bytes, product.RefusedTables));
            Assert.Empty(_service.Feed.DrainTables());
        }

        [Fact]
        public async Task The_lag_is_the_largest_of_the_period_and_a_provider_clock_ahead_gives_a_negative_one()
        {
            await RegisterAsync("a");

            _now = T0.AddSeconds(90);
            Put(Block(("a", 1, T0.AddSeconds(30))));   // 60 s
            _now = T0.AddSeconds(100);
            Put(Block(("a", 2, T0.AddSeconds(10))));   // 89 s: the last row of the block is at T0 + 11 s
            _now = T0.AddSeconds(120);
            Put(Block(("a", 1, T0.AddSeconds(110)))); // 10 s

            var period = _service.Feed.DrainProducts()[_product];
            Assert.Equal(3, period.Blocks);
            Assert.Equal(TimeSpan.FromSeconds(89).Ticks, period.MaxLagTicks);

            _now = T0;
            Put(Block(("a", 1, T0.AddSeconds(5))));
            Assert.Equal(TimeSpan.FromSeconds(-5).Ticks, _service.Feed.DrainProducts()[_product].MaxLagTicks);
        }

        // === The service ===

        [Fact]
        public async Task The_sensors_are_created_under_the_tables_product_with_their_paths_types_units_descriptions_and_values()
        {
            var a = await RegisterAsync("a");

            _now = T0.AddSeconds(50);
            Put(Block(("a", 3, T0.AddSeconds(20)), ("unknown", 1, T0)));
            var bytes = _databaseCoreManager.DatabaseCore.TableBlocks.GetBlocks(_product).Single().StoredLength;

            var feed = Feed();
            await feed.TickAsync(T0.AddMinutes(1));

            var rows = Sensor(".tables/Rows");
            Assert.Equal(SensorType.Integer, rows.Type);
            Assert.Equal(Unit.Count, rows.OriginalUnit);
            Assert.Contains("in the last minute", rows.Description);
            Assert.Equal(3, Int(rows));
            Assert.Equal(T0.AddMinutes(1), rows.LastValue.Time);

            var stored = Sensor(".tables/Stored");
            Assert.Equal(SensorType.Double, stored.Type);
            Assert.Equal(Unit.MB, stored.OriginalUnit);
            Assert.Equal(Math.Round(bytes / 1024.0 / 1024.0, 3), Double(stored));

            var lag = Sensor(".tables/Delivery lag");
            Assert.Equal(SensorType.Double, lag.Type);
            Assert.Equal(Unit.Seconds, lag.OriginalUnit);
            Assert.Contains("clock skew", lag.Description);
            // The block's last row is at T0 + 22 s (rows at 20, 21, 22), it arrived at T0 + 50 s.
            Assert.Equal(28.0, Double(lag));

            var refused = Sensor(".tables/Tables refused");
            Assert.Equal(SensorType.Integer, refused.Type);
            Assert.Equal(1, Int(refused));

            // No unreadable block yet: no sensor.
            Assert.False(_valuesCache.TryGetSensorByPath(_product, ".tables/Blocks the merge could not read", out _));

            var tableRows = Sensor($".tables/{_root}/a/Rows");
            Assert.Equal(SensorType.Integer, tableRows.Type);
            Assert.Equal(Unit.Count, tableRows.OriginalUnit);
            Assert.Contains($"'{_root}/a'", tableRows.Description);
            Assert.Contains("in the last 5 minutes", tableRows.Description);
            Assert.Equal(3, Int(tableRows));

            // Ordinary sensors of the table's product, in its tree (a user of the product sees them), the table sensor untouched.
            var product = _valuesCache.GetProduct(_product);
            var node = Assert.Single(product.SubProducts.Values, p => p.DisplayName == ".tables");
            Assert.Equal(4, node.Sensors.Count);
            foreach (var sensor in new[] { rows, stored, lag, refused, tableRows })
            {
                Assert.IsNotType<TableSensorModel>(sensor);
                Assert.Equal(_product, sensor.Root.Id);
            }

            Assert.IsType<TableSensorModel>(_valuesCache.GetSensor(a.Id));
            Assert.Null(_valuesCache.GetSensor(a.Id).LastValue);
        }

        [Fact]
        public async Task An_idle_product_or_table_posts_nothing_and_a_failure_counter_posts_0_once_then_stays_silent()
        {
            await RegisterAsync("a");
            await RegisterAsync("b");
            var feed = Feed();

            // Nothing at all: no sensor.
            await feed.TickAsync(T0.AddMinutes(1));
            Assert.False(_valuesCache.TryGetSensorByPath(_product, ".tables/Rows", out _));

            Put(Block(("a", 2, T0), ("b", 1, T0), ("unknown", 1, T0)));
            _service.Feed.AddUnreadable(_product, 2);
            await feed.TickAsync(T0.AddMinutes(2));

            Assert.Equal(1, Int(Sensor(".tables/Tables refused")));
            Assert.Equal(2, Int(Sensor(".tables/Blocks the merge could not read")));
            // The table period has not ended (the first tick drained it): no table sensor yet.
            Assert.False(_valuesCache.TryGetSensorByPath(_product, $".tables/{_root}/a/Rows", out _));

            // Only table a in the next minute: the failure counters post 0 once.
            Put(Block(("a", 4, T0)));
            await feed.TickAsync(T0.AddMinutes(3));

            Assert.Equal(4, Int(Sensor(".tables/Rows")));
            AssertLast(".tables/Tables refused", 0, T0.AddMinutes(3));
            AssertLast(".tables/Blocks the merge could not read", 0, T0.AddMinutes(3));

            // Idle: nothing is posted, the zeros are not repeated.
            await feed.TickAsync(T0.AddMinutes(4));
            AssertLast(".tables/Rows", 4, T0.AddMinutes(3));
            AssertLast(".tables/Tables refused", 0, T0.AddMinutes(3));
            Assert.Equal(T0.AddMinutes(3), Sensor(".tables/Delivery lag").LastValue.Time);

            // The table period ends: a and b had rows in it (a 2 + 4, b 1).
            await feed.TickAsync(T0.AddMinutes(5));
            AssertLast($".tables/{_root}/a/Rows", 6, T0.AddMinutes(5));
            AssertLast($".tables/{_root}/b/Rows", 1, T0.AddMinutes(5));

            // Only a in the next table period: b posts nothing.
            Put(Block(("a", 1, T0)));
            for (int minute = 6; minute <= 10; minute++)
                await feed.TickAsync(T0.AddMinutes(minute));

            AssertLast($".tables/{_root}/a/Rows", 1, T0.AddMinutes(10));
            AssertLast($".tables/{_root}/b/Rows", 1, T0.AddMinutes(5));
        }

        [Fact]
        public async Task The_per_table_sensors_can_be_switched_off()
        {
            await RegisterAsync("a");
            Put(Block(("a", 2, T0)));

            await Feed(new TableSensorsConfig { FeedTableSensors = false }).TickAsync(T0.AddMinutes(1));

            Assert.Equal(2, Int(Sensor(".tables/Rows")));
            Assert.False(_valuesCache.TryGetSensorByPath(_product, $".tables/{_root}/a/Rows", out _));
        }

        [Fact]
        public async Task A_removed_table_sensor_goes_silent()
        {
            var a = await RegisterAsync("a");
            Put(Block(("a", 2, T0)));

            await _valuesCache.RemoveSensorAsync(a.Id);
            await Feed().TickAsync(T0.AddMinutes(1));

            Assert.Equal(2, Int(Sensor(".tables/Rows")));
            Assert.False(_valuesCache.TryGetSensorByPath(_product, $".tables/{_root}/a/Rows", out _));
        }

        [Fact]
        public async Task Odd_table_paths_get_their_sensor_and_a_table_sensor_on_a_feed_path_is_not_shadowed()
        {
            var odd = await RegisterAsync("a b.c/ünï code/.tables");
            var onFeedPath = await RegisterPathAsync(".tables/Rows");
            // Nine parts: .tables and Rows would make eleven, more than a path may have.
            var deep = await RegisterAsync("1/2/3/4/5/6/7/8");

            Put(Block(("a b.c/ünï code/.tables", 2, T0), ("1/2/3/4/5/6/7/8", 1, T0)));
            _service.Put(_product, _producer, NextProducerSequence(), RawBlock(".tables/Rows", 5, T0));

            await Feed().TickAsync(T0.AddMinutes(1));

            AssertLast($".tables/{_root}/a b.c/ünï code/.tables/Rows", 2, T0.AddMinutes(1));
            Assert.IsNotType<TableSensorModel>(Sensor($".tables/{_root}/a b.c/ünï code/.tables/Rows"));
            Assert.False(_valuesCache.TryGetSensorByPath(_product, $".tables/{deep.Path}/Rows", out _));

            // The product's Rows path holds a table sensor: it stays one, with no value; its own rows sensor is posted.
            Assert.Same(onFeedPath, Sensor(".tables/Rows"));
            Assert.IsType<TableSensorModel>(onFeedPath);
            Assert.Null(onFeedPath.LastValue);
            AssertLast(".tables/.tables/Rows/Rows", 5, T0.AddMinutes(1));

            // The other product sensors are posted as usual.
            Assert.NotNull(Sensor(".tables/Stored").LastValue);
            Assert.NotNull(odd);
        }

        [Fact]
        public async Task Server_values_are_added_only_to_an_existing_sensor_of_their_type_that_is_not_a_table_sensor()
        {
            var table = await RegisterPathAsync(".tables/t");
            IServerSensorValues values = _valuesCache;

            var answer = await values.AddServerValuesAsync(_product, [
                new HSMSensorDataObjects.SensorValueRequests.IntSensorValue { Path = ".tables/missing", Value = 1, Time = T0 },
                new HSMSensorDataObjects.SensorValueRequests.IntSensorValue { Path = ".tables/t", Value = 1, Time = T0 },
            ]);

            Assert.Equal(IServerSensorValues.NoSensor, answer[".tables/missing"]);
            Assert.Contains("table sensor", answer[".tables/t"]);
            Assert.False(_valuesCache.TryGetSensorByPath(_product, ".tables/missing", out _));
            Assert.Null(table.LastValue);

            // A sensor of another type takes no value.
            Assert.True((await values.AddServerValuesAsync(_product, [
                new HSMSensorDataObjects.SensorValueRequests.DoubleSensorValue { Path = ".tables/i", Value = 1, Time = T0 }])).ContainsKey(".tables/i"));
            await _valuesCache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(_product, ".tables/i")
            {
                Update = new SensorUpdate { Id = Guid.Empty },
                Type = SensorType.Integer,
                TypeIsExplicit = true,
            });
            var wrong = await values.AddServerValuesAsync(_product, [
                new HSMSensorDataObjects.SensorValueRequests.DoubleSensorValue { Path = ".tables/i", Value = 1, Time = T0 }]);
            Assert.Contains("Integer", wrong[".tables/i"]);

            // An unknown product.
            Assert.Single(await values.AddServerValuesAsync(Guid.NewGuid(), [
                new HSMSensorDataObjects.SensorValueRequests.IntSensorValue { Path = "x", Value = 1, Time = T0 }]));
        }

        [Fact]
        public void The_feed_periods_are_validated_with_the_name_of_their_key()
        {
            var defaults = new TableSensorsConfig();
            Assert.Equal((1, 5, true), (defaults.FeedProductPeriodMinutes, defaults.FeedTablePeriodMinutes, defaults.FeedTableSensors));

            Assert.Contains("TableSensors.FeedProductPeriodMinutes = 0",
                Assert.Throws<InvalidOperationException>(() => new TableSensorsConfig { FeedProductPeriodMinutes = 0 }.Validate()).Message);
            Assert.Contains("TableSensors.FeedTablePeriodMinutes = 1441",
                Assert.Throws<InvalidOperationException>(() => new TableSensorsConfig { FeedTablePeriodMinutes = 1441 }.Validate()).Message);
            Assert.Contains("TableSensors.FeedTablePeriodMinutes = 5",
                Assert.Throws<InvalidOperationException>(() => new TableSensorsConfig { FeedProductPeriodMinutes = 2 }.Validate()).Message);
        }


        // === Helpers ===

        private TableFeedSensorsService Feed(TableSensorsConfig config = null) =>
            new(_service.Feed, _valuesCache, _valuesCache, config ?? new TableSensorsConfig());

        private long NextProducerSequence() => Interlocked.Increment(ref _producerSequence) + 1000;

        private void Put(byte[] block) => Assert.NotEqual(TableBlockPutOutcome.Invalid, _service.Put(_product, _producer, NextProducerSequence(), block).Outcome);

        private Task<BaseSensorModel> RegisterAsync(string name) => RegisterPathAsync($"{_root}/{name}");

        private async Task<BaseSensorModel> RegisterPathAsync(string path)
        {
            var result = await _valuesCache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(_product, path)
            {
                Update = new SensorUpdate { Id = Guid.Empty },
                Type = SensorType.Table,
                TypeIsExplicit = true,
                Schema = Quotes,
            });

            Assert.True(result.IsOk, result.Error);
            return Sensor(path);
        }

        private BaseSensorModel Sensor(string path)
        {
            Assert.True(_valuesCache.TryGetSensorByPath(_product, path, out var sensor), $"no sensor at {path}");
            return sensor;
        }

        private void AssertLast(string path, int value, DateTime time)
        {
            var sensor = Sensor(path);
            Assert.Equal((value, time), (Int(sensor), sensor.LastValue.Time));
        }

        private static int Int(BaseSensorModel sensor) => Assert.IsType<IntegerValue>(sensor.LastValue).Value;

        private static double Double(BaseSensorModel sensor) => Assert.IsType<DoubleValue>(sensor.LastValue).Value;

        // Tables of the schema under the test's root: each with its rows one second apart from its start.
        private byte[] Block(params (string Name, int Rows, DateTime Start)[] tables)
        {
            using var writer = new TableBlockWriter();

            foreach (var (name, rows, start) in tables)
                Write(writer, $"{_root}/{name}", rows, start);

            return writer.ToArray();
        }

        private static byte[] RawBlock(string identifier, int rows, DateTime start)
        {
            using var writer = new TableBlockWriter();
            Write(writer, identifier, rows, start);
            return writer.ToArray();
        }

        private static void Write(TableBlockWriter writer, string identifier, int rows, DateTime start)
        {
            writer.BeginTable(Encoding.UTF8.GetBytes(identifier), Quotes.Id, rows);
            writer.AddTimeColumn(Enumerable.Range(0, rows).Select(i => start.AddSeconds(i).Ticks).ToArray());
            writer.AddDoubleColumn(Enumerable.Range(0, rows).Select(i => (double)i).ToArray());
        }
    }
}
