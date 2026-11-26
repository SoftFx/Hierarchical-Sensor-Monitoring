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

        // === Null cells (#1859) ===

        [Fact]
        public async Task Nulls_stay_with_their_rows_across_blocks_with_and_without_a_map()
        {
            var sensor = await RegisterAsync("q");

            // Block 1: a null bid. Block 2: no nulls at all (no map in it). Block 3: a null size and a row of nulls only.
            AssertStored(Put(NullableTable("q", (T0.AddMinutes(5), null, 50), (T0.AddMinutes(1), 1.0, 10))));
            AssertStored(Put(NullableTable("q", (T0.AddMinutes(3), 3.0, 30), (T0.AddMinutes(2), 2.0, 20))));
            AssertStored(Put(NullableTable("q", (T0.AddMinutes(4), 4.0, null), (T0.AddMinutes(6), null, null))));

            var rows = Read(sensor, T0, T0.AddHours(1));

            Assert.Equal(Enumerable.Range(1, 6).Select(m => T0.AddMinutes(m).Ticks), rows.Time);
            AssertCells(rows, [1.0, 2.0, 3.0, 4.0, null, null], [10, 20, 30, null, 50, null]);
            Assert.False(rows.Columns[0].HasNulls);

            // A range that leaves every null row out returns no map at all.
            var dense = Read(sensor, T0.AddMinutes(1), T0.AddMinutes(4));
            AssertCells(dense, [1.0, 2.0, 3.0], [10, 20, 30]);
            Assert.False(dense.Columns[1].HasNulls);
            Assert.False(dense.Columns[2].HasNulls);
            Assert.Null(dense.Columns[1].NullMap);
        }

        [Fact]
        public async Task Equal_times_keep_a_null_with_its_row_when_only_the_earlier_block_has_it()
        {
            var sensor = await RegisterAsync("q");

            // Both blocks hold T0 + 10 min; only the earlier block's row is null. The sort moves rows around them.
            AssertStored(Put(NullableTable("q", (T0.AddMinutes(20), 5.0, 5), (T0.AddMinutes(10), null, 1))));
            AssertStored(Put(NullableTable("q", (T0.AddMinutes(10), 2.0, 2), (T0.AddMinutes(1), 3.0, 3))));

            var rows = Read(sensor, T0, T0.AddHours(1));

            Assert.Equal([T0.AddMinutes(1).Ticks, T0.AddMinutes(10).Ticks, T0.AddMinutes(10).Ticks, T0.AddMinutes(20).Ticks], rows.Time);
            AssertCells(rows, [3.0, null, 2.0, 5.0], [3, 1, 2, 5]);
        }

        [Fact]
        public async Task The_row_limit_cuts_exactly_at_a_null_row()
        {
            var sensor = await RegisterAsync("q");

            // Five blocks of ten rows (51..60, then 61..70, ...), each falling in time, so the limit trims while it
            // collects and the earliest rows, with the nulls, go through every trim. Second 57 has a null bid and second
            // 58 a null size.
            for (int block = 0; block < 5; block++)
                AssertStored(Put(NullableTable("q", Enumerable.Range(0, 10).Select(i =>
                {
                    var second = 51 + block * 10 + (9 - i);
                    return (T0.AddSeconds(second), second == 57 ? (double?)null : second, second == 58 ? (long?)null : second);
                }).ToArray())));

            // The last row kept is the null one.
            var seven = _service.Read(sensor.Id, T0, T0.AddHours(1), maxRows: 7).Value;
            Assert.True(seven.Truncated);
            Assert.Equal(Enumerable.Range(51, 7).Select(s => T0.AddSeconds(s).Ticks), seven.Time);
            AssertCells(seven, [51, 52, 53, 54, 55, 56, null], [51, 52, 53, 54, 55, 56, 57]);
            Assert.False(seven.Columns[2].HasNulls);

            // The first row dropped is the null one: no map is left for its column.
            var six = _service.Read(sensor.Id, T0, T0.AddHours(1), maxRows: 6).Value;
            AssertCells(six, [51, 52, 53, 54, 55, 56], [51, 52, 53, 54, 55, 56]);
            Assert.False(six.Columns[1].HasNulls);

            var eight = _service.Read(sensor.Id, T0, T0.AddHours(1), maxRows: 8).Value;
            AssertCells(eight, [51, 52, 53, 54, 55, 56, null, 58], [51, 52, 53, 54, 55, 56, 57, null]);
        }

        [Fact]
        public async Task A_block_with_nulls_passes_the_type_check_of_the_put_and_a_malformed_map_is_refused_there()
        {
            var sensor = await RegisterAsync("q");
            var block = NullableTable("q", (T0, null, 1), (T0.AddSeconds(1), 2.0, null));

            Assert.True(TableBlockReader.ReadDirectory(block).Entries[0].HasNulls(1));
            var stored = Put(block);
            AssertStored(stored);

            // The same block with its last map byte (the size column, row 1) cleared: a map with no null row.
            var broken = ResealedWithPayload(block, payload => payload[^1] = 0);
            var invalid = Put(broken);
            Assert.Equal(TableBlockPutOutcome.Invalid, invalid.Outcome);
            Assert.Equal(TableBlockError.CorruptNullMap, invalid.FormatError);

            AssertCells(Read(sensor, T0, T0.AddHours(1)), [null, 2.0], [1, null]);
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
        public async Task A_block_resent_after_its_hour_was_merged_is_a_duplicate_whose_refusals_are_unknown()
        {
            // #1850: the blocks of a merged hour are deleted; the producer's watermark still recognises a resend.
            var sensor = await RegisterAsync("q");
            var block = Block(("q", Quotes.Id, Quotes.ColumnTypes.ToArray()));
            var sequence = NextProducerSequence();

            Assert.Equal(TableBlockPutOutcome.Stored, _service.Put(_product, _producer, sequence, block).Outcome);
            Store.DeleteMergedHour(Store.GetBlocks(_product)[^1].ArrivalHourTicks, new HashSet<long>(), [new TableBlockWatermark(_product, _producer, sequence)]);

            var resent = _service.Put(_product, _producer, sequence, block);

            Assert.Equal(TableBlockPutOutcome.Duplicate, resent.Outcome);
            Assert.False(resent.RefusalsKnown);
            Assert.Null(resent.Sequence);
            Assert.Empty(resent.Refusals);
            Assert.Equal(0, Read(sensor, T0, T0.AddHours(1)).RowCount);

            // An ordinary duplicate knows its refusals.
            var next = NextProducerSequence();
            _service.Put(_product, _producer, next, block);
            Assert.True(_service.Put(_product, _producer, next, block).RefusalsKnown);
            Assert.True(_service.Put(_product, _producer, NextProducerSequence(), block).RefusalsKnown);
        }

        [Fact]
        public void The_service_bounds_the_open_week_databases_of_the_batch_store()
        {
            var service = new TableBlockService(_databaseCoreManager.DatabaseCore, _valuesCache, new TableBlockOptions { MinFreeDiskMb = 0, MaxOpenBatchDatabases = 7 });

            Assert.Equal(7, _databaseCoreManager.DatabaseCore.TableBatches.MaxOpenDatabases);
            Assert.False(_databaseCoreManager.DatabaseCore.TableBatches.IsOpened);
            GC.KeepAlive(service);
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

        [Fact]
        public async Task A_read_takes_unspecified_times_as_UTC_and_converts_local_ones()
        {
            var sensor = await RegisterAsync("q");
            AssertStored(Put(Table("q", (T0.AddMinutes(10), 1.0, 1L), (T0.AddMinutes(30), 2.0, 2L))));

            var from = T0.AddMinutes(5);
            var to = T0.AddMinutes(20);

            var utc = Read(sensor, from, to);
            var unspecified = Read(sensor, DateTime.SpecifyKind(from, DateTimeKind.Unspecified), DateTime.SpecifyKind(to, DateTimeKind.Unspecified));
            var local = Read(sensor, from.ToLocalTime(), to.ToLocalTime());

            Assert.Equal([1.0], utc.Columns[1].DoubleValues);
            Assert.Equal(utc.Time, unspecified.Time);
            Assert.Equal(utc.Time, local.Time);
        }

        [Fact]
        public void A_service_over_a_database_without_a_block_store_is_refused_at_construction()
        {
            var database = new Moq.Mock<HSMServer.Core.DataLayer.IDatabaseCore>();

            var error = Assert.Throws<ArgumentException>(() => new TableBlockService(database.Object, _valuesCache));
            Assert.Contains("no table block store", error.Message);
        }

        [Fact]
        public async Task A_block_is_taken_only_under_the_exact_path_the_registration_stored()
        {
            // Registration normalizes a path (BaseUpdateRequest: a leading separator, empty segments and the spaces around
            // segments go); the block's identifier is looked up exactly, case included. So a block accepted for a sensor
            // always carries the sensor's Path byte for byte, which is what Read looks for: nothing can be accepted under
            // an identifier the read would not find. An identifier in the raw form is refused (UnknownSensor), not lost.
            var raw = $"/{_root}/ spaced ";
            var registered = await _valuesCache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(_product, raw)
            {
                Update = new SensorUpdate { Id = Guid.Empty },
                Type = SensorType.Table,
                TypeIsExplicit = true,
                Schema = Quotes,
            });
            Assert.True(registered.IsOk, registered.Error);

            var sensor = GetSensorByPath($"{_root}/spaced");
            Assert.Equal($"{_root}/spaced", sensor.Path);

            foreach (var identifier in new[] { raw, $"/{_root}/spaced", $"{_root}/Spaced", $"{_root}//spaced" })
            {
                var refused = _service.Put(_product, _producer, NextProducerSequence(), RawTable(identifier, T0));
                Assert.Equal(TableBlockPutOutcome.AllRefused, refused.Outcome);
                Assert.Equal(TableRefusalReason.UnknownSensor, Assert.Single(refused.Refusals).Reason);
            }

            AssertStored(_service.Put(_product, _producer, NextProducerSequence(), RawTable(sensor.Path, T0)));
            Assert.Equal(1, Read(sensor, T0, T0.AddHours(1)).RowCount);
        }

        [Fact]
        public async Task A_deleted_store_numbers_its_blocks_from_the_sensors_first_sequences_after_a_restart()
        {
            // The operator deletes Tables/ (the sequence mark goes with it): the sequence would start again at 0, below the
            // first sequence of every registered table sensor, and their tables would be refused. The first put raises the
            // store's next sequence to the largest first sequence; building the service opens nothing.
            await RegisterAsync("early");
            for (int i = 0; i < 5; i++)
                AssertStored(Put(Table("early", (T0.AddSeconds(i), 1.0, 1L))));

            var late = (TableSensorModel)await RegisterAsync("late");
            Assert.True(late.FirstSequence > 0);

            var settings = _databaseCoreManager.DatabaseCore.DatabaseSettings;
            _databaseCoreManager.DatabaseCore.Dispose();
            Directory.Delete(settings.PathToTableBlocks, recursive: true);

            var (database, cache) = Restart(settings);
            await using var _ = cache;
            var service = new TableBlockService(database, cache, new TableBlockOptions { MinFreeDiskMb = 0 });

            Assert.False(Directory.Exists(settings.PathToTableBlocks), "building the service created the store");

            var put = service.Put(_product, Guid.NewGuid(), 1, Table("late", (T0, 2.0, 2L)));

            Assert.Equal(TableBlockPutOutcome.Stored, put.Outcome);
            Assert.Empty(put.Refusals);
            Assert.True(put.Sequence >= late.FirstSequence);

            var rows = service.Read(late.Id, T0, T0.AddHours(1));
            Assert.True(rows.IsOk, rows.Error);
            Assert.Equal([2.0], rows.Value.Columns[1].DoubleValues);
        }

        [Fact]
        public async Task A_store_that_cannot_open_costs_the_puts_only_and_the_same_service_recovers()
        {
            // A damaged sequence mark makes the store refuse to open. The service is still built (it opens nothing), every
            // put is a fault, and once the mark is repaired the same service raises the floor and stores the block.
            var sensor = (TableSensorModel)await RegisterAsync("q");
            AssertStored(Put(Table("q", (T0, 1.0, 1L))));

            var settings = _databaseCoreManager.DatabaseCore.DatabaseSettings;
            _databaseCoreManager.DatabaseCore.Dispose();
            var mark = Path.Combine(settings.PathToTableBlocks, HSMDatabase.LevelDB.TableBlocks.TableBlockDatabase.SequenceMarkName);
            File.WriteAllText(mark, "damaged");

            var (database, cache) = Restart(settings);
            await using var _ = cache;

            var service = new TableBlockService(database, cache, new TableBlockOptions { MinFreeDiskMb = 0 });

            Assert.Throws<TableBlockStoreException>(() => service.Put(_product, _producer, NextProducerSequence(), Table("q", (T0.AddMinutes(1), 2.0, 2L))));

            File.WriteAllText(mark, "100000");
            var now = DateTime.UtcNow;
            ((HSMDatabase.LevelDB.TableBlocks.TableBlockDatabase)database.TableBlocks).Clock = () => now.AddMinutes(2);

            var put = service.Put(_product, _producer, NextProducerSequence(), Table("q", (T0.AddMinutes(2), 3.0, 3L)));
            Assert.Equal(TableBlockPutOutcome.Stored, put.Outcome);
            Assert.True(put.Sequence >= sensor.FirstSequence);
            Assert.Equal([1.0, 3.0], service.Read(sensor.Id, T0, T0.AddHours(1)).Value.Columns[1].DoubleValues);
        }

        [Fact]
        public async Task The_store_is_opened_before_the_tree_lock_when_a_table_sensor_is_registered()
        {
            // Review of PR #1857: the first open of a process loads (and may rebuild) the index. It must not happen under
            // the tree-structure lock, which every registration of the server waits for.
            var store = (HSMDatabase.LevelDB.TableBlocks.TableBlockDatabase)Store;
            Assert.False(store.IsOpened);

            bool? heldAtOpen = null;
            store.OnOpen = () => heldAtOpen = _valuesCache.IsTreeStructureLockHeld;

            await RegisterAsync("q");

            Assert.True(store.IsOpened);
            Assert.Equal(false, heldAtOpen);
        }

        [Fact]
        public async Task A_read_of_a_store_that_cannot_open_throws_the_store_fault()
        {
            // A store fault is a fault for a read as for a put (the caller answers 5xx), not an error result.
            var sensor = await RegisterAsync("q");
            AssertStored(Put(Table("q", (T0, 1.0, 1L))));

            var settings = _databaseCoreManager.DatabaseCore.DatabaseSettings;
            _databaseCoreManager.DatabaseCore.Dispose();
            File.WriteAllText(Path.Combine(settings.PathToTableBlocks, HSMDatabase.LevelDB.TableBlocks.TableBlockDatabase.SequenceMarkName), "damaged");

            var (database, cache) = Restart(settings);
            await using var _ = cache;
            var service = new TableBlockService(database, cache, new TableBlockOptions { MinFreeDiskMb = 0 });

            Assert.Throws<TableBlockStoreException>(() => service.Read(sensor.Id, T0, T0.AddHours(1)));
        }

        [Fact]
        public async Task A_read_after_a_failed_reopen_throws_the_store_fault_and_the_same_service_reads_once_the_store_reopens()
        {
            // Review of PR #1857: a reopen that failed closed the databases and left the old index in memory; a read then
            // returned a successful result with no rows, which says "no data in the range", and counted every block as
            // a read failure.
            var sensor = await RegisterAsync("q");
            var store = (HSMDatabase.LevelDB.TableBlocks.TableBlockDatabase)Store;
            var now = T0.AddMinutes(30);
            store.Clock = () => now;

            AssertStored(Put(Table("q", (T0, 1.0, 1L))));

            var mark = Path.Combine(_databaseCoreManager.DatabaseCore.DatabaseSettings.PathToTableBlocks, HSMDatabase.LevelDB.TableBlocks.TableBlockDatabase.SequenceMarkName);
            var good = File.ReadAllText(mark);

            // A failed write, the backoff passed, a damaged mark: the next put reopens, the reopen fails.
            store.FailForTest(new IOException("simulated"));
            now = now.AddMinutes(2);
            File.WriteAllText(mark, "damaged");
            Assert.Throws<TableBlockStoreException>(() => Put(Table("q", (T0.AddMinutes(1), 2.0, 2L))));

            var failures = store.Statistics.ReadFailures;
            Assert.Throws<TableBlockStoreException>(() => _service.Read(sensor.Id, T0, T0.AddHours(1)));
            Assert.Equal(failures, store.Statistics.ReadFailures);

            File.WriteAllText(mark, good);
            now = now.AddMinutes(2);

            var rows = Read(sensor, T0, T0.AddHours(1));
            Assert.Equal([1.0], rows.Columns[1].DoubleValues);
            Assert.Equal(0, rows.BlocksFailed);
        }

        [Fact]
        public async Task A_store_whose_folder_is_removed_while_the_server_runs_keeps_taking_the_sensors_tables()
        {
            // The store failed and closed, its folder was removed, and a put opened a fresh store without a mark: its
            // sequence started again at 0, below the first sequence of every registered sensor, and their tables were all
            // refused (SensorCreatedAfterBlock), with no restart to raise the floor again.
            await RegisterAsync("early");
            for (int i = 0; i < 5; i++)
                AssertStored(Put(Table("early", (T0.AddSeconds(i), 1.0, 1L))));

            var sensor = (TableSensorModel)await RegisterAsync("q");
            Assert.True(sensor.FirstSequence > 0);
            AssertStored(Put(Table("q", (T0, 1.0, 1L))));

            var store = (HSMDatabase.LevelDB.TableBlocks.TableBlockDatabase)Store;
            var now = DateTime.UtcNow;
            store.Clock = () => now;

            var folder = _databaseCoreManager.DatabaseCore.DatabaseSettings.PathToTableBlocks;
            var mark = Path.Combine(folder, HSMDatabase.LevelDB.TableBlocks.TableBlockDatabase.SequenceMarkName);

            // Failed and closed: a failed write, the backoff passed, a reopen that fails on a damaged mark.
            store.FailForTest(new IOException("simulated"));
            now = now.AddMinutes(2);
            File.WriteAllText(mark, "damaged");
            Assert.Throws<TableBlockStoreException>(() => Put(Table("q", (T0.AddMinutes(1), 2.0, 2L))));

            Directory.Delete(folder, recursive: true);
            now = now.AddMinutes(2);

            var put = Put(Table("q", (T0.AddMinutes(2), 3.0, 3L)));
            Assert.Equal(TableBlockPutOutcome.Stored, put.Outcome);
            Assert.Empty(put.Refusals);
            Assert.True(put.Sequence >= sensor.FirstSequence);

            // The rows of the removed blocks went with the folder; the new block's are read.
            Assert.Equal([3.0], Read(sensor, T0, T0.AddHours(1)).Columns[1].DoubleValues);
        }

        [Fact]
        public async Task A_read_from_a_disposed_store_throws_the_store_fault()
        {
            var sensor = await RegisterAsync("q");
            AssertStored(Put(Table("q", (T0, 1.0, 1L))));

            ((HSMDatabase.LevelDB.TableBlocks.TableBlockDatabase)Store).Dispose();

            Assert.Throws<TableBlockStoreException>(() => _service.Read(sensor.Id, T0, T0.AddHours(1)));
        }

        // A restart of the server over the same environment database and Tables folder. DatabaseCore.Dispose leaves the
        // layout, change-set and snapshot databases open, so the restarted core gets fresh ones of those.
        private static (HSMDatabase.DatabaseWorkCore.DatabaseCore Database, HSMServer.Core.Cache.TreeValuesCache Cache) Restart(HSMDatabase.AccessManager.IDatabaseSettings settings)
        {
            var restart = ((HSMDatabase.Settings.DatabaseSettings)settings) with
            {
                ServerLayoutDatabaseName = settings.ServerLayoutDatabaseName + "_restart",
                ChangeSetsDatabaseName = settings.ChangeSetsDatabaseName + "_restart",
                SnaphotsDatabaseName = settings.SnaphotsDatabaseName + "_restart",
            };
            var database = new HSMDatabase.DatabaseWorkCore.DatabaseCore(restart);
            var snapshot = new Moq.Mock<HSMServer.Core.TreeStateSnapshot.ITreeStateSnapshot>();
            snapshot.Setup(s => s.Keys).Returns(new HSMServer.Core.TreeStateSnapshot.StateCollection<
                HSMServer.Core.TreeStateSnapshot.States.LastKeyState, HSMDatabase.AccessManager.DatabaseEntities.SnapshotEntity.LastKeyStateEntity>());

            var cache = new HSMServer.Core.Cache.TreeValuesCache(database, snapshot.Object, new HSMServer.Core.Journal.JournalService(database),
                new HSMServer.Core.Schedule.AlertScheduleProvider(database), new HSMServer.Core.ChangeSets.ChangeSetJournal(database));

            return (database, cache);
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

        // One table of the schema; a null bid or size is a cell without a value.
        private byte[] NullableTable(string name, params (DateTime Time, double? Bid, long? Size)[] rows)
        {
            var bidNulls = new byte[TableNullMap.ByteLength(rows.Length)];
            var sizeNulls = new byte[TableNullMap.ByteLength(rows.Length)];
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Bid is null) TableNullMap.SetNull(bidNulls, i);
                if (rows[i].Size is null) TableNullMap.SetNull(sizeNulls, i);
            }

            using var writer = new TableBlockWriter();
            writer.BeginTable(Encoding.UTF8.GetBytes($"{_root}/{name}"), Quotes.Id, rows.Length);
            writer.AddTimeColumn(rows.Select(r => r.Time.Ticks).ToArray());
            // A value under a null is ignored by the writer: put a non-zero one there to show it.
            writer.AddDoubleColumn(rows.Select(r => r.Bid ?? -7.0).ToArray(), bidNulls);
            writer.AddInt64Column(rows.Select(r => r.Size ?? -7L).ToArray(), sizeNulls);
            return writer.ToArray();
        }

        // Every cell of the bid and size columns: a value, or null where the row holds none (its value array holds 0).
        private static void AssertCells(TableRows rows, double?[] bids, long?[] sizes)
        {
            Assert.Equal(bids.Length, rows.RowCount);
            Assert.Equal(bids, Enumerable.Range(0, rows.RowCount).Select(r => rows.Columns[1].IsNull(r) ? null : (double?)rows.Columns[1].DoubleValues[r]));
            Assert.Equal(sizes, Enumerable.Range(0, rows.RowCount).Select(r => rows.Columns[2].IsNull(r) ? null : (long?)rows.Columns[2].Int64Values[r]));
            Assert.All(Enumerable.Range(0, rows.RowCount).Where(rows.Columns[1].IsNull), r => Assert.Equal(0.0, rows.Columns[1].DoubleValues[r]));
            Assert.All(Enumerable.Range(0, rows.RowCount).Where(rows.Columns[2].IsNull), r => Assert.Equal(0L, rows.Columns[2].Int64Values[r]));

            // A map is there exactly when a row of its column is null, and is ceil(rows / 8) bytes.
            Assert.Equal(bids.Any(b => b is null), rows.Columns[1].HasNulls);
            Assert.Equal(sizes.Any(s => s is null), rows.Columns[2].HasNulls);
            foreach (var column in rows.Columns.Where(c => c.HasNulls))
                Assert.Equal(TableNullMap.ByteLength(rows.RowCount), column.NullMap.Length);
        }

        // The block with its decompressed payload changed, compressed and checksummed again.
        private static byte[] ResealedWithPayload(byte[] block, Action<byte[]> change)
        {
            byte[] payload;
            using (var decompressor = new ZstdSharp.Decompressor())
                payload = decompressor.Unwrap(block.AsSpan(TableBlockFormat.HeaderSize)).ToArray();

            change(payload);

            byte[] compressed;
            using (var compressor = new ZstdSharp.Compressor(3))
                compressed = compressor.Wrap(payload).ToArray();

            var resealed = new byte[TableBlockFormat.HeaderSize + compressed.Length];
            block.AsSpan(0, TableBlockFormat.HeaderSize).CopyTo(resealed);
            compressed.CopyTo(resealed, TableBlockFormat.HeaderSize);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(resealed.AsSpan(32), compressed.Length);

            var crc = new System.IO.Hashing.Crc32();
            crc.Append(resealed.AsSpan(0, 40));
            crc.Append(resealed.AsSpan(TableBlockFormat.HeaderSize));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(resealed.AsSpan(40), crc.GetCurrentHashAsUInt32());
            return resealed;
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

        private BaseSensorModel GetSensorByPath(string path)
        {
            Assert.True(_valuesCache.TryGetSensorByPath(_product, path, out var sensor), $"no sensor at {path}");
            return sensor;
        }

        private static byte[] RawTable(string identifier, DateTime time)
        {
            using var writer = new TableBlockWriter();
            writer.BeginTable(Encoding.UTF8.GetBytes(identifier), Quotes.Id, 1);
            writer.AddTimeColumn([time.Ticks]);
            writer.AddDoubleColumn([1.0]);
            writer.AddInt64Column([1L]);
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
