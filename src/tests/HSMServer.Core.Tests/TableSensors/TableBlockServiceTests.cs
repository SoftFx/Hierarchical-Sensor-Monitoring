using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMDatabase.AccessManager.TableBlocks;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.TableSensors;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using HSMTableBlocks;
using Xunit;

namespace HSMServer.Core.Tests.TableSensors
{
    public sealed class TableBlockServiceFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(TableBlockServiceTests);
    }


    /// <summary>
    /// The block service (#1844): a put checks a block against the sensors of a product and stores it, a read returns the
    /// rows of one table sensor. Every test works on its own database (each test gets its own DatabaseCore) and its own
    /// node and producer.
    /// </summary>
    [Collection("Database collection")]
    public class TableBlockServiceTests : MonitoringCoreTestsBase<TableBlockServiceFixture>
    {
        private static readonly DateTime T0 = new(2026, 11, 24, 9, 0, 0, DateTimeKind.Utc);

        private static readonly TableSchema Quotes = new([
            new TableSchemaColumn("time", TableColumnType.Time),
            new TableSchemaColumn("bid", TableColumnType.Double),
            new TableSchemaColumn("size", TableColumnType.Int64),
        ]);

        private readonly Guid _product = TestProductsManager.ProductId;
        private readonly Guid _producer = Guid.NewGuid();
        private readonly string _root = $"t{Guid.NewGuid():N}"[..12];
        private readonly TableBlockService _service;
        private long _producerSequence;


        public TableBlockServiceTests(TableBlockServiceFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture)
        {
            _service = new TableBlockService(_databaseCoreManager.DatabaseCore, _valuesCache, new TableBlockOptions { MinFreeDiskMb = 0 });
        }


        // === Put and read ===

        [Fact]
        public async Task Rows_are_read_in_time_order_across_hours_late_and_overlapping_blocks_with_equal_times_in_arrival_order()
        {
            var sensor = await RegisterAsync("q");
            var store = Store;

            // Arrivals in three hours; the third block arrives late with old times, the second overlaps the first.
            SetClock(T0.AddMinutes(50));
            AssertStored(Put(Table("q", (T0.AddMinutes(10), 1.0, 10), (T0.AddMinutes(5), 2.0, 20), (T0.AddMinutes(20), 3.0, 30))));

            SetClock(T0.AddMinutes(70));
            AssertStored(Put(Table("q", (T0.AddMinutes(15), 4.0, 40), (T0.AddMinutes(10), 5.0, 50))));

            SetClock(T0.AddMinutes(130));
            AssertStored(Put(Table("q", (T0.AddMinutes(1), 6.0, 60), (T0.AddMinutes(10), 7.0, 70))));

            var hours = store.GetBlocks(_product).Select(b => b.ArrivalHourTicks).Distinct().Count();
            Assert.Equal(3, hours);

            var rows = Read(sensor, T0, T0.AddHours(3));

            Assert.Equal([T0.AddMinutes(1).Ticks, T0.AddMinutes(5).Ticks, T0.AddMinutes(10).Ticks, T0.AddMinutes(10).Ticks,
                          T0.AddMinutes(10).Ticks, T0.AddMinutes(15).Ticks, T0.AddMinutes(20).Ticks], rows.Time);
            // Equal times keep their order of arrival: block 1, then block 2, then the late block 3.
            Assert.Equal([6.0, 2.0, 1.0, 5.0, 7.0, 4.0, 3.0], rows.Columns[1].DoubleValues);
            Assert.Equal([60L, 20, 10, 50, 70, 40, 30], rows.Columns[2].Int64Values);
            Assert.False(rows.Truncated);
            Assert.Equal(3, rows.BlocksRead);

            // [from, to): from is in, to is out; a block outside the range is not read.
            var edge = Read(sensor, T0.AddMinutes(10), T0.AddMinutes(15));
            Assert.Equal([1.0, 5.0, 7.0], edge.Columns[1].DoubleValues);

            var none = Read(sensor, T0.AddMinutes(30), T0.AddMinutes(40));
            Assert.Equal(0, none.RowCount);
            Assert.Equal(0, none.BlocksRead);
        }

        [Fact]
        public async Task A_read_beyond_its_limit_returns_the_earliest_rows_marked_truncated()
        {
            var sensor = await RegisterAsync("q");

            for (int block = 0; block < 5; block++)
                AssertStored(Put(Table("q", Enumerable.Range(0, 10).Select(i => (T0.AddSeconds(100 - block * 10 - i), (double)(block * 10 + i), 0L)).ToArray())));

            var rows = _service.Read(sensor.Id, T0, T0.AddHours(1), maxRows: 7).Value;

            Assert.True(rows.Truncated);
            Assert.Equal(7, rows.RowCount);
            Assert.Equal(Enumerable.Range(51, 7).Select(s => T0.AddSeconds(s).Ticks), rows.Time);

            Assert.False(Read(sensor, T0, T0.AddHours(1)).Truncated);
        }

        [Fact]
        public async Task A_refused_table_inside_an_accepted_block_is_recorded_and_never_served()
        {
            var good = await RegisterAsync("good");
            var blocked = await RegisterAsync("blocked");
            var wrongTypes = await RegisterAsync("types");
            await CreateIntSensorAsync("int");

            Assert.True((await _valuesCache.UpdateSensorAsync(new SensorUpdate { Id = blocked.Id, State = SensorState.Blocked })).IsOk);

            var types = new[] { TableColumnType.Time, TableColumnType.Double, TableColumnType.Double };
            var block = Block(
                ("good", Quotes.Id, Quotes.ColumnTypes.ToArray()),
                ("missing", Quotes.Id, Quotes.ColumnTypes.ToArray()),
                ("int", Quotes.Id, Quotes.ColumnTypes.ToArray()),
                ("blocked", Quotes.Id, Quotes.ColumnTypes.ToArray()),
                ("types", Quotes.Id, types),
                ("good-schema", Quotes.Id + 1, Quotes.ColumnTypes.ToArray()));

            var result = _service.Put(_product, _producer, NextProducerSequence(), block);

            Assert.Equal(TableBlockPutOutcome.Stored, result.Outcome);
            Assert.Equal(6, result.TableCount);
            Assert.Equal(1, result.AcceptedTables);
            Assert.Equal(
                [(1, $"{_root}/missing", TableRefusalReason.UnknownSensor), (2, $"{_root}/int", TableRefusalReason.NotATableSensor),
                 (3, $"{_root}/blocked", TableRefusalReason.SensorBlocked), (4, $"{_root}/types", TableRefusalReason.ColumnTypesMismatch),
                 (5, $"{_root}/good-schema", TableRefusalReason.UnknownSensor)],
                result.Refusals.Select(r => (r.Index, r.TableId, r.Reason)));
            Assert.Equal(5, Store.Statistics.TablesRefused);

            Assert.Equal(1, Read(good, T0, T0.AddHours(1)).RowCount);
            Assert.Equal(0, Read(wrongTypes, T0, T0.AddHours(1)).RowCount);

            // Unblocked, the sensor still never gets the rows that were refused while it was blocked.
            Assert.True((await _valuesCache.UpdateSensorAsync(new SensorUpdate { Id = blocked.Id, State = SensorState.Available })).IsOk);
            Assert.Equal(0, Read(blocked, T0, T0.AddHours(1)).RowCount);
        }

        [Fact]
        public async Task A_table_with_another_schema_id_and_an_identifier_that_is_not_UTF8_are_refused()
        {
            var sensor = await RegisterAsync("q");

            var block = Block(("q", Quotes.Id ^ 0x5A5A, Quotes.ColumnTypes.ToArray()));
            var badId = BlockWithIds([0xC3, 0x28], Quotes.Id);

            var schema = _service.Put(_product, _producer, NextProducerSequence(), block);
            Assert.Equal(TableBlockPutOutcome.AllRefused, schema.Outcome);
            Assert.Equal(TableRefusalReason.SchemaIdMismatch, Assert.Single(schema.Refusals).Reason);

            var id = _service.Put(_product, _producer, NextProducerSequence(), badId);
            Assert.Equal(TableRefusalReason.InvalidIdentifier, Assert.Single(id.Refusals).Reason);

            Assert.Equal(0, Read(sensor, T0, T0.AddHours(1)).RowCount);
        }

        [Fact]
        public async Task A_block_every_table_of_which_is_refused_is_not_stored_and_its_resend_is_taken_once_the_sensor_exists()
        {
            var block = Table("late", (T0, 1.0, 1L));
            var sequence = NextProducerSequence();
            var blocksBefore = Store.BlockCount;

            var refused = _service.Put(_product, _producer, sequence, block);

            Assert.Equal(TableBlockPutOutcome.AllRefused, refused.Outcome);
            Assert.Null(refused.Sequence);
            Assert.Equal(TableRefusalReason.UnknownSensor, Assert.Single(refused.Refusals).Reason);
            Assert.Equal(blocksBefore, Store.BlockCount);

            var sensor = await RegisterAsync("late");

            var resent = _service.Put(_product, _producer, sequence, block);
            Assert.Equal(TableBlockPutOutcome.Stored, resent.Outcome);
            Assert.Equal(1, Read(sensor, T0, T0.AddHours(1)).RowCount);
        }

        [Fact]
        public async Task A_block_sent_twice_is_stored_once_and_answered_as_the_first_time()
        {
            var sensor = await RegisterAsync("q");
            var block = Block(("q", Quotes.Id, Quotes.ColumnTypes.ToArray()), ("nobody", Quotes.Id, Quotes.ColumnTypes.ToArray()));
            var sequence = NextProducerSequence();

            var first = _service.Put(_product, _producer, sequence, block);
            var second = _service.Put(_product, _producer, sequence, block);

            Assert.Equal(TableBlockPutOutcome.Stored, first.Outcome);
            Assert.Equal(TableBlockPutOutcome.Duplicate, second.Outcome);
            Assert.Equal(first.Sequence, second.Sequence);
            Assert.Equal(first.Refusals, second.Refusals);
            Assert.Equal(1, Read(sensor, T0, T0.AddHours(1)).RowCount);
        }

        [Fact]
        public void A_block_that_breaks_the_format_or_the_limits_is_refused_as_a_result_not_a_fault()
        {
            var block = Table("q", (T0, 1.0, 1L));
            var damaged = (byte[])block.Clone();
            damaged[^1] ^= 0xFF;

            var invalid = _service.Put(_product, _producer, NextProducerSequence(), damaged);
            Assert.Equal(TableBlockPutOutcome.Invalid, invalid.Outcome);
            Assert.Equal(TableBlockError.ChecksumMismatch, invalid.FormatError);

            var strict = new TableBlockService(_databaseCoreManager.DatabaseCore, _valuesCache,
                new TableBlockOptions { MinFreeDiskMb = 0, Limits = new TableBlockLimits(maxTables: 1) });
            var tooMany = strict.Put(_product, _producer, NextProducerSequence(), Block(("a", Quotes.Id, Quotes.ColumnTypes.ToArray()), ("b", Quotes.Id, Quotes.ColumnTypes.ToArray())));
            Assert.Equal(TableBlockPutOutcome.Invalid, tooMany.Outcome);
            Assert.Equal(TableBlockError.LimitExceeded, tooMany.FormatError);

            Assert.Equal(2, Store.Statistics.InvalidBlocks);
            Assert.False(Store.IsOpened);
        }

        [Fact]
        public async Task A_store_that_cannot_take_a_block_is_a_fault_and_nothing_is_stored()
        {
            var sensor = await RegisterAsync("q");
            var store = (HSMDatabase.LevelDB.TableBlocks.TableBlockDatabase)Store;
            var blocks = store.BlockCount;

            store.FreeBytes = () => 0;
            store.MinFreeBytes = 1;

            Assert.Throws<TableBlockStoreException>(() => Put(Table("q", (T0, 1.0, 1L))));
            Assert.Equal(blocks, store.BlockCount);

            store.FreeBytes = () => long.MaxValue;
            AssertStored(Put(Table("q", (T0, 1.0, 1L))));
            Assert.Equal(1, Read(sensor, T0, T0.AddHours(1)).RowCount);
        }

        // === Sensors that come and go ===

        [Fact]
        public async Task A_new_sensor_on_the_path_of_a_removed_one_never_reads_the_old_ones_rows()
        {
            var old = await RegisterAsync("q");
            AssertStored(Put(Table("q", (T0, 1.0, 1L))));
            Assert.Equal(1, Read(old, T0, T0.AddHours(1)).RowCount);

            await _valuesCache.RemoveSensorAsync(old.Id);
            var stale = Put(Table("q", (T0.AddMinutes(1), 2.0, 2L)));
            Assert.Equal(TableBlockPutOutcome.AllRefused, stale.Outcome);

            var renewed = await RegisterAsync("q");
            Assert.True(((TableSensorModel)renewed).FirstSequence > ((TableSensorModel)old).FirstSequence);
            Assert.Equal(0, Read(renewed, T0, T0.AddHours(1)).RowCount);

            AssertStored(Put(Table("q", (T0.AddMinutes(2), 3.0, 3L))));
            Assert.Equal([3.0], Read(renewed, T0, T0.AddHours(1)).Columns[1].DoubleValues);
        }

        [Fact]
        public async Task A_sensor_created_while_a_put_is_matching_never_takes_rows_matched_before_it_existed()
        {
            // The put reserved its sequence, then the old sensor was removed and a new one created on the path, all before
            // the put matched its tables: the new sensor's first sequence is above the block's, so the table is refused
            // and neither the old sensor's rows nor this block's become the new sensor's.
            var old = await RegisterAsync("q");
            AssertStored(Put(Table("q", (T0, 1.0, 1L))));

            BaseSensorModel renewed = null;
            _service.AfterReserve = _ =>
            {
                _valuesCache.RemoveSensorAsync(old.Id).GetAwaiter().GetResult();
                renewed = RegisterAsync("q").GetAwaiter().GetResult();
            };

            var raced = Put(Table("q", (T0.AddMinutes(1), 2.0, 2L)));
            _service.AfterReserve = null;

            Assert.Equal(TableBlockPutOutcome.AllRefused, raced.Outcome);
            Assert.Equal(TableRefusalReason.SensorCreatedAfterBlock, Assert.Single(raced.Refusals).Reason);
            Assert.Equal(0, Read(renewed, T0, T0.AddHours(1)).RowCount);

            // A put numbered after the sensor existed is the sensor's.
            AssertStored(Put(Table("q", (T0.AddMinutes(2), 3.0, 3L))));
            Assert.Equal([3.0], Read(renewed, T0, T0.AddHours(1)).Columns[1].DoubleValues);
        }

        [Fact]
        public void A_block_numbered_before_its_sensor_existed_is_refused_even_when_matched_after()
        {
            // The sensor appears after the put reserved its sequence but before the put matches.
            BaseSensorModel sensor = null;
            _service.AfterReserve = _ => sensor ??= RegisterAsync("q").GetAwaiter().GetResult();

            var put = Put(Table("q", (T0, 1.0, 1L)));
            _service.AfterReserve = null;

            // The sequence was reserved before the sensor read it, so the table is refused: the rule keeps no row of a
            // block numbered before the sensor, even one matched after it.
            Assert.Equal(TableRefusalReason.SensorCreatedAfterBlock, Assert.Single(put.Refusals).Reason);

            AssertStored(Put(Table("q", (T0.AddMinutes(1), 2.0, 2L))));
            Assert.Equal([2.0], Read(sensor, T0, T0.AddHours(1)).Columns[1].DoubleValues);
        }

        // === Threads ===

        [Fact]
        public async Task Puts_from_many_threads_and_reads_meanwhile_lose_nothing()
        {
            var sensors = new List<BaseSensorModel>();
            for (int s = 0; s < 4; s++)
                sensors.Add(await RegisterAsync($"s{s}"));

            const int producers = 6, perProducer = 25;
            var stop = false;

            var reader = Task.Run(() =>
            {
                while (!Volatile.Read(ref stop))
                    foreach (var sensor in sensors)
                    {
                        var rows = Read(sensor, T0, T0.AddHours(10));
                        for (int i = 1; i < rows.RowCount; i++)
                            Assert.True(rows.Time[i] >= rows.Time[i - 1]);
                    }
            });

            await Task.WhenAll(Enumerable.Range(0, producers).Select(p => Task.Run(() =>
            {
                var producer = Guid.NewGuid();
                for (int i = 0; i < perProducer; i++)
                {
                    var block = Block(Enumerable.Range(0, 4).Select(s => ($"s{s}", Quotes.Id, Quotes.ColumnTypes.ToArray())).ToArray(),
                        time: T0.AddSeconds(p * 1000 + i));
                    var result = _service.Put(_product, producer, i, block);
                    Assert.Equal(TableBlockPutOutcome.Stored, result.Outcome);
                }
            })));

            Volatile.Write(ref stop, true);
            await reader;

            foreach (var sensor in sensors)
                Assert.Equal(producers * perProducer, Read(sensor, T0, T0.AddHours(10)).RowCount);
        }

        // === Inert ===

        [Fact]
        public async Task A_server_without_table_sensors_creates_no_Tables_folder()
        {
            await CreateIntSensorAsync("int");
            _ = _service.Read(Guid.NewGuid(), T0, T0.AddHours(1));
            await _valuesCache.CheckSensorsHistoryAsync();

            Assert.False(Directory.Exists(_databaseCoreManager.DatabaseCore.DatabaseSettings.PathToTableBlocks));
            Assert.False(Store.IsOpened);
            Assert.Equal(0, _databaseCoreManager.DatabaseCore.TableBlocksDbSize);

            await RegisterAsync("first");

            Assert.True(Directory.Exists(_databaseCoreManager.DatabaseCore.DatabaseSettings.PathToTableBlocks));
            Assert.True(Store.IsOpened);
        }


        // === Helpers ===

        private ITableBlockStore Store => _databaseCoreManager.DatabaseCore.TableBlocks;

        private void SetClock(DateTime now) => ((HSMDatabase.LevelDB.TableBlocks.TableBlockDatabase)Store).Clock = () => now;

        private long NextProducerSequence() => Interlocked.Increment(ref _producerSequence);

        private TableBlockPutResult Put(byte[] block) => _service.Put(_product, _producer, NextProducerSequence(), block);

        private TableRows Read(BaseSensorModel sensor, DateTime from, DateTime to)
        {
            var result = _service.Read(sensor.Id, from, to);
            Assert.True(result.IsOk, result.Error);
            return result.Value;
        }

        private static void AssertStored(TableBlockPutResult result)
        {
            Assert.Equal(TableBlockPutOutcome.Stored, result.Outcome);
            Assert.Empty(result.Refusals);
        }

        private async Task<BaseSensorModel> RegisterAsync(string name)
        {
            var path = $"{_root}/{name}";
            var result = await _valuesCache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(_product, path)
            {
                Update = new SensorUpdate { Id = Guid.Empty },
                Type = SensorType.Table,
                TypeIsExplicit = true,
                Schema = Quotes,
            });

            Assert.True(result.IsOk, result.Error);
            Assert.True(_valuesCache.TryGetSensorByPath(_product, path, out var sensor));
            return sensor;
        }

        private async Task CreateIntSensorAsync(string name)
        {
            var value = SensorValuesFactory.BuildSensorValue(SensorType.Integer, $"{_root}/{name}", DateTime.UtcNow);
            Assert.True((await _valuesCache.AddSensorValueAsync(Guid.Parse(TestProductsManager.TestProductKey.Id), _product, value)).IsOk);
        }

        // One table of the schema with the given rows.
        private byte[] Table(string name, params (DateTime Time, double Bid, long Size)[] rows)
        {
            using var writer = new TableBlockWriter();
            writer.BeginTable(Encoding.UTF8.GetBytes($"{_root}/{name}"), Quotes.Id, rows.Length);
            writer.AddTimeColumn(rows.Select(r => r.Time.Ticks).ToArray());
            writer.AddDoubleColumn(rows.Select(r => r.Bid).ToArray());
            writer.AddInt64Column(rows.Select(r => r.Size).ToArray());
            return writer.ToArray();
        }

        // One row per table, of the given types and schema id.
        private byte[] Block(params (string Name, uint SchemaId, TableColumnType[] Types)[] tables) => Block(tables, T0);

        private byte[] Block((string Name, uint SchemaId, TableColumnType[] Types)[] tables, DateTime time)
        {
            using var writer = new TableBlockWriter();

            foreach (var (name, schemaId, types) in tables)
            {
                writer.BeginTable(Encoding.UTF8.GetBytes($"{_root}/{name}"), schemaId, 1);
                writer.AddTimeColumn([time.Ticks]);

                foreach (var type in types.Skip(1))
                {
                    if (type == TableColumnType.Double)
                        writer.AddDoubleColumn([1.5]);
                    else
                        writer.AddInt64Column([7L]);
                }
            }

            return writer.ToArray();
        }

        private static byte[] BlockWithIds(byte[] id, uint schemaId)
        {
            using var writer = new TableBlockWriter();
            writer.BeginTable(id, schemaId, 1);
            writer.AddTimeColumn([T0.Ticks]);
            writer.AddDoubleColumn([1.0]);
            writer.AddInt64Column([1L]);
            return writer.ToArray();
        }
    }
}
