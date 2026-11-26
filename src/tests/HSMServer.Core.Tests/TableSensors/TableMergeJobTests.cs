using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.TableBatches;
using HSMDatabase.AccessManager.TableBlocks;
using HSMDatabase.LevelDB.TableBatches;
using HSMDatabase.LevelDB.TableBlocks;
using HSMCommon.Model;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.DataLayer;
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
    public sealed class TableMergeFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(TableMergeJobTests);
    }


    /// <summary>
    /// The hourly merge of table blocks into batches and the read that joins them (#1850 part 3): the crash points of the
    /// merge and of the deletion, the read at every stage, products apart, damaged blocks, verification, faults, removed
    /// sensors, nulls and sizes, watermarks across a restart, the deletion delay, the row limit and the memory budget.
    /// No timing: the job's clock is a parameter, the stores' clocks are seams.
    /// </summary>
    [Collection("Database collection")]
    public partial class TableMergeJobTests : MonitoringCoreTestsBase<TableMergeFixture>, IAsyncLifetime
    {
        private static readonly DateTime Hour0 = new(2026, 11, 24, 9, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime From = Hour0.AddHours(-3), To = Hour0.AddHours(12);

        private static readonly TableSchema Quotes = new([
            new TableSchemaColumn("time", TableColumnType.Time),
            new TableSchemaColumn("bid", TableColumnType.Double),
            new TableSchemaColumn("size", TableColumnType.Int64),
        ]);

        private readonly Guid _product = TestProductsManager.ProductId;
        private readonly Guid _producer = Guid.NewGuid();
        private readonly string _root = $"m{Guid.NewGuid():N}"[..12];
        private readonly List<IAsyncDisposable> _restarted = [];
        private readonly List<IDisposable> _databases = [];
        private long _producerSequence;
        private int _restarts;
        private DateTime _now = Hour0;

        private Server _server;


        public TableMergeJobTests(TableMergeFixture fixture, DatabaseRegisterFixture registerFixture) : base(fixture, registerFixture)
        {
            _server = Build(_databaseCoreManager.DatabaseCore, _valuesCache);
        }


        Task IAsyncLifetime.InitializeAsync() => InitializeAsync();

        async Task IAsyncLifetime.DisposeAsync()
        {
            foreach (var cache in _restarted)
                await cache.DisposeAsync();

            foreach (var database in _databases)
                database.Dispose();
        }


        // === Crash points ===

        [Theory]
        [InlineData("encoded")]
        [InlineData("part-written")]
        [InlineData("before-commit")]
        [InlineData("committed")]
        [InlineData("watermarks-raised")]
        [InlineData("after-hour-blocks-deleted")]
        [InlineData("before-hour-compaction")]
        public async Task A_crash_at_any_point_of_the_merge_keeps_every_read_the_same_and_a_rerun_completes_and_then_changes_nothing(string point)
        {
            var (a, b) = await SeedAsync();
            var before = All(a, b);
            var merge = Hour0.AddHours(1).AddMinutes(3);
            var deletion = point is "after-hour-blocks-deleted" or "before-hour-compaction";

            if (deletion)
            {
                Assert.Null(Pass(merge).Fault);
                Assert.True(_server.Batches.IsHourMerged(_product, Hour0.Ticks));
            }

            Crash(point);
            var report = Pass(deletion ? merge.AddHours(25) : merge);
            Assert.NotNull(report.Fault);

            await RestartAsync(crash: true);

            // Committed exactly when the crash came after the commit.
            Assert.Equal(deletion || point is "committed" or "watermarks-raised", _server.Batches.IsHourMerged(_product, Hour0.Ticks));

            // The same rows in the same order, nothing doubled, wherever the crash left the hour.
            Assert.Equal(before, All(a, b));

            var rerun = Pass(deletion ? merge.AddHours(25) : merge);
            Assert.Null(rerun.Fault);
            Assert.True(_server.Batches.IsHourMerged(_product, Hour0.Ticks));
            Assert.Equal(before, All(a, b));

            if (deletion)
                Assert.DoesNotContain(_server.Blocks.GetBlocks(_product), e => e.ArrivalHourTicks == Hour0.Ticks);

            // A second rerun is a no-op: nothing merged, written or deleted.
            var written = _server.Batches.Statistics.BatchesWritten;
            var again = Pass(deletion ? merge.AddHours(25) : merge);
            Assert.Equal((0, 0, null), (again.HoursMerged, again.HoursDeleted, again.Fault));
            Assert.Equal(written, _server.Batches.Statistics.BatchesWritten);
            Assert.Equal(before, All(a, b));
        }

        // === The read rule ===

        [Fact]
        public async Task A_read_at_every_stage_returns_the_same_rows_with_equal_times_across_a_merged_hour_an_unmerged_hour_and_a_late_block()
        {
            var (a, _) = await SeedAsync();

            var expected = new List<(long, double?, long?)>
            {
                (Hour0.AddMinutes(1).Ticks, 2.0, 20),
                (Hour0.AddMinutes(5).Ticks, null, 10),  // block 1, hour 0
                (Hour0.AddMinutes(5).Ticks, 3.0, 30),   // block 2, hour 0
                (Hour0.AddMinutes(5).Ticks, 5.0, null), // block 3, hour 1 (late)
                (Hour0.AddMinutes(9).Ticks, 4.0, 40),
                (Hour0.AddMinutes(65).Ticks, 6.0, 60),
            };

            Assert.Equal(expected, Rows(a));

            // Hour 0 merged: its blocks stay for the delay and are ignored; hour 1 (with the late block) from blocks.
            Assert.Equal(1, Pass(Hour0.AddHours(1).AddMinutes(3)).HoursMerged);
            Assert.Contains(_server.Blocks.GetBlocks(_product), e => e.ArrivalHourTicks == Hour0.Ticks);
            var merged = _server.Service.Read(a.Id, From, To).Value;
            Assert.Equal(expected, Snapshot(merged));
            Assert.True(merged.BatchesRead > 0);

            // Both merged.
            Assert.Equal(1, Pass(Hour0.AddHours(2).AddMinutes(3)).HoursMerged);
            Assert.Equal(expected, Rows(a));

            // Hour 0's blocks deleted after the delay; hour 1's not yet.
            var deleted = Pass(Hour0.AddHours(1).AddMinutes(3).AddHours(24).AddMinutes(1));
            Assert.Equal(1, deleted.HoursDeleted);
            Assert.DoesNotContain(_server.Blocks.GetBlocks(_product), e => e.ArrivalHourTicks == Hour0.Ticks);
            Assert.Contains(_server.Blocks.GetBlocks(_product), e => e.ArrivalHourTicks == Hour0.AddHours(1).Ticks);
            Assert.Equal(expected, Rows(a));

            Pass(Hour0.AddHours(30));
            Assert.Empty(_server.Blocks.GetBlocks(_product));
            var batchesOnly = _server.Service.Read(a.Id, From, To).Value;
            Assert.Equal(expected, Snapshot(batchesOnly));
            Assert.Equal(0, batchesOnly.BlocksRead);
        }

        [Fact]
        public async Task One_product_committed_and_another_not_reads_the_same_and_nothing_is_deleted_within_the_delay()
        {
            var other = (await _server.Cache.AddProductAsync($"p{Guid.NewGuid():N}"[..10], Guid.Empty)).Id;
            var a = await RegisterAsync("a");
            var o = await RegisterAsync("o", other);

            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("a", [(Hour0.AddMinutes(1), 1.0, 1)]))));
            AssertStored(_server.Service.Put(other, _producer, 1, Block(("o", [(Hour0.AddMinutes(2), 2.0, 2)]))));
            var before = All(a, o);

            // The second product's hour fails at its encoding: the first is committed, the second is not.
            int encoded = 0;
            _server.Job.Checkpoint = p =>
            {
                if (p == "encoded" && ++encoded == 2)
                    throw new IOException("crash");
            };

            var merge = Hour0.AddHours(1).AddMinutes(3);
            Assert.NotNull(Pass(merge).Fault);
            _server.Job.Checkpoint = null;

            var committed = new[] { _product, other }.Where(p => _server.Batches.IsHourMerged(p, Hour0.Ticks)).ToList();
            Assert.Single(committed);
            Assert.Equal(before, All(a, o));

            // The next pass commits the second product; nothing is deleted within the delay.
            Assert.Equal(1, Pass(merge.AddMinutes(1)).HoursMerged);
            Assert.True(_server.Batches.IsHourMerged(_product, Hour0.Ticks) && _server.Batches.IsHourMerged(other, Hour0.Ticks));
            Assert.Equal(before, All(a, o));

            Assert.Equal(1, Pass(merge.AddHours(25)).HoursDeleted);
            Assert.Equal(before, All(a, o));
        }

        [Fact]
        public async Task A_product_that_never_merges_holds_no_blocks_of_another_product_of_the_same_hour()
        {
            // Product A's sensor a kept for one week; product B's sensor o kept for ever. Every hour of B fails its
            // verification (never merged in this process), A's hour 0 is committed and its hour 1 does not verify either.
            var other = (await _server.Cache.AddProductAsync($"p{Guid.NewGuid():N}"[..10], Guid.Empty)).Id;
            var a = await RegisterAsync("a", keep: new TimeIntervalModel(TimeSpan.FromDays(7).Ticks));
            var o = await RegisterAsync("o", other, keep: TimeIntervalModel.None);

            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("a", [(Hour0.AddMinutes(1), 1.0, 1)]))));
            AssertStored(_server.Service.Put(other, _producer, 1, Block(("o", [(Hour0.AddMinutes(2), 2.0, 2)]))));

            _server.Job.AfterDecode = (sensor, batch) =>
            {
                if (sensor == o.Id)
                    batch.Columns[1].DoubleValues[0] += 0.5;
            };
            Assert.Equal(1, Pass(Hour0.AddHours(1).AddMinutes(3)).Mismatches);
            Assert.True(_server.Batches.IsHourMerged(_product, Hour0.Ticks));
            Assert.False(_server.Batches.IsHourMerged(other, Hour0.Ticks));

            _now = Hour0.AddMinutes(70);
            AssertStored(Put(Block(("a", [(Hour0.AddMinutes(61), 3.0, 3)]))));
            AssertStored(_server.Service.Put(other, _producer, 2, Block(("o", [(Hour0.AddMinutes(62), 4.0, 4)]))));
            _server.Job.AfterDecode = (_, batch) => batch.Columns[1].DoubleValues[0] += 0.5;
            Assert.Equal(2, Pass(Hour0.AddHours(2).AddMinutes(3)).Mismatches);
            _server.Job.AfterDecode = null;

            // Past A's delay, A's blocks of hour 0 are deleted; B's stay and still serve its reads.
            Assert.Equal(1, Pass(Hour0.AddHours(26)).HoursDeleted);
            Assert.DoesNotContain(_server.Blocks.GetBlocks(_product), e => e.ArrivalHourTicks == Hour0.Ticks);
            Assert.Contains(_server.Blocks.GetBlocks(other), e => e.ArrivalHourTicks == Hour0.Ticks);
            Assert.Equal(2, Rows(a).Count);
            Assert.Equal(2, Rows(o).Count);

            // Once a's period has passed for the week, A's never-merged blocks of hour 1 are deleted as expired; B's, kept for
            // ever, stay.
            var report = Pass(new DateTime(WeekStart + 2 * Week, DateTimeKind.Utc));
            Assert.Equal(1, report.ExpiredHoursDeleted);
            Assert.Equal(1, _server.Batches.MergeStatistics.ExpiredBlocksDeleted);
            Assert.Empty(_server.Blocks.GetBlocks(_product));
            Assert.Equal(2, _server.Blocks.GetBlocks(other).Count);
            Assert.Equal(2, Rows(o).Count);
        }

        [Fact]
        public async Task A_fault_judging_one_products_expired_blocks_does_not_hold_another_products_of_the_same_hour()
        {
            // Two products, a sensor kept for one week in each; their blocks of hour 0 never merge (they do not verify).
            var other = (await _server.Cache.AddProductAsync($"p{Guid.NewGuid():N}"[..10], Guid.Empty)).Id;
            await RegisterAsync("a", keep: new TimeIntervalModel(TimeSpan.FromDays(7).Ticks));
            await RegisterAsync("o", other, keep: new TimeIntervalModel(TimeSpan.FromDays(7).Ticks));

            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("a", [(Hour0.AddMinutes(1), 1.0, 1)]))));
            var failing = _server.Service.Put(other, _producer, 1, Block(("o", [(Hour0.AddMinutes(2), 2.0, 2)]))).Sequence.Value;

            _server.Job.AfterDecode = (_, batch) => batch.Columns[1].DoubleValues[0] += 0.5;
            Assert.Equal(2, Pass(Hour0.AddHours(1).AddMinutes(3)).Mismatches);
            _server.Job.AfterDecode = null;

            // Both have expired; judging the other product's block meets a read error (a fault, not damage).
            _server.Blocks.BeforeBlockGet = sequence =>
            {
                if (sequence == failing)
                    throw new Exception("IO error: 000005.ldb: the disk did not answer");
            };

            var faults = _server.Batches.MergeStatistics.RetentionFaults;
            var p1 = new DateTime(WeekStart + 2 * Week, DateTimeKind.Utc);
            var report = Pass(p1);

            // The first product's expired blocks are deleted; the other's are kept, the fault counted.
            Assert.Equal(1, report.ExpiredHoursDeleted);
            Assert.NotNull(report.Fault);
            Assert.Equal(faults + 1, _server.Batches.MergeStatistics.RetentionFaults);
            Assert.Empty(_server.Blocks.GetBlocks(_product));
            Assert.Single(_server.Blocks.GetBlocks(other));

            // Without the fault the next retention deletes them too.
            _server.Blocks.BeforeBlockGet = null;
            Assert.Equal(1, Pass(p1.AddHours(1)).ExpiredHoursDeleted);
            Assert.Empty(_server.Blocks.GetBlocks(other));
        }

        // === Damage, verification, faults ===

        [Fact]
        public async Task A_damaged_block_is_kept_listed_and_counted_and_the_rest_of_its_hour_is_merged()
        {
            var a = await RegisterAsync("a");
            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("a", [(Hour0.AddMinutes(1), 1.0, 1)]))));
            var damaged = Put(Block(("a", [(Hour0.AddMinutes(2), 2.0, 2)]))).Sequence.Value;
            AssertStored(Put(Block(("a", [(Hour0.AddMinutes(3), 3.0, 3)]))));

            await RestartAsync(crash: false, beforeOpen: settings =>
            {
                // A byte of the block (after its envelope) changed: the block's checksum fails, the envelope reads.
                using var db = new global::LevelDB.DB(Path.Combine(settings.PathToTableBlocks, TableBlockDatabase.BlocksName), new global::LevelDB.Options());
                var key = TableBlockDatabase.Key(Hour0.Ticks, damaged);
                var value = db.Get(key);
                value[^1] ^= 0x55;
                db.Put(key, value, new global::LevelDB.WriteOptions { Sync = true });
            });

            var before = _server.Service.Read(a.Id, From, To).Value;
            Assert.Equal(1, before.BlocksFailed);
            Assert.Equal([1.0, 3.0], before.Columns[1].DoubleValues);

            Assert.Equal(1, Pass(Hour0.AddHours(1).AddMinutes(3)).HoursMerged);

            var hour = _server.Batches.GetHour(_product, Hour0.Ticks);
            Assert.Equal([damaged], hour.UnreadableBlocks);
            Assert.Equal(2, hour.BlocksMerged);
            Assert.Equal(1, _server.Batches.MergeStatistics.UnreadableBlocksKept);
            Assert.Equal([1.0, 3.0], _server.Service.Read(a.Id, From, To).Value.Columns[1].DoubleValues);

            // The kept block is still a failed block of the answer (#1850 part 4b), as it was before the merge.
            Assert.Equal(1, _server.Service.Read(a.Id, From, To).Value.BlocksFailed);

            // The feed counters of the product have it at the commit (#1847); the service started after the puts.
            Assert.Equal(1, _server.Service.Feed.Drain(tables: false).Products[_product].UnreadableBlocks);

            // Past the delay the other blocks go; the damaged one stays.
            Pass(Hour0.AddHours(30));
            Assert.Equal([damaged], _server.Blocks.GetBlocks(_product).Select(e => e.Sequence));
            Assert.Equal([1.0, 3.0], _server.Service.Read(a.Id, From, To).Value.Columns[1].DoubleValues);
        }

        [Fact]
        public async Task A_verification_mismatch_writes_nothing_for_its_product_is_counted_and_the_blocks_stay()
        {
            var a = await RegisterAsync("a");
            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("a", [(Hour0.AddMinutes(1), 1.0, 1), (Hour0.AddMinutes(2), 2.0, 2)]))));
            var before = Rows(a);

            _server.Job.AfterDecode = (_, batch) => batch.Columns[1].DoubleValues[1] = 2.5;

            var report = Pass(Hour0.AddHours(1).AddMinutes(3));
            Assert.Null(report.Fault);
            Assert.Equal(1, report.Mismatches);
            Assert.False(_server.Batches.IsHourMerged(_product, Hour0.Ticks));
            Assert.Equal(0, _server.Batches.Statistics.BatchesWritten);
            Assert.Equal(1, _server.Batches.MergeStatistics.VerificationMismatches);
            Assert.Equal(before, Rows(a));

            // Not tried again in this process, and its blocks are never deleted while it is not merged.
            var later = Pass(Hour0.AddHours(30));
            Assert.Equal((0, 0, 0), (later.HoursMerged, later.HoursDeleted, later.Mismatches));
            Assert.Single(_server.Blocks.GetBlocks(_product));

            // After a restart (the defect fixed) it is merged.
            await RestartAsync(crash: false);
            Assert.Equal(1, Pass(Hour0.AddHours(30)).HoursMerged);
            Assert.Equal(before, Rows(a));
        }

        [Fact]
        public async Task A_store_fault_in_the_middle_of_an_hour_stops_the_pass_and_a_later_pass_merges_the_hour()
        {
            var (a, b) = await SeedAsync();
            var before = All(a, b);

            _server.Batches.Checkpoint = p =>
            {
                if (p == "before-commit")
                    throw new IOException("disk gone");
            };

            var failed = Pass(Hour0.AddHours(1).AddMinutes(3));
            Assert.NotNull(failed.Fault);
            Assert.False(_server.Batches.IsHourMerged(_product, Hour0.Ticks));
            Assert.Equal(1, _server.Batches.MergeStatistics.Faults);
            Assert.Equal(1, _server.Batches.Statistics.WriteFailures);
            Assert.Equal(before, All(a, b));

            _server.Batches.Checkpoint = null;
            Assert.Equal(1, Pass(Hour0.AddHours(1).AddMinutes(4)).HoursMerged);
            Assert.Equal(before, All(a, b));
        }

        [Fact]
        public async Task Too_little_free_disk_is_a_fault_before_any_batch_is_written()
        {
            var a = await RegisterAsync("a");
            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("a", [(Hour0.AddMinutes(1), 1.0, 1)]))));

            await RestartAsync(crash: false, options: new TableBlockOptions { MinFreeDiskMb = 10 });
            _server.Job.FreeBytes = () => 5L << 20;

            Assert.Contains("free disk", Pass(Hour0.AddHours(2)).Fault);
            Assert.Equal(0, _server.Batches.Statistics.BatchesWritten);
            Assert.False(_server.Batches.IsHourMerged(_product, Hour0.Ticks));

            _server.Job.FreeBytes = () => 50L << 20;
            Assert.Equal(1, Pass(Hour0.AddHours(2)).HoursMerged);
            Assert.Equal([1.0], _server.Service.Read(a.Id, From, To).Value.Columns[1].DoubleValues);
        }

        [Fact]
        public async Task With_too_little_disk_a_pass_still_deletes_the_merged_hours_past_their_delay_and_the_next_pass_merges()
        {
            var a = await RegisterAsync("a");
            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("a", [(Hour0.AddMinutes(1), 1.0, 1)]))));
            Assert.Equal(1, Pass(Hour0.AddHours(1).AddMinutes(3)).HoursMerged);

            // A block of a later hour; then the disk runs low.
            _now = Hour0.AddHours(20);
            AssertStored(Put(Block(("a", [(Hour0.AddHours(20), 2.0, 2)]))));

            await RestartAsync(crash: false, options: new TableBlockOptions { MinFreeDiskMb = 10 });
            _server.Job.FreeBytes = () => 5L << 20;

            // The merge of hour 20 faults on the disk, and the blocks of hour 0, merged and past their delay, are deleted.
            var low = Pass(Hour0.AddHours(26));
            Assert.Contains("free disk", low.Fault);
            Assert.Equal(1, low.HoursDeleted);
            Assert.DoesNotContain(_server.Blocks.GetBlocks(_product), e => e.ArrivalHourTicks == Hour0.Ticks);
            Assert.False(_server.Batches.IsHourMerged(_product, Hour0.AddHours(20).Ticks));

            _server.Job.FreeBytes = () => 50L << 20;
            Assert.Equal(1, Pass(Hour0.AddHours(26).AddMinutes(1)).HoursMerged);
            Assert.Equal([1.0, 2.0], _server.Service.Read(a.Id, From, Hour0.AddDays(2)).Value.Columns[1].DoubleValues);
        }

        [Fact]
        public async Task A_product_whose_merge_keeps_faulting_does_not_stop_the_merge_of_another()
        {
            var other = (await _server.Cache.AddProductAsync($"p{Guid.NewGuid():N}"[..10], Guid.Empty)).Id;
            var a = await RegisterAsync("a");
            var o = await RegisterAsync("o", other);

            foreach (var hour in new[] { Hour0, Hour0.AddHours(1) })
            {
                _now = hour.AddMinutes(10);
                AssertStored(Put(Block(("a", [(hour.AddMinutes(1), 1.0, 1)]))));
                AssertStored(_server.Service.Put(other, _producer, hour.Hour, Block(("o", [(hour.AddMinutes(2), 2.0, 2)]))));
            }

            var before = All(a, o);

            // Every merge of the first product faults (an I/O error while its batches are built).
            _server.Job.AfterDecode = (sensor, _) =>
            {
                if (sensor == a.Id)
                    throw new IOException("its week database cannot be opened");
            };

            var first = Pass(Hour0.AddHours(2).AddMinutes(3));
            Assert.NotNull(first.Fault);
            Assert.True(_server.Batches.IsHourMerged(other, Hour0.Ticks));
            Assert.True(_server.Batches.IsHourMerged(other, Hour0.AddHours(1).Ticks));
            Assert.False(_server.Batches.IsHourMerged(_product, Hour0.Ticks));

            // The fault repeats; nothing of the first product is committed and the reads are the same.
            Assert.NotNull(Pass(Hour0.AddHours(2).AddMinutes(4)).Fault);
            Assert.False(_server.Batches.IsHourMerged(_product, Hour0.Ticks));
            Assert.Equal(before, All(a, o));

            _server.Job.AfterDecode = null;
            Assert.Equal(2, Pass(Hour0.AddHours(2).AddMinutes(5)).HoursMerged);
            Assert.Equal(before, All(a, o));
        }

        [Fact]
        public async Task An_error_while_the_merge_reads_a_block_is_a_fault_and_the_next_pass_merges_the_hour_whole()
        {
            var a = await RegisterAsync("a");
            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("a", [(Hour0.AddMinutes(1), 1.0, 1)]))));
            var failing = Put(Block(("a", [(Hour0.AddMinutes(2), 2.0, 2)]))).Sequence.Value;
            var before = Rows(a);

            var thrown = 0;
            _server.Blocks.BeforeBlockGet = sequence =>
            {
                if (sequence == failing && thrown++ == 0)
                    throw new Exception("IO error: 000005.ldb: the disk did not answer");
            };

            var faulted = Pass(Hour0.AddHours(1).AddMinutes(3));
            Assert.NotNull(faulted.Fault);
            Assert.False(_server.Batches.IsHourMerged(_product, Hour0.Ticks));
            Assert.Equal(0, _server.Batches.MergeStatistics.UnreadableBlocksKept);

            Assert.Equal(1, Pass(Hour0.AddHours(1).AddMinutes(4)).HoursMerged);
            Assert.Empty(_server.Batches.GetHour(_product, Hour0.Ticks).UnreadableBlocks);
            Assert.Equal(before, Rows(a));
        }

        [Fact]
        public async Task A_catch_up_merge_leaves_out_the_rows_whose_period_has_passed_and_commits_the_others()
        {
            var a = await RegisterAsync("a");
            var b = await RegisterAsync("b");
            Assert.True((await _server.Cache.UpdateSensorAsync(new SensorUpdate { Id = a.Id, KeepHistory = new TimeIntervalModel(TimeSpan.FromDays(49).Ticks) })).IsOk);
            Assert.True((await _server.Cache.UpdateSensorAsync(new SensorUpdate { Id = b.Id, KeepHistory = new TimeIntervalModel(TimeSpan.FromDays(7).Ticks) })).IsOk);

            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("a", [(Hour0.AddMinutes(1), 1.0, 1), (Hour0.AddMinutes(2), 2.0, 2)]), ("b", [(Hour0.AddMinutes(3), 3.0, 3)]))));
            var before = Rows(a);

            // Merged 15 days later: the week of the hour plus one week (b) has passed, plus seven weeks (a) has not.
            var week = HSMDatabase.AccessManager.StorageWeek.GetStartTicks(Hour0.Ticks);
            var late = Hour0.AddDays(15);
            Assert.True(TableBatchRetention.IsExpired(week, 1, late));
            Assert.False(TableBatchRetention.IsExpired(week, 7, late));

            var report = Pass(late);
            Assert.Null(report.Fault);
            Assert.Equal(1, report.HoursMerged);

            var hour = _server.Batches.GetHour(_product, Hour0.Ticks);
            Assert.Equal([7], hour.Classes.Select(c => c.PeriodWeeks));
            Assert.Equal(1, _server.Batches.MergeStatistics.RowsExpired);
            Assert.Equal(2, _server.Batches.MergeStatistics.RowsMerged);
            Assert.Equal(before, Rows(a));
            Assert.Empty(Rows(b));
        }

        [Fact]
        public async Task A_corruption_error_while_the_merge_reads_a_block_is_damage_and_the_next_hour_merges_in_the_same_pass()
        {
            var a = await RegisterAsync("a");
            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("a", [(Hour0.AddMinutes(1), 1.0, 1)]))));
            var corrupt = Put(Block(("a", [(Hour0.AddMinutes(2), 2.0, 2)]))).Sequence.Value;
            _now = Hour0.AddHours(1).AddMinutes(10);
            AssertStored(Put(Block(("a", [(Hour0.AddHours(1).AddMinutes(1), 3.0, 3)]))));

            // Every read of the block fails with a corruption status: permanent, so retrying it would hold the product back.
            _server.Blocks.BeforeBlockGet = sequence =>
            {
                if (sequence == corrupt)
                    throw new Exception("Corruption: block checksum mismatch: 000005.ldb");
            };

            var report = Pass(Hour0.AddHours(2).AddMinutes(3));
            Assert.Null(report.Fault);
            Assert.Equal(2, report.HoursMerged);
            Assert.Equal([corrupt], _server.Batches.GetHour(_product, Hour0.Ticks).UnreadableBlocks);
            Assert.True(_server.Batches.IsHourMerged(_product, Hour0.AddHours(1).Ticks));
            Assert.Equal(1, _server.Batches.MergeStatistics.UnreadableBlocksKept);
            Assert.Equal([1.0, 3.0], _server.Service.Read(a.Id, From, To).Value.Columns[1].DoubleValues);
        }

        [Fact]
        public async Task A_product_whose_batches_cannot_be_opened_faults_its_own_puts_and_merges_only()
        {
            var other = (await _server.Cache.AddProductAsync($"p{Guid.NewGuid():N}"[..10], Guid.Empty)).Id;
            var a = await RegisterAsync("a");
            var o = await RegisterAsync("o", other);

            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("a", [(Hour0.AddMinutes(1), 1.0, 1)]))));
            AssertStored(_server.Service.Put(other, _producer, 1, Block(("o", [(Hour0.AddMinutes(2), 2.0, 2)]))));
            Assert.Equal(1, Pass(Hour0.AddHours(1).AddMinutes(3)).HoursMerged);

            await RestartAsync(crash: false);

            // The first product's merged hours are held open by another process (its LOCK file).
            var hours = Path.Combine(_server.Database.DatabaseSettings.PathToTableBlocks, TableBatchDatabase.FolderName, _product.ToString("N"), TableBatchDatabase.HoursName);
            using (new global::LevelDB.DB(hours, new global::LevelDB.Options()))
            {
                _now = Hour0.AddHours(2).AddMinutes(10);
                Assert.Throws<TableBlockStoreException>(() => Put(Block(("a", [(Hour0.AddHours(2), 3.0, 3)]))));
                AssertStored(_server.Service.Put(other, _producer, 2, Block(("o", [(Hour0.AddHours(2), 4.0, 4)]))));

                Assert.NotNull(Pass(Hour0.AddHours(3).AddMinutes(3)).Fault);
                Assert.True(_server.Batches.IsHourMerged(other, Hour0.AddHours(2).Ticks));
            }

            // Released: the same service takes the first product's blocks again.
            AssertStored(Put(Block(("a", [(Hour0.AddHours(2), 3.0, 3)]))));
            Assert.Equal([2.0, 4.0], _server.Service.Read(o.Id, From, To).Value.Columns[1].DoubleValues);
            Assert.Equal([1.0, 3.0], _server.Service.Read(a.Id, From, To).Value.Columns[1].DoubleValues);
        }

        // === Sensors ===

        [Fact]
        public async Task A_sensor_removed_and_created_again_between_the_put_and_the_merge_never_sees_the_old_rows()
        {
            var old = await RegisterAsync("q");
            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("q", [(Hour0.AddMinutes(1), 1.0, 1), (Hour0.AddMinutes(2), 2.0, 2)]))));

            await _server.Cache.RemoveSensorAsync(old.Id);
            var renewed = await RegisterAsync("q");
            AssertStored(Put(Block(("q", [(Hour0.AddMinutes(3), 3.0, 3)]))));

            Assert.Equal([3.0], _server.Service.Read(renewed.Id, From, To).Value.Columns[1].DoubleValues);

            Assert.Equal(1, Pass(Hour0.AddHours(1).AddMinutes(3)).HoursMerged);

            Assert.Equal([3.0], _server.Service.Read(renewed.Id, From, To).Value.Columns[1].DoubleValues);
            Assert.Equal(2, _server.Batches.MergeStatistics.RowsDropped);
            Assert.Equal(1, _server.Batches.MergeStatistics.RowsMerged);
            Assert.Empty(_server.Batches.ReadSensor(_product, old.Id, TablePeriodClass.Of(renewed, DateTime.UtcNow), [Hour0.Ticks]));
        }

        [Fact]
        public async Task Nulls_a_large_hour_a_single_row_and_rows_out_of_order_read_back_the_same_after_the_merge()
        {
            var nulls = await RegisterAsync("nulls");
            var large = await RegisterAsync("large");
            var single = await RegisterAsync("single");

            var random = new Random(1850);
            _now = Hour0.AddMinutes(5);

            for (int block = 0; block < 3; block++)
            {
                // 1,700 rows a block in shuffled times (equal times too), a null in some cells.
                var rows = Enumerable.Range(0, 1700).Select(i =>
                    (Hour0.AddSeconds(random.Next(0, 3000)), (double?)(random.Next(10_000) / 100.0), (long?)random.Next(100))).ToArray();
                var nullRows = Enumerable.Range(0, 40).Select(i => (Hour0.AddSeconds(random.Next(0, 3000)), i % 2 == 0 ? (double?)null : 1.5, i % 3 == 0 ? (long?)null : 7)).ToArray();

                AssertStored(Put(Block(("large", rows), ("nulls", nullRows))));
                _now = _now.AddMinutes(5);
            }

            AssertStored(Put(Block(("single", [(Hour0.AddMinutes(30), null, null)]))));

            var before = All(nulls, large, single);
            Assert.Equal(5100, Rows(large).Count);
            Assert.Contains(Rows(nulls), r => r.Item2 is null);

            Assert.Equal(1, Pass(Hour0.AddHours(1).AddMinutes(3)).HoursMerged);
            Assert.Equal(before, All(nulls, large, single));

            // 5,100 rows: two batches of 2,550, in time order.
            var batches = _server.Batches.ReadSensor(_product, large.Id, TablePeriodClass.Of(large, DateTime.UtcNow), [Hour0.Ticks]);
            Assert.Equal([2550, 2550], batches.Select(r => r.RowCount));
            Assert.True(batches[0].LastTime <= batches[1].FirstTime);
            Assert.Equal(5100 + 120 + 1, _server.Batches.MergeStatistics.RowsMerged);
        }

        // === Watermarks and the deletion delay ===

        [Fact]
        public async Task A_resend_of_a_merged_and_deleted_block_is_a_duplicate_after_a_restart_per_product_and_no_block_lands_in_a_merged_hour()
        {
            var a = await RegisterAsync("a");
            var other = (await _server.Cache.AddProductAsync($"p{Guid.NewGuid():N}"[..10], Guid.Empty)).Id;
            await RegisterAsync("a", other);

            _now = Hour0.AddMinutes(10);
            for (long s = 1; s <= 3; s++)
                AssertStored(_server.Service.Put(_product, _producer, s, Block(("a", [(Hour0.AddMinutes(s), s, s)]))));

            Pass(Hour0.AddHours(1).AddMinutes(3));
            Assert.Equal(1, Pass(Hour0.AddHours(26)).HoursDeleted);
            Assert.Empty(_server.Blocks.GetBlocks(_product));

            await RestartAsync(crash: false);

            // The clock is behind: without the newest merged hour closed, the block would go into hour 0, already merged.
            _now = Hour0.AddMinutes(20);
            var resent = _server.Service.Put(_product, _producer, 2, Block(("a", [(Hour0.AddMinutes(2), 2.0, 2)])));
            Assert.Equal(TableBlockPutOutcome.Duplicate, resent.Outcome);
            Assert.False(resent.RefusalsKnown);
            Assert.Null(resent.AcceptedTables);
            Assert.Equal(1, _server.Blocks.Statistics.MergedDuplicateBlocks);

            // The same producer and sequence under another product is that product's block.
            AssertStored(_server.Service.Put(other, _producer, 2, Block(("a", [(Hour0.AddMinutes(2), 2.0, 2)]))));

            var next = _server.Service.Put(_product, _producer, 4, Block(("a", [(Hour0.AddMinutes(4), 4.0, 4)])));
            AssertStored(next);
            Assert.Equal(Hour0.AddHours(1).Ticks, _server.Blocks.GetBlocks(_product).Single().ArrivalHourTicks);

            Assert.Equal([1.0, 2.0, 3.0, 4.0], _server.Service.Read(a.Id, From, To).Value.Columns[1].DoubleValues);
        }

        [Fact]
        public async Task The_blocks_of_a_merged_hour_stay_until_the_delay_after_its_commit_also_across_a_restart()
        {
            var a = await RegisterAsync("a");
            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("a", [(Hour0.AddMinutes(1), 1.0, 1)]))));

            // Merged late (a catch-up): the delay runs from the commit, not from the end of the hour.
            var commit = Hour0.AddHours(10);
            Assert.Equal(1, Pass(commit).HoursMerged);
            Assert.Equal(commit.Ticks, _server.Batches.GetHour(_product, Hour0.Ticks).CommitTicks);

            await RestartAsync(crash: false);

            var holding = Pass(commit.AddHours(23));
            Assert.Equal(0, holding.HoursDeleted);
            Assert.Single(_server.Blocks.GetBlocks(_product));
            Assert.Equal(1, _server.Batches.MergeStatistics.MergedHoursHoldingBlocks);
            Assert.Equal([1.0], _server.Service.Read(a.Id, From, To).Value.Columns[1].DoubleValues);

            Assert.Equal(1, Pass(commit.AddHours(24)).HoursDeleted);
            Assert.Empty(_server.Blocks.GetBlocks(_product));
            Assert.Equal(0, _server.Batches.MergeStatistics.MergedHoursHoldingBlocks);
            Assert.Equal([1.0], _server.Service.Read(a.Id, From, To).Value.Columns[1].DoubleValues);
        }

        // === The row limit ===

        [Fact]
        public async Task The_row_limit_returns_the_unlimited_result_cut_at_the_limit_across_overlapping_hours()
        {
            var a = await RegisterAsync("a");

            // Four arrival hours whose rows overlap in time, with equal times across them; the first two merged.
            for (int h = 0; h < 4; h++)
            {
                _now = Hour0.AddHours(h).AddMinutes(10);
                AssertStored(Put(Block(("a", Enumerable.Range(0, 6).Select(i => (Hour0.AddMinutes(h * 20 + i * 15), (double?)(h * 100 + i), (long?)i)).ToArray()))));
            }

            Pass(Hour0.AddHours(2).AddMinutes(3));
            Assert.True(_server.Batches.IsHourMerged(_product, Hour0.AddHours(1).Ticks));
            Assert.False(_server.Batches.IsHourMerged(_product, Hour0.AddHours(2).Ticks));

            var all = Rows(a);
            Assert.Equal(24, all.Count);

            // Cut at the limit, then back to a time boundary (#1845): the rows of the time of the first row left out are left
            // out too, and the next page starts at that time; one time with more rows than the limit is cut at the limit.
            for (int limit = 1; limit <= 25; limit++)
            {
                var limited = _server.Service.Read(a.Id, From, To, limit).Value;
                long? next = limit < all.Count ? all[limit].Item1 : null;
                var keep = limit;

                while (next is not null && keep > 0 && all[keep - 1].Item1 == next)
                    keep--;

                if (keep == 0)
                    keep = limit;

                Assert.Equal(all.Take(keep), Snapshot(limited));
                Assert.Equal(limit < 24, limited.Truncated);

                if (next is not null && keep < limit)
                    Assert.Equal(next, limited.NextFromTicks);
            }
        }

        [Fact]
        public async Task The_row_limit_stops_before_the_hours_that_start_after_the_rows_it_keeps()
        {
            var a = await RegisterAsync("a");

            for (int h = 0; h < 6; h++)
            {
                _now = Hour0.AddHours(h).AddMinutes(10);
                AssertStored(Put(Block(("a", Enumerable.Range(0, 10).Select(i => (Hour0.AddHours(h).AddMinutes(i), (double?)(h * 10 + i), (long?)i)).ToArray()))));
            }

            Pass(Hour0.AddHours(3).AddMinutes(3));

            // 15 rows: hour 0 (merged) gives 10, hour 1 (merged) the rest; hours 2 to 5 are not read.
            var limited = _server.Service.Read(a.Id, From, To, 15).Value;
            Assert.Equal(Rows(a).Take(15), Snapshot(limited));
            Assert.True(limited.Truncated);
            Assert.Equal(2, limited.BatchesRead);
            Assert.Equal(0, limited.BlocksRead);

            var fromBlocks = _server.Service.Read(a.Id, Hour0.AddHours(3), To, 15).Value;
            Assert.Equal(2, fromBlocks.BlocksRead);
            Assert.True(fromBlocks.Truncated);
        }

        // === The memory budget ===

        [Fact]
        public async Task A_budget_that_forces_several_passes_writes_the_same_bytes_as_one_pass()
        {
            var sensors = new List<BaseSensorModel>();
            for (int s = 0; s < 5; s++)
                sensors.Add(await RegisterAsync($"s{s}"));

            var random = new Random(7);
            for (int block = 0; block < 4; block++)
            {
                _now = Hour0.AddMinutes(5 + block);
                AssertStored(Put(Block(Enumerable.Range(0, 5).Select(s => ($"s{s}", Enumerable.Range(0, 100 * (s + 1)).Select(i =>
                    (Hour0.AddSeconds(random.Next(3600)), random.Next(4) == 0 ? (double?)null : random.Next(1000) / 10.0, (long?)i)).ToArray())).ToArray())));
            }

            var before = sensors.Select(Rows).ToList();
            var hour = Hour0.AddHours(1).AddMinutes(3);

            // About a third of the hour's rows per share.
            _server.Job.MemoryBudgetBytes = 1200L * TableMergeJob.RowBytes(Quotes.Columns.Count);
            Assert.Equal(1, Pass(hour).HoursMerged);
            Assert.True(_server.Job.LastPasses > 2, $"{_server.Job.LastPasses} passes");
            var several = Stored(sensors);
            Assert.Equal(before, sensors.Select(Rows).ToList());

            // The same hour merged again in one pass: the batch store removed, the blocks still there (the delay).
            await RestartAsync(crash: false, beforeOpen: settings => Directory.Delete(Path.Combine(settings.PathToTableBlocks, TableBatchDatabase.FolderName), recursive: true));
            Assert.Equal(1, Pass(hour).HoursMerged);
            Assert.Equal(1, _server.Job.LastPasses);

            Assert.Equal(several, Stored(sensors));
            Assert.Equal(before, sensors.Select(Rows).ToList());
        }

        [Fact]
        public async Task A_wide_schema_is_split_where_a_narrow_one_with_as_many_rows_fits_one_pass()
        {
            const int RowsPerTable = 250;
            var wideSchema = new TableSchema([new TableSchemaColumn("time", TableColumnType.Time),
                .. Enumerable.Range(1, 39).Select(c => new TableSchemaColumn($"c{c}", TableColumnType.Double))]);

            var wide = new List<BaseSensorModel>();
            var narrow = new List<BaseSensorModel>();
            for (int s = 0; s < 4; s++)
            {
                wide.Add(await RegisterAsync($"w{s}", schema: wideSchema));
                narrow.Add(await RegisterAsync($"n{s}"));
            }

            var random = new Random(11);
            (DateTime, double?, long?)[] NarrowRows() => Enumerable.Range(0, RowsPerTable)
                .Select(i => (Hour0.AddSeconds(random.Next(3600)), (double?)random.Next(1000), (long?)i)).ToArray();

            // Hour 0: the wide tables; hour 1: as many rows in narrow tables.
            _now = Hour0.AddMinutes(5);
            AssertStored(Put(WideBlock(wideSchema, Enumerable.Range(0, 4).Select(s => $"w{s}").ToArray(), RowsPerTable, random)));
            _now = Hour0.AddMinutes(65);
            AssertStored(Put(Block(Enumerable.Range(0, 4).Select(s => ($"n{s}", NarrowRows())).ToArray())));

            // The narrow hour's weight exactly, so the narrow hour is one pass; the wide one is not and is split by table.
            _server.Job.MemoryBudgetBytes = Enumerable.Range(0, 4)
                .Sum(s => TableMergeJob.TableBytes(RowsPerTable, Quotes.Columns.Count, 0, Encoding.UTF8.GetByteCount($"{_root}/n{s}")));

            Assert.Equal(1, Pass(Hour0.AddHours(1).AddMinutes(3)).HoursMerged);
            Assert.Equal(4, _server.Job.LastPasses);
            var split = Stored(wide);
            Assert.Equal(4, split.Count);

            Assert.Equal(1, Pass(Hour0.AddHours(2).AddMinutes(3)).HoursMerged);
            Assert.Equal(1, _server.Job.LastPasses);

            // The wide hour merged again in one pass (the default budget): the batch store removed, the blocks still there.
            await RestartAsync(crash: false, beforeOpen: settings => Directory.Delete(Path.Combine(settings.PathToTableBlocks, TableBatchDatabase.FolderName), recursive: true));
            Assert.Equal(1, Pass(Hour0.AddHours(1).AddMinutes(3)).HoursMerged);
            Assert.Equal(1, _server.Job.LastPasses);

            Assert.Equal(split, Stored(wide));
        }


        [Fact]
        public async Task Many_one_row_tables_are_weighed_by_what_each_table_allocates_and_split()
        {
            // 48 sensors, one row each in each of 6 blocks: about what 10-second blocks of an hour hold of a quiet table.
            var sensors = new List<BaseSensorModel>();
            for (int s = 0; s < 48; s++)
                sensors.Add(await RegisterAsync($"t{s}"));

            for (int block = 0; block < 6; block++)
            {
                _now = Hour0.AddMinutes(5 + block);
                AssertStored(Put(Block(Enumerable.Range(0, 48).Select(s => ($"t{s}", new[] { (Hour0.AddSeconds(block * 10 + s), (double?)s, (long?)block) })).ToArray())));
            }

            // By the rows alone (45 bytes a row at 3 columns) the 288 rows fit four times over; by what the tables allocate they do not.
            var budget = 4 * 288 * TableMergeJob.RowBytes(Quotes.Columns.Count);
            Assert.True(288 * TableMergeJob.TableBytes(1, Quotes.Columns.Count, 0, Encoding.UTF8.GetByteCount($"{_root}/t0")) > budget);
            _server.Job.MemoryBudgetBytes = budget;

            var hour = Hour0.AddHours(1).AddMinutes(3);
            Assert.Equal(1, Pass(hour).HoursMerged);
            Assert.True(_server.Job.LastPasses > 1, $"{_server.Job.LastPasses} passes");
            var split = Stored(sensors);

            // The same hour in one pass: the same batches.
            await RestartAsync(crash: false, beforeOpen: settings => Directory.Delete(Path.Combine(settings.PathToTableBlocks, TableBatchDatabase.FolderName), recursive: true));
            Assert.Equal(1, Pass(hour).HoursMerged);
            Assert.Equal(1, _server.Job.LastPasses);
            Assert.Equal(split, Stored(sensors));
        }

        [Fact]
        public async Task A_single_pass_given_up_at_the_budget_counts_dropped_expired_and_unreadable_rows_once_as_one_pass_does()
        {
            var kept = await RegisterAsync("k");
            var expiring = await RegisterAsync("e");
            var removed = await RegisterAsync("r");
            var wideSchema = new TableSchema([new TableSchemaColumn("time", TableColumnType.Time),
                .. Enumerable.Range(1, 39).Select(c => new TableSchemaColumn($"c{c}", TableColumnType.Double))]);
            var wide = await RegisterAsync("w", schema: wideSchema);

            foreach (var (sensor, days) in new[] { (kept, 49), (expiring, 7), (wide, 49) })
                Assert.True((await _server.Cache.UpdateSensorAsync(new SensorUpdate { Id = sensor.Id, KeepHistory = new TimeIntervalModel(TimeSpan.FromDays(days).Ticks) })).IsOk);

            // Block 1: rows to keep, to drop (their sensor is removed and created again) and expired at the merge; block 2
            // unreadable; block 3 the wide table, which passes the budget after both were met.
            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("k", [(Hour0.AddMinutes(1), 1.0, 1)]), ("r", [(Hour0.AddMinutes(1), 2.0, 2), (Hour0.AddMinutes(2), 3.0, 3)]),
                ("e", [(Hour0.AddMinutes(3), 4.0, 4)]))));
            var corrupt = Put(Block(("k", [(Hour0.AddMinutes(4), 5.0, 5)]))).Sequence.Value;
            AssertStored(Put(WideBlock(wideSchema, ["w"], 250, new Random(5))));

            await _server.Cache.RemoveSensorAsync(removed.Id);
            await RegisterAsync("r");

            void Corrupt() => _server.Blocks.BeforeBlockGet = sequence =>
            {
                if (sequence == corrupt)
                    throw new Exception("Corruption: block checksum mismatch: 000005.ldb");
            };

            // Merged after the expiring sensor's period has passed for the week of the hour.
            var late = Hour0.AddDays(15);
            Corrupt();

            // The hour fits by its rows at the narrowest weight, not by the wide table: the single pass is tried and given up.
            var rows = 1 + 2 + 1 + 1 + 250;
            var budget = 20_000L;
            Assert.True(rows * TableMergeJob.RowBytes(1) <= budget);
            Assert.True(250 * TableMergeJob.RowBytes(wideSchema.Columns.Count) > budget);
            _server.Job.MemoryBudgetBytes = budget;

            Assert.Equal(1, Pass(late).HoursMerged);
            Assert.True(_server.Job.LastPasses > 1, $"{_server.Job.LastPasses} passes");
            var split = Counters();
            var splitBatches = Stored([kept, wide]);

            // The same hour in one pass under the default budget.
            await RestartAsync(crash: false, beforeOpen: settings => Directory.Delete(Path.Combine(settings.PathToTableBlocks, TableBatchDatabase.FolderName), recursive: true));
            Corrupt();
            Assert.Equal(1, Pass(late).HoursMerged);
            Assert.Equal(1, _server.Job.LastPasses);

            Assert.Equal((2L, 1L, 1L), (split.Dropped, split.Expired, split.Unreadable));
            Assert.Equal([corrupt], split.List);
            var single = Counters();
            Assert.Equal((split.Dropped, split.Expired, split.Unreadable), (single.Dropped, single.Expired, single.Unreadable));
            Assert.Equal(split.List, single.List);
            Assert.Equal(splitBatches, Stored([kept, wide]));

            (long Dropped, long Expired, long Unreadable, IReadOnlyList<long> List) Counters()
            {
                var stats = _server.Batches.MergeStatistics;
                return (stats.RowsDropped, stats.RowsExpired, stats.UnreadableBlocksKept, _server.Batches.GetHour(_product, Hour0.Ticks).UnreadableBlocks);
            }
        }

        // === The period class ===

        public static TheoryData<TimeIntervalModel, int> PeriodClasses => new()
        {
            { new TimeIntervalModel(TimeInterval.Month), 5 },
            { new TimeIntervalModel(TimeInterval.ThreeMonths), 14 },
            { new TimeIntervalModel(TimeInterval.SixMonths), 27 },
            { new TimeIntervalModel(TimeInterval.Year), 53 },
            { new TimeIntervalModel(TimeInterval.None), 0 },
            { TimeIntervalModel.None, 0 },
            { new TimeIntervalModel(TimeSpan.FromDays(7).Ticks), 1 },
            { new TimeIntervalModel(TimeSpan.FromDays(10).Ticks), 2 },
            { new TimeIntervalModel(TimeSpan.FromHours(1).Ticks), 1 },
            { new TimeIntervalModel(TimeSpan.FromDays(14).Ticks), 2 },
            { new TimeIntervalModel(TimeSpan.FromDays(400).Ticks), 58 },
            { new TimeIntervalModel(long.MaxValue), 0 },
            { new TimeIntervalModel(TimeInterval.Ticks, 0), 0 },
            // Inherited from a folder: a custom period keeps its ticks, a calendar preset is inherited as itself and counts
            // as the preset does (#1864); one stored by an earlier build without its preset (0 ticks) is kept for ever, as
            // the cleanup of ordinary history keeps it; Forever is long.MaxValue.
            { new TimeIntervalModel(TimeSpan.FromDays(30).Ticks).ToFromFolderModel(), 5 },
            { new TimeIntervalModel(TimeInterval.Month).ToFromFolderModel(), 5 },
            { new TimeIntervalModel(TimeInterval.ThreeMonths).ToFromFolderModel(), 14 },
            { new TimeIntervalModel(TimeInterval.SixMonths).ToFromFolderModel(), 27 },
            { new TimeIntervalModel(TimeInterval.Year).ToFromFolderModel(), 53 },
            { new TimeIntervalModel(TimeInterval.FromFolder, 0L), 0 },
            { TimeIntervalModel.None.ToFromFolderModel(), 0 },
            { new TimeIntervalModel(TimeInterval.FromParent), 0 },
        };

        [Theory]
        [MemberData(nameof(PeriodClasses))]
        public void The_period_class_is_the_history_setting_in_whole_weeks_rounded_up_or_0_for_ever(TimeIntervalModel policy, int weeks)
        {
            Assert.Equal(weeks, TablePeriodClass.Weeks(policy, Hour0));
        }

        [Fact]
        public async Task A_sensor_with_its_own_period_is_merged_into_the_database_of_its_class()
        {
            var (a, b) = await SeedAsync();
            Pass(Hour0.AddHours(1).AddMinutes(3));

            Assert.Equal(1, TablePeriodClass.Of(b, DateTime.UtcNow));
            Assert.NotEqual(1, TablePeriodClass.Of(a, DateTime.UtcNow));
            Assert.Contains(new TableBatchWeek(HSMDatabase.AccessManager.StorageWeek.GetStartTicks(Hour0.Ticks), 1), _server.Batches.GetWeeks(_product));
            Assert.Equal(2, _server.Batches.GetHour(_product, Hour0.Ticks).Classes.Count);
        }


        // === Retention (#1850 part 4) ===

        private static readonly long Week = 7 * TimeSpan.TicksPerDay;
        private static readonly long WeekStart = HSMDatabase.AccessManager.StorageWeek.GetStartTicks(Hour0.Ticks);

        [Fact]
        public async Task Retention_drops_a_week_database_once_its_class_has_passed_for_the_week_and_never_one_kept_for_ever()
        {
            var a = await RegisterAsync("a", keep: TimeIntervalModel.None);
            var b = await RegisterAsync("b", keep: new TimeIntervalModel(TimeSpan.FromDays(7).Ticks));
            var c = await RegisterAsync("c", keep: new TimeIntervalModel(TimeSpan.FromDays(35).Ticks));

            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("a", [(Hour0.AddMinutes(1), 1.0, 1)]), ("b", [(Hour0.AddMinutes(2), 2.0, 2)]), ("c", [(Hour0.AddMinutes(3), 3.0, 3)]))));
            Assert.Equal(1, Pass(Hour0.AddHours(1).AddMinutes(3)).HoursMerged);
            Assert.Equal([0, 1, 5], _server.Batches.GetWeeks(_product).Select(w => w.PeriodWeeks));
            var (rowsA, rowsC) = (Rows(a), Rows(c));
            Assert.Single(Rows(b));

            // P1: the week's end plus one week. A tick before it nothing is dropped.
            var p1 = new DateTime(WeekStart + 2 * Week, DateTimeKind.Utc);
            Assert.Equal(0, Pass(p1.AddTicks(-1)).WeeksDropped);
            Assert.Single(Rows(b));

            // At it, a drop that faults is counted and tried again in the next hour, not in the same one.
            _server.Job.RetentionCheckpoint = p =>
            {
                if (p == "week-dropping")
                    throw new IOException("the folder is held");
            };
            var faulted = Pass(p1);
            Assert.NotNull(faulted.Fault);
            Assert.Equal(1, _server.Batches.MergeStatistics.RetentionFaults);
            _server.Job.RetentionCheckpoint = null;
            Assert.Equal(0, Pass(p1.AddMinutes(1)).WeeksDropped);
            Assert.Single(Rows(b));

            Assert.Equal(1, Pass(p1.AddHours(1)).WeeksDropped);
            Assert.Equal([0, 5], _server.Batches.GetWeeks(_product).Select(w => w.PeriodWeeks));
            Assert.Equal(1, _server.Batches.Statistics.WeeksDropped);
            Assert.Empty(Rows(b));
            Assert.Equal(rowsA, Rows(a));
            Assert.Equal(rowsC, Rows(c));

            // P5 at the week's end plus five weeks.
            var p5 = new DateTime(WeekStart + 6 * Week, DateTimeKind.Utc);
            Assert.Equal(0, Pass(p5.AddTicks(-1)).WeeksDropped);
            Assert.Equal(rowsC, Rows(c));
            Assert.Equal(1, Pass(p5).WeeksDropped);
            Assert.Empty(Rows(c));

            // P0 is kept for ever.
            Assert.Equal(0, Pass(Hour0.AddYears(10)).WeeksDropped);
            Assert.Equal([0], _server.Batches.GetWeeks(_product).Select(w => w.PeriodWeeks));
            Assert.Equal(rowsA, Rows(a));
        }

        [Fact]
        public async Task Blocks_never_merged_are_deleted_once_every_sensor_with_rows_in_them_has_passed_and_never_while_merely_late()
        {
            var b = await RegisterAsync("b", keep: new TimeIntervalModel(TimeSpan.FromDays(7).Ticks));
            var c = await RegisterAsync("c", keep: new TimeIntervalModel(TimeSpan.FromDays(35).Ticks));

            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("b", [(Hour0.AddMinutes(1), 1.0, 1), (Hour0.AddMinutes(2), 2.0, 2)]))));
            AssertStored(Put(Block(("b", [(Hour0.AddMinutes(3), 3.0, 3)]), ("c", [(Hour0.AddMinutes(4), 4.0, 4)]))));

            // The hour does not verify: not committed, held on its blocks (not tried again in this process).
            _server.Job.AfterDecode = (_, batch) => batch.Columns[1].DoubleValues[0] += 0.5;
            Assert.Equal(1, Pass(Hour0.AddHours(1).AddMinutes(3)).Mismatches);
            Assert.False(_server.Batches.IsHourMerged(_product, Hour0.Ticks));

            // Late, not expired: before the week's end plus a week nothing is even looked at.
            Assert.Equal(0, Pass(Hour0.AddDays(5)).ExpiredHoursDeleted);
            Assert.Equal(0, Pass(new DateTime(WeekStart + 2 * Week - 1, DateTimeKind.Utc)).ExpiredHoursDeleted);
            Assert.Equal(2, _server.Blocks.GetBlocks(_product).Count);

            // b's period has passed, c's (rows in the second block) has not: the hour stays whole.
            Assert.Equal(0, Pass(new DateTime(WeekStart + 2 * Week, DateTimeKind.Utc)).ExpiredHoursDeleted);
            Assert.Equal(2, _server.Blocks.GetBlocks(_product).Count);
            Assert.Equal(3, Rows(b).Count);

            Assert.Equal(0, Pass(new DateTime(WeekStart + 6 * Week - 1, DateTimeKind.Utc)).ExpiredHoursDeleted);
            var report = Pass(new DateTime(WeekStart + 6 * Week, DateTimeKind.Utc));
            Assert.Null(report.Fault);
            Assert.Equal(1, report.ExpiredHoursDeleted);
            Assert.Empty(_server.Blocks.GetBlocks(_product));
            Assert.Equal(2, _server.Batches.MergeStatistics.ExpiredBlocksDeleted);
            Assert.Equal(4, _server.Batches.MergeStatistics.ExpiredBlockRowsDeleted);
            Assert.Empty(Rows(b));
            Assert.Empty(Rows(c));
        }

        [Fact]
        public async Task An_hour_never_merged_with_rows_of_a_sensor_kept_for_ever_is_never_deleted()
        {
            var a = await RegisterAsync("a", keep: TimeIntervalModel.None);
            var b = await RegisterAsync("b", keep: new TimeIntervalModel(TimeSpan.FromDays(7).Ticks));

            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("a", [(Hour0.AddMinutes(1), 1.0, 1)]), ("b", [(Hour0.AddMinutes(2), 2.0, 2)]))));
            var before = All(a, b);

            _server.Job.AfterDecode = (_, batch) => batch.Columns[1].DoubleValues[0] += 0.5;
            Assert.Equal(1, Pass(Hour0.AddHours(1).AddMinutes(3)).Mismatches);

            Assert.Equal(0, Pass(Hour0.AddYears(10)).ExpiredHoursDeleted);
            Assert.Single(_server.Blocks.GetBlocks(_product));
            Assert.Equal(before, All(a, b));
        }

        [Fact]
        public async Task An_unreadable_block_is_judged_by_every_table_sensor_of_its_product()
        {
            var other = (await _server.Cache.AddProductAsync($"p{Guid.NewGuid():N}"[..10], Guid.Empty)).Id;
            var a = await RegisterAsync("a", other, keep: new TimeIntervalModel(TimeSpan.FromDays(7).Ticks));
            var z = await RegisterAsync("z", other, keep: TimeIntervalModel.None);

            _now = Hour0.AddMinutes(10);
            AssertStored(_server.Service.Put(other, _producer, 1, Block(("a", [(Hour0.AddMinutes(1), 1.0, 1)]))));
            var damaged = _server.Service.Put(other, _producer, 2, Block(("a", [(Hour0.AddMinutes(2), 2.0, 2)]))).Sequence.Value;

            await RestartAsync(crash: false, beforeOpen: settings =>
            {
                using var db = new global::LevelDB.DB(Path.Combine(settings.PathToTableBlocks, TableBlockDatabase.BlocksName), new global::LevelDB.Options());
                var key = TableBlockDatabase.Key(Hour0.Ticks, damaged);
                var value = db.Get(key);
                value[^1] ^= 0x55;
                db.Put(key, value, new global::LevelDB.WriteOptions { Sync = true });
            });

            Assert.Equal(1, Pass(Hour0.AddHours(1).AddMinutes(3)).HoursMerged);
            Assert.Equal([damaged], _server.Batches.GetHour(other, Hour0.Ticks).UnreadableBlocks);
            Pass(Hour0.AddHours(30));
            Assert.Equal([damaged], _server.Blocks.GetBlocks(other).Select(e => e.Sequence));

            // Its directory cannot be read, so it may hold rows of z, which is kept for ever: it stays.
            var p1 = new DateTime(WeekStart + 2 * Week, DateTimeKind.Utc);
            Assert.Equal(0, Pass(p1).ExpiredHoursDeleted);
            Assert.Single(_server.Blocks.GetBlocks(other));

            // Once every table sensor of the product has a period that has passed, it goes.
            Assert.True((await _server.Cache.UpdateSensorAsync(new SensorUpdate { Id = z.Id, KeepHistory = new TimeIntervalModel(TimeSpan.FromDays(7).Ticks) })).IsOk);
            // Its week database was dropped at p1 (a's class); the hour stays committed while its block is held (#1850 part
            // 4b), so the merge never takes it again: the block is listed as unreadable once, and goes at the next hour.
            Assert.True(_server.Batches.IsHourMerged(other, Hour0.Ticks));
            Assert.Equal(1, Pass(p1.AddHours(1)).ExpiredHoursDeleted);
            Assert.Equal(1, _server.Batches.MergeStatistics.UnreadableBlocksKept);
            Assert.Empty(_server.Blocks.GetBlocks(other));

            // With neither a database of its week nor a block left, the record of the hour is forgotten in the same run.
            Assert.False(_server.Batches.IsHourMerged(other, Hour0.Ticks));
            Assert.Empty(_server.Batches.GetHoursWithoutDatabases(other));
            Assert.Equal(1, _server.Batches.MergeStatistics.ExpiredBlocksDeleted);
            Assert.Equal(1, _server.Batches.MergeStatistics.ExpiredBlockRowsDeleted);
            Assert.Equal(0, _server.Batches.MergeStatistics.RetentionFaults);
            Assert.Equal(0, _server.Service.Read(a.Id, From, To).Value.RowCount);
        }


        // === Placements (#1850 part 4b) ===

        [Fact]
        public async Task A_change_of_period_takes_effect_from_the_hour_after_every_hour_with_blocks_and_earlier_hours_keep_their_class()
        {
            // What holds while the rows wait for the move (#1850 part 4c): the move held off.
            HoldMoves();

            var b = await RegisterAsync("b", keep: new TimeIntervalModel(TimeSpan.FromDays(7).Ticks));

            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("b", [(Hour0.AddMinutes(1), 1.0, 1)]))));
            Pass(Hour0.AddHours(1).AddMinutes(3));
            Assert.Equal(new TablePlacement(1, Hour0.Ticks), _server.Batches.GetPlacement(_product, b.Id));

            // The period changes while hour 1 has blocks: the change takes effect from hour 2.
            _now = Hour0.AddMinutes(70);
            AssertStored(Put(Block(("b", [(Hour0.AddMinutes(61), 2.0, 2)]))));
            Assert.True((await _server.Cache.UpdateSensorAsync(new SensorUpdate { Id = b.Id, KeepHistory = new TimeIntervalModel(TimeSpan.FromDays(35).Ticks) })).IsOk);
            Pass(Hour0.AddHours(2).AddMinutes(3));

            var placement = _server.Batches.GetPlacement(_product, b.Id);
            Assert.Equal(new TablePlacement(5, Hour0.Ticks, [new TablePlacementSegment(Hour0.Ticks, Hour0.AddHours(2).Ticks, 1)]), placement);
            Assert.Equal([1], _server.Batches.GetHour(_product, Hour0.AddHours(1).Ticks).Classes.Select(c => c.PeriodWeeks));

            _now = Hour0.AddMinutes(130);
            AssertStored(Put(Block(("b", [(Hour0.AddMinutes(121), 3.0, 3)]))));
            Pass(Hour0.AddHours(3).AddMinutes(3));
            Assert.Equal([5], _server.Batches.GetHour(_product, Hour0.AddHours(2).Ticks).Classes.Select(c => c.PeriodWeeks));

            // The placement survives a restart, and a pass with the setting unchanged changes nothing.
            await RestartAsync(crash: false);
            Pass(Hour0.AddHours(4));
            Assert.Equal(placement, _server.Batches.GetPlacement(_product, b.Id));
        }

        [Fact]
        public async Task A_change_of_period_while_the_open_hour_already_holds_blocks_takes_effect_from_the_hour_after_it()
        {
            // What holds while the rows wait for the move (#1850 part 4c): the move held off.
            HoldMoves();

            var b = await RegisterAsync("b", keep: new TimeIntervalModel(TimeSpan.FromDays(7).Ticks));

            // The hour before hour 0 is merged, so the sensor has its placement.
            _now = Hour0.AddMinutes(-50);
            AssertStored(Put(Block(("b", [(Hour0.AddMinutes(-51), 1.0, 1)]))));
            Assert.Equal(1, Pass(Hour0.AddMinutes(3)).HoursMerged);
            Assert.Equal(new TablePlacement(1, Hour0.AddHours(-1).Ticks), _server.Batches.GetPlacement(_product, b.Id));

            // Five minutes into hour 0 a block has arrived in it, and the period changes. The pass runs while hour 0 is
            // still open (it merges nothing of it).
            _now = Hour0.AddMinutes(5);
            AssertStored(Put(Block(("b", [(Hour0.AddMinutes(4), 2.0, 2)]))));
            Assert.True((await _server.Cache.UpdateSensorAsync(new SensorUpdate { Id = b.Id, KeepHistory = new TimeIntervalModel(TimeSpan.FromDays(35).Ticks) })).IsOk);
            Assert.Equal(0, Pass(Hour0.AddMinutes(6)).HoursMerged);
            Assert.False(_server.Batches.IsHourMerged(_product, Hour0.Ticks));

            // The cut-off is the hour after the open hour: the whole of hour 0 stays in the old class.
            var placement = _server.Batches.GetPlacement(_product, b.Id);
            Assert.Equal(new TablePlacement(5, Hour0.AddHours(-1).Ticks, [new TablePlacementSegment(Hour0.AddHours(-1).Ticks, Hour0.AddHours(1).Ticks, 1)]), placement);
            Assert.Equal((1, 5), (placement.ClassOf(Hour0.Ticks), placement.ClassOf(Hour0.AddHours(1).Ticks)));

            // Also the rows that arrive in hour 0 after the change: the hour is merged whole into P1, the next one into P5.
            _now = Hour0.AddMinutes(50);
            AssertStored(Put(Block(("b", [(Hour0.AddMinutes(49), 3.0, 3)]))));
            _now = Hour0.AddMinutes(70);
            AssertStored(Put(Block(("b", [(Hour0.AddMinutes(69), 4.0, 4)]))));
            Pass(Hour0.AddHours(2).AddMinutes(3));

            Assert.Equal([1], _server.Batches.GetHour(_product, Hour0.Ticks).Classes.Select(c => c.PeriodWeeks));
            Assert.Equal(2L, _server.Batches.GetHour(_product, Hour0.Ticks).Classes[0].Rows);
            Assert.Equal([5], _server.Batches.GetHour(_product, Hour0.AddHours(1).Ticks).Classes.Select(c => c.PeriodWeeks));
            Assert.Equal(placement, _server.Batches.GetPlacement(_product, b.Id));
            Assert.Equal(4, Rows(b).Count);
        }

        [Fact]
        public async Task A_repeat_of_an_hour_that_did_not_commit_gives_every_sensor_the_class_of_the_run_before_a_change_of_period()
        {
            // What holds while the rows wait for the move (#1850 part 4c): the move held off.
            HoldMoves();

            var a = await RegisterAsync("a", keep: TimeIntervalModel.None);
            var b = await RegisterAsync("b", keep: new TimeIntervalModel(TimeSpan.FromDays(7).Ticks));

            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("a", [(Hour0.AddMinutes(1), 1.0, 1)]), ("b", [(Hour0.AddMinutes(2), 2.0, 2)]))));
            var before = All(a, b);

            // The first run writes the P0 part and stops before the P1 part.
            _server.Batches.Checkpoint = p =>
            {
                if (p == "part-written")
                    throw new IOException("crash");
            };
            Assert.NotNull(Pass(Hour0.AddHours(1).AddMinutes(3)).Fault);
            _server.Batches.Checkpoint = null;
            Assert.False(_server.Batches.IsHourMerged(_product, Hour0.Ticks));

            // b's period changes before the repeat: the repeat still writes b into P1, as the first run did.
            Assert.True((await _server.Cache.UpdateSensorAsync(new SensorUpdate { Id = b.Id, KeepHistory = new TimeIntervalModel(TimeSpan.FromDays(35).Ticks) })).IsOk);
            Assert.Equal(1, Pass(Hour0.AddHours(1).AddMinutes(4)).HoursMerged);

            Assert.Equal([0, 1], _server.Batches.GetHour(_product, Hour0.Ticks).Classes.Select(c => c.PeriodWeeks));
            Assert.Equal(5, _server.Batches.GetPlacement(_product, b.Id).Target);
            Assert.Equal(1, _server.Batches.GetPlacement(_product, b.Id).ClassOf(Hour0.Ticks));
            Assert.Equal(before, All(a, b));
        }

        [Fact]
        public async Task A_change_on_the_product_is_recorded_for_every_sensor_that_inherits_it_within_a_pass()
        {
            // What holds while the rows wait for the move (#1850 part 4c): the move held off.
            HoldMoves();

            var sensors = new List<BaseSensorModel>();
            foreach (var name in new[] { "x", "y", "z" })
                sensors.Add(await RegisterAsync(name));
            var own = await RegisterAsync("own", keep: TimeIntervalModel.None);

            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("x", [(Hour0.AddMinutes(1), 1.0, 1)]), ("y", [(Hour0.AddMinutes(1), 1.0, 1)]), ("z", [(Hour0.AddMinutes(1), 1.0, 1)]), ("own", [(Hour0.AddMinutes(1), 1.0, 1)]))));
            Pass(Hour0.AddHours(1).AddMinutes(3));
            var inherited = TablePeriodClass.Of((TableSensorModel)sensors[0], Hour0);
            Assert.NotEqual(1, inherited);

            Assert.True((await _server.Cache.UpdateProductAsync(new ProductUpdate { Id = _product, KeepHistory = new TimeIntervalModel(TimeSpan.FromDays(7).Ticks) })).IsOk);

            // No new rows: the next pass records the change for the three sensors that inherit it, from the next hour.
            Pass(Hour0.AddHours(2));
            foreach (var sensor in sensors)
                Assert.Equal(new TablePlacement(1, Hour0.Ticks, [new TablePlacementSegment(Hour0.Ticks, Hour0.AddHours(2).Ticks, inherited)]), _server.Batches.GetPlacement(_product, sensor.Id));
            Assert.Equal(new TablePlacement(0, Hour0.Ticks), _server.Batches.GetPlacement(_product, own.Id));
        }

        [Fact]
        public async Task Retention_keeps_a_week_database_whose_rows_are_owed_to_a_longer_period_and_drops_it_once_that_has_passed_too()
        {
            // What holds while the rows wait for the move (#1850 part 4c): the move held off.
            HoldMoves();

            var b = await RegisterAsync("b", keep: new TimeIntervalModel(TimeSpan.FromDays(7).Ticks));

            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("b", [(Hour0.AddMinutes(1), 1.0, 1)]))));
            Pass(Hour0.AddHours(1).AddMinutes(3));

            // Lengthened to five weeks: the P1 database of the week holds rows the sensor keeps for five weeks now.
            Assert.True((await _server.Cache.UpdateSensorAsync(new SensorUpdate { Id = b.Id, KeepHistory = new TimeIntervalModel(TimeSpan.FromDays(35).Ticks) })).IsOk);
            Pass(Hour0.AddHours(2));

            var p1 = new DateTime(WeekStart + 2 * Week, DateTimeKind.Utc);
            Assert.Equal(0, Pass(p1).WeeksDropped);
            Assert.Contains(new TableBatchWeek(WeekStart, 1), _server.Batches.GetWeeks(_product));
            Assert.Equal(1, _server.Batches.MergeStatistics.MoveWeeksPending);

            var p5 = new DateTime(WeekStart + 6 * Week, DateTimeKind.Utc);
            Assert.Equal(0, Pass(p5.AddTicks(-1)).WeeksDropped);
            Assert.Equal(1, Pass(p5).WeeksDropped);
            Assert.DoesNotContain(new TableBatchWeek(WeekStart, 1), _server.Batches.GetWeeks(_product));
            Assert.Equal(0, _server.Batches.MergeStatistics.MoveWeeksPending);
        }

        [Fact]
        public async Task A_segment_starts_at_the_sensors_first_hour_so_a_sensor_keeps_no_week_from_before_it_existed()
        {
            // What holds while the rows wait for the move (#1850 part 4c): the move held off.
            HoldMoves();

            // The review's sequence. Last week's P1 database holds only sensor a.
            var a = await RegisterAsync("a", keep: new TimeIntervalModel(TimeSpan.FromDays(7).Ticks));
            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("a", [(Hour0.AddMinutes(1), 1.0, 1)]))));
            Pass(Hour0.AddHours(1).AddMinutes(3));
            Assert.Equal([new TableBatchWeek(WeekStart, 1)], _server.Batches.GetWeeks(_product));

            // Sensor b is created in P1 a week later, merges its first batch, and is then kept for ever.
            var later = Hour0.AddDays(7);
            var b = await RegisterAsync("b", keep: new TimeIntervalModel(TimeSpan.FromDays(7).Ticks));
            _now = later.AddMinutes(10);
            AssertStored(Put(Block(("b", [(later.AddMinutes(1), 2.0, 2)]))));
            Pass(later.AddHours(1).AddMinutes(3));
            Assert.Equal(new TablePlacement(1, later.Ticks), _server.Batches.GetPlacement(_product, b.Id));

            Assert.True((await _server.Cache.UpdateSensorAsync(new SensorUpdate { Id = b.Id, KeepHistory = TimeIntervalModel.None })).IsOk);
            Pass(later.AddHours(2));

            // b's hours in P1 are the ones from its first hour to the cut-off: nothing of last week.
            var placement = _server.Batches.GetPlacement(_product, b.Id);
            Assert.Equal(new TablePlacement(0, later.Ticks, [new TablePlacementSegment(later.Ticks, later.AddHours(2).Ticks, 1)]), placement);

            // Last week's P1 database goes when its period has passed: b never had a row in it. This week's P1 database
            // holds b's rows, owed to P0, and is kept.
            var p1 = new DateTime(WeekStart + 2 * Week, DateTimeKind.Utc);
            Assert.Equal(1, Pass(p1).WeeksDropped);
            Assert.Equal([new TableBatchWeek(WeekStart + Week, 1)], _server.Batches.GetWeeks(_product));
            Assert.Equal(0, _server.Batches.MergeStatistics.MoveWeeksPending);

            Assert.Equal(0, Pass(p1.AddDays(7)).WeeksDropped);
            Assert.Equal(1, _server.Batches.MergeStatistics.MoveWeeksPending);
            Assert.Equal(1, _server.Service.Read(b.Id, later.AddHours(-1), later.AddHours(1)).Value.RowCount);
            Assert.Empty(Rows(a));
        }

        [Fact]
        public async Task The_first_hour_of_a_placement_is_the_oldest_hour_with_blocks_not_committed_when_it_was_created()
        {
            var a = await RegisterAsync("a", keep: new TimeIntervalModel(TimeSpan.FromDays(7).Ticks));
            var b = await RegisterAsync("b", keep: new TimeIntervalModel(TimeSpan.FromDays(7).Ticks));

            // Hour 0 (rows of a) does not verify and is left on its blocks; hour 1 (rows of b) is merged first.
            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("a", [(Hour0.AddMinutes(1), 1.0, 1)]))));
            _server.Job.AfterDecode = (sensor, batch) =>
            {
                if (sensor == a.Id)
                    batch.Columns[1].DoubleValues[0] += 0.5;
            };
            Assert.Equal(1, Pass(Hour0.AddHours(1).AddMinutes(3)).Mismatches);

            _now = Hour0.AddMinutes(70);
            AssertStored(Put(Block(("b", [(Hour0.AddMinutes(61), 2.0, 2)]))));
            Pass(Hour0.AddHours(2).AddMinutes(3));
            Assert.True(_server.Batches.IsHourMerged(_product, Hour0.AddHours(1).Ticks));
            Assert.False(_server.Batches.IsHourMerged(_product, Hour0.Ticks));

            // b's placement was created while hour 0 was still to be merged: its first hour is hour 0, not hour 1.
            Assert.Equal(new TablePlacement(1, Hour0.Ticks), _server.Batches.GetPlacement(_product, b.Id));
        }

        [Fact]
        public async Task A_period_lengthened_before_its_week_expired_protects_an_hour_still_to_be_merged_and_a_block_kept_unreadable()
        {
            // What holds while the rows wait for the move (#1850 part 4c): the move held off.
            HoldMoves();

            // The review's sequence. A sensor kept for one week; hour 0 is committed with one block the merge could not
            // read (kept), hour 1 holds readable rows and keeps faulting in its merge.
            var b = await RegisterAsync("b", keep: new TimeIntervalModel(TimeSpan.FromDays(7).Ticks));

            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("b", [(Hour0.AddMinutes(1), 1.0, 1)]))));
            var damaged = Put(Block(("b", [(Hour0.AddMinutes(2), 2.0, 2)]))).Sequence.Value;
            _server.Blocks.BeforeBlockGet = sequence =>
            {
                if (sequence == damaged)
                    throw new Exception("Corruption: block checksum mismatch: 000005.ldb");
            };
            Assert.Equal(1, Pass(Hour0.AddHours(1).AddMinutes(3)).HoursMerged);
            _server.Blocks.BeforeBlockGet = null;
            Assert.Equal([damaged], _server.Batches.GetHour(_product, Hour0.Ticks).UnreadableBlocks);

            _now = Hour0.AddMinutes(70);
            AssertStored(Put(Block(("b", [(Hour0.AddMinutes(61), 3.0, 3)]))));
            _server.Job.Checkpoint = p =>
            {
                if (p == "encoded")
                    throw new IOException("the merge of hour 1 faults");
            };
            Assert.NotNull(Pass(Hour0.AddHours(2).AddMinutes(3)).Fault);
            Assert.False(_server.Batches.IsHourMerged(_product, Hour0.AddHours(1).Ticks));

            // Before the week expires the period is lengthened to 52 weeks. Hours 0 and 1 are before the cut-off: the class
            // that holds them stays P1, the sensor's period is P52.
            Assert.True((await _server.Cache.UpdateSensorAsync(new SensorUpdate { Id = b.Id, KeepHistory = new TimeIntervalModel(TimeSpan.FromDays(364).Ticks) })).IsOk);
            Pass(Hour0.AddHours(3));
            var placement = _server.Batches.GetPlacement(_product, b.Id);
            Assert.Equal((52, 1, 1), (placement.Target, placement.ClassOf(Hour0.Ticks), placement.ClassOf(Hour0.AddHours(1).Ticks)));

            // P1's time for the week has passed when the merge of hour 1 gets through, and retention runs in the same pass.
            _server.Job.Checkpoint = null;
            var p1 = new DateTime(WeekStart + 2 * Week, DateTimeKind.Utc);
            var report = Pass(p1.AddHours(1));

            // Nothing that the sensor's period protects is gone: the rows of hour 1 are merged, into the class that holds
            // the hour, and not left out as expired; the block kept unreadable is still kept; the week database stays.
            Assert.Null(report.Fault);
            Assert.Equal(0, _server.Batches.MergeStatistics.RowsExpired);
            Assert.Equal([1], _server.Batches.GetHour(_product, Hour0.AddHours(1).Ticks).Classes.Select(c => c.PeriodWeeks));
            Assert.Equal((0, 0), (report.ExpiredHoursDeleted, report.WeeksDropped));
            Assert.Contains(_server.Blocks.GetBlocks(_product), e => e.Sequence == damaged);
            Assert.Equal([1.0, 3.0], _server.Service.Read(b.Id, From, To).Value.Columns[1].DoubleValues.Take(2));
            Assert.Equal(2, _server.Service.Read(b.Id, From, To).Value.RowCount);

            // Still so a tick before the sensor's own period has passed for the week; at it, the week and the block go.
            var p52 = new DateTime(WeekStart + 53 * Week, DateTimeKind.Utc);
            report = Pass(p52.AddTicks(-1));
            Assert.Equal((0, 0), (report.ExpiredHoursDeleted, report.WeeksDropped));
            Assert.Contains(_server.Blocks.GetBlocks(_product), e => e.Sequence == damaged);
            Assert.Equal(2, _server.Service.Read(b.Id, From, To).Value.RowCount);

            report = Pass(p52);
            Assert.Equal((1, 1), (report.ExpiredHoursDeleted, report.WeeksDropped));
            Assert.Empty(_server.Blocks.GetBlocks(_product));
            Assert.Equal(0, _server.Service.Read(b.Id, From, To).Value.RowCount);
        }

        [Fact]
        public async Task A_period_shortened_deletes_nothing_before_the_class_that_holds_the_rows_has_passed_while_the_move_has_not_run()
        {
            // Retention and the rule of expiry on their own, before any move: once the move runs, its expired step deletes
            // rows whose new class has passed before the old one has (TableBatchRetention.IsExpired, "The move").
            // What holds while the rows wait for the move (#1850 part 4c): the move held off.
            HoldMoves();

            // Kept for five weeks, then for one: an hour still to be merged and before the cut-off is held by P5.
            var b = await RegisterAsync("b", keep: new TimeIntervalModel(TimeSpan.FromDays(35).Ticks));

            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("b", [(Hour0.AddMinutes(1), 1.0, 1)]))));
            Pass(Hour0.AddHours(1).AddMinutes(3));

            _now = Hour0.AddMinutes(70);
            AssertStored(Put(Block(("b", [(Hour0.AddMinutes(61), 2.0, 2)]))));
            _server.Job.Checkpoint = p =>
            {
                if (p == "encoded")
                    throw new IOException("the merge of hour 1 faults");
            };
            Assert.NotNull(Pass(Hour0.AddHours(2).AddMinutes(3)).Fault);

            Assert.True((await _server.Cache.UpdateSensorAsync(new SensorUpdate { Id = b.Id, KeepHistory = new TimeIntervalModel(TimeSpan.FromDays(7).Ticks) })).IsOk);
            Pass(Hour0.AddHours(3));
            Assert.Equal((1, 5), (_server.Batches.GetPlacement(_product, b.Id).Target, _server.Batches.GetPlacement(_product, b.Id).ClassOf(Hour0.AddHours(1).Ticks)));

            // The new period (one week) has passed for the week, the class that holds the hour (five weeks) has not: the
            // rows are merged into the P5 database and kept, the blocks are not taken for expired.
            _server.Job.Checkpoint = null;
            var report = Pass(new DateTime(WeekStart + 2 * Week, DateTimeKind.Utc).AddHours(1));
            Assert.Null(report.Fault);
            Assert.Equal(0, _server.Batches.MergeStatistics.RowsExpired);
            Assert.Equal((0, 0), (report.ExpiredHoursDeleted, report.WeeksDropped));
            Assert.Equal(2, _server.Service.Read(b.Id, From, To).Value.RowCount);

            // The P5 database goes with its own period.
            Assert.Equal(1, Pass(new DateTime(WeekStart + 6 * Week, DateTimeKind.Utc)).WeeksDropped);
        }

        [Fact]
        public async Task Rows_whose_week_database_was_dropped_before_the_period_was_lengthened_are_left_out_and_the_hour_commits()
        {
            // Two sensors: b kept for one week, c for five. Hour 0 is merged; hour 1 holds rows of both and keeps faulting.
            var b = await RegisterAsync("b", keep: new TimeIntervalModel(TimeSpan.FromDays(7).Ticks));
            var c = await RegisterAsync("c", keep: new TimeIntervalModel(TimeSpan.FromDays(35).Ticks));

            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("b", [(Hour0.AddMinutes(1), 1.0, 1)]), ("c", [(Hour0.AddMinutes(1), 1.0, 1)]))));
            Pass(Hour0.AddHours(1).AddMinutes(3));

            _now = Hour0.AddMinutes(70);
            AssertStored(Put(Block(("b", [(Hour0.AddMinutes(61), 2.0, 2)]), ("c", [(Hour0.AddMinutes(61), 2.0, 2)]))));
            _server.Job.Checkpoint = p =>
            {
                if (p == "encoded")
                    throw new IOException("the merge of hour 1 faults");
            };
            Assert.NotNull(Pass(Hour0.AddHours(2).AddMinutes(3)).Fault);

            // b's period passes for the week while it is still one week: its P1 database is dropped (c's P5 one stays, so
            // the drop stays on the record). The block of hour 1 stays: c's rows in it have not expired.
            var p1 = new DateTime(WeekStart + 2 * Week, DateTimeKind.Utc);
            Assert.Equal(1, Pass(p1).WeeksDropped);
            Assert.True(_server.Batches.IsWeekDropped(_product, new TableBatchWeek(WeekStart, 1)));
            Assert.Single(_server.Blocks.GetBlocks(_product), e => e.ArrivalHourTicks == Hour0.AddHours(1).Ticks);

            // Only then b's period is lengthened. Its rows of hour 1 would go into the dropped database: they are left
            // out, counted as expired, and the hour commits with c's rows instead of faulting on the refused write for ever.
            Assert.True((await _server.Cache.UpdateSensorAsync(new SensorUpdate { Id = b.Id, KeepHistory = new TimeIntervalModel(TimeSpan.FromDays(364).Ticks) })).IsOk);
            _server.Job.Checkpoint = null;
            var report = Pass(p1.AddHours(1));
            Assert.Null(report.Fault);
            Assert.True(_server.Batches.IsHourMerged(_product, Hour0.AddHours(1).Ticks));
            Assert.Equal([5], _server.Batches.GetHour(_product, Hour0.AddHours(1).Ticks).Classes.Select(x => x.PeriodWeeks));
            Assert.Equal(1, _server.Batches.MergeStatistics.RowsExpired);
            Assert.Equal(2, _server.Service.Read(c.Id, From, To).Value.RowCount);
            Assert.Equal(0, _server.Service.Read(b.Id, From, To).Value.RowCount);
        }

        [Fact]
        public async Task A_placement_write_that_fails_does_not_let_retention_drop_rows_the_setting_now_keeps()
        {
            // What holds while the rows wait for the move (#1850 part 4c): the move held off.
            HoldMoves();

            // The review's sequence. A sensor kept for one week, its hour merged into P1; a second product for comparison.
            var b = await RegisterAsync("b", keep: new TimeIntervalModel(TimeSpan.FromDays(7).Ticks));
            var other = (await _server.Cache.AddProductAsync($"p{Guid.NewGuid():N}"[..10], Guid.Empty)).Id;
            await RegisterAsync("o", other, keep: new TimeIntervalModel(TimeSpan.FromDays(7).Ticks));

            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("b", [(Hour0.AddMinutes(1), 1.0, 1)]))));
            AssertStored(_server.Service.Put(other, _producer, 1, Block(("o", [(Hour0.AddMinutes(1), 1.0, 1)]))));
            Assert.Equal(1, Pass(Hour0.AddHours(1).AddMinutes(3)).HoursMerged);
            Assert.Single(Rows(b));

            // Before the week expires the sensor is kept for ever. No pass runs until the one in which P1 has passed for
            // the week; in that pass the synced write of the changed placement fails once.
            Assert.True((await _server.Cache.UpdateSensorAsync(new SensorUpdate { Id = b.Id, KeepHistory = TimeIntervalModel.None })).IsOk);

            var failed = false;
            _server.Batches.Checkpoint = p =>
            {
                if (p == "placement-writing" && !failed)
                {
                    failed = true;
                    throw new IOException("the disk refused the write");
                }
            };

            var p1 = new DateTime(WeekStart + 2 * Week, DateTimeKind.Utc);
            var report = Pass(p1);
            Assert.True(failed);

            // The placement on record (P1) is stale, but retention also reads the sensor's setting (for ever): the only
            // database with the sensor's rows is still there. The other product had its retention.
            Assert.Contains(new TableBatchWeek(WeekStart, 1), _server.Batches.GetWeeks(_product));
            Assert.Single(Rows(b));
            Assert.Equal(new TablePlacement(1, Hour0.Ticks), _server.Batches.GetPlacement(_product, b.Id));
            Assert.Equal(1, report.WeeksDropped);
            Assert.Empty(_server.Batches.GetWeeks(other));

            // Counted and reported.
            Assert.NotNull(report.Fault);
            Assert.Equal(1, report.ProductsNotReconciled);
            Assert.Equal(1, _server.Batches.MergeStatistics.ProductsNotReconciled);

            // The next pass records the change; from then on the guard holds the P1 database for the rows owed to P0. The
            // retention of the product runs again an hour later and drops nothing.
            Assert.Equal(0, Pass(p1.AddMinutes(1)).ProductsNotReconciled);
            Assert.Equal(0, _server.Batches.GetPlacement(_product, b.Id).Target);

            Assert.Equal(0, Pass(p1.AddHours(1)).WeeksDropped);
            Assert.Equal(1, _server.Batches.MergeStatistics.MoveWeeksPending);
            Assert.Equal(0, Pass(Hour0.AddYears(3)).WeeksDropped);
            Assert.Single(Rows(b));
        }

        [Fact]
        public async Task A_product_whose_placements_were_not_reconciled_is_not_merged_and_its_blocks_are_judged_by_the_setting_now()
        {
            // Two sensors of one product: b kept for one week, c kept for ever. Hour 0 is merged (the placements exist).
            var b = await RegisterAsync("b", keep: new TimeIntervalModel(TimeSpan.FromDays(7).Ticks));
            await RegisterAsync("c", keep: TimeIntervalModel.None);

            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("b", [(Hour0.AddMinutes(1), 1.0, 1)]), ("c", [(Hour0.AddMinutes(1), 1.0, 1)]))));
            Assert.Equal(1, Pass(Hour0.AddHours(1).AddMinutes(3)).HoursMerged);

            // Hour 1 holds rows of b only and does not verify: it stays on its blocks, not tried again in this process.
            _now = Hour0.AddMinutes(70);
            AssertStored(Put(Block(("b", [(Hour0.AddMinutes(61), 2.0, 2)]))));
            _server.Job.AfterDecode = (_, batch) => batch.Columns[1].DoubleValues[0] += 0.5;
            Assert.Equal(1, Pass(Hour0.AddHours(2).AddMinutes(3)).Mismatches);
            _server.Job.AfterDecode = null;

            // b is kept for ever from now on; a later hour holds rows of c. In the pass in which P1 has passed for the
            // week, every write of a placement fails.
            Assert.True((await _server.Cache.UpdateSensorAsync(new SensorUpdate { Id = b.Id, KeepHistory = TimeIntervalModel.None })).IsOk);
            var p1 = new DateTime(WeekStart + 2 * Week, DateTimeKind.Utc);
            _now = p1.AddHours(-2).AddMinutes(10);
            AssertStored(Put(Block(("c", [(Hour0.AddMinutes(2), 3.0, 3)]))));

            _server.Batches.Checkpoint = p =>
            {
                if (p == "placement-writing")
                    throw new IOException("the disk refused the write");
            };

            var report = Pass(p1);
            Assert.Equal(1, report.ProductsNotReconciled);

            // Nothing of the product was merged (the hour of c's block waits), and the blocks of hour 1, which by the
            // placement on record (P1) have expired, are kept by b's setting now (for ever): they are still there.
            Assert.Equal((0, 0, 0), (report.HoursMerged, report.ExpiredHoursDeleted, report.WeeksDropped));
            Assert.False(_server.Batches.IsHourMerged(_product, p1.AddHours(-2).Ticks));
            Assert.Contains(_server.Blocks.GetBlocks(_product), e => e.ArrivalHourTicks == Hour0.AddHours(1).Ticks);
            Assert.Equal(0, _server.Batches.MergeStatistics.ExpiredBlocksDeleted);

            // With the write working again the next pass merges the waiting hour; the blocks of hour 1 are kept by the
            // sensor's period now (for ever), whenever retention looks.
            _server.Batches.Checkpoint = null;
            report = Pass(p1.AddMinutes(1));
            Assert.Equal((0, 1), (report.ProductsNotReconciled, report.HoursMerged));
            Assert.Equal(0, Pass(p1.AddHours(1)).ExpiredHoursDeleted);
            Assert.Contains(_server.Blocks.GetBlocks(_product), e => e.ArrivalHourTicks == Hour0.AddHours(1).Ticks);
            Assert.Equal(2, Rows(b).Count);
        }

        // Changes the sensor's period at the checkpoint after the reconciliation of the pass (once): a change the pass's
        // reconciliation did not see.
        private Action<string> ChangeAfterReconcile(BaseSensorModel sensor, TimeIntervalModel keep, Action changed)
        {
            var done = false;
            return p =>
            {
                if (p != "reconciled" || done)
                    return;

                done = true;
                Assert.True(Task.Run(() => _server.Cache.UpdateSensorAsync(new SensorUpdate { Id = sensor.Id, KeepHistory = keep })).GetAwaiter().GetResult().IsOk);
                changed();
            };
        }

        [Fact]
        public async Task A_period_lengthened_after_the_reconciliation_whose_write_fails_in_the_merge_keeps_its_week_database()
        {
            // What holds while the rows wait for the move (#1850 part 4c): the move held off.
            HoldMoves();

            // The review's first sequence. A sensor kept for one week, its hour 0 merged into the P1 database of the week.
            var s = await RegisterAsync("s", keep: new TimeIntervalModel(TimeSpan.FromDays(7).Ticks));

            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("s", [(Hour0.AddMinutes(1), 1.0, 1)]))));
            Assert.Equal(1, Pass(Hour0.AddHours(1).AddMinutes(3)).HoursMerged);

            // An hour with rows of s is due in the pass in which P1 passes for the week.
            var p1 = new DateTime(WeekStart + 2 * Week, DateTimeKind.Utc);
            _now = p1.AddMinutes(-50);
            AssertStored(Put(Block(("s", [(Hour0.AddMinutes(2), 2.0, 2)]))));

            // The pass reconciles s unchanged; right after, the user keeps s for ever. The merge of the due hour resolves s
            // and the write of its changed placement fails: the product's merge faults, it is not "not reconciled".
            var changed = false;
            _server.Job.Checkpoint = ChangeAfterReconcile(s, TimeIntervalModel.None, () => changed = true);
            _server.Batches.Checkpoint = p =>
            {
                if (p == "placement-writing")
                    throw new IOException("the disk refused the write");
            };

            var report = Pass(p1.AddMinutes(21));
            Assert.True(changed);
            Assert.NotNull(report.Fault);
            Assert.Equal(0, report.ProductsNotReconciled);
            Assert.False(_server.Batches.IsHourMerged(_product, p1.AddHours(-1).Ticks));
            Assert.Equal(new TablePlacement(1, Hour0.Ticks), _server.Batches.GetPlacement(_product, s.Id));

            // Retention ran with the stored placement (P1, no segment) and found s's setting kept for ever: the week database
            // with s's rows stays.
            Assert.Equal(0, report.WeeksDropped);
            Assert.Contains(new TableBatchWeek(WeekStart, 1), _server.Batches.GetWeeks(_product));
            Assert.Equal(2, Rows(s).Count);
            Assert.Equal(1, _server.Batches.MergeStatistics.MoveWeeksPending);

            // With the write working the change is recorded, the hour merged, and the database still kept years later.
            _server.Job.Checkpoint = null;
            _server.Batches.Checkpoint = null;
            Assert.Null(Pass(p1.AddMinutes(22)).Fault);
            Assert.Equal(0, _server.Batches.GetPlacement(_product, s.Id).Target);
            Assert.Equal(0, Pass(Hour0.AddYears(3)).WeeksDropped);
            Assert.Equal(2, Rows(s).Count);
        }

        [Fact]
        public async Task A_period_lengthened_after_the_reconciliation_of_an_inactive_sensor_keeps_its_week_database()
        {
            // What holds while the rows wait for the move (#1850 part 4c): the move held off.
            HoldMoves();

            // The review's second sequence: no write fails. A sensor kept for one week has rows in hour 0 only.
            var s = await RegisterAsync("s", keep: new TimeIntervalModel(TimeSpan.FromDays(7).Ticks));

            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("s", [(Hour0.AddMinutes(1), 1.0, 1)]))));
            Assert.Equal(1, Pass(Hour0.AddHours(1).AddMinutes(3)).HoursMerged);

            // In the pass in which P1 passes for the week, the user keeps s for ever right after the reconciliation. No hour
            // merged in the pass holds rows of s, so nothing resolves s again before retention.
            var changed = false;
            _server.Job.Checkpoint = ChangeAfterReconcile(s, TimeIntervalModel.None, () => changed = true);

            var p1 = new DateTime(WeekStart + 2 * Week, DateTimeKind.Utc);
            var report = Pass(p1);
            Assert.True(changed);
            Assert.Null(report.Fault);
            Assert.Equal(new TablePlacement(1, Hour0.Ticks), _server.Batches.GetPlacement(_product, s.Id));

            // The setting read by retention keeps the database.
            Assert.Equal(0, report.WeeksDropped);
            Assert.Contains(new TableBatchWeek(WeekStart, 1), _server.Batches.GetWeeks(_product));
            Assert.Single(Rows(s));

            // The next pass records the change; the guard holds the database from then on.
            _server.Job.Checkpoint = null;
            Pass(p1.AddMinutes(1));
            Assert.Equal(0, _server.Batches.GetPlacement(_product, s.Id).Target);
            Assert.Equal(0, Pass(Hour0.AddYears(3)).WeeksDropped);
            Assert.Single(Rows(s));
        }

        [Fact]
        public async Task With_every_placement_write_failing_retention_still_frees_what_has_expired_and_keeps_a_lengthened_period()
        {
            // The review's stall: u kept for one week (unchanged), l for two; hour 0 merged into W P1 (u) and W P2 (l).
            var u = await RegisterAsync("u", keep: new TimeIntervalModel(TimeSpan.FromDays(7).Ticks));
            var l = await RegisterAsync("l", keep: new TimeIntervalModel(TimeSpan.FromDays(14).Ticks));

            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("u", [(Hour0.AddMinutes(1), 1.0, 1)]), ("l", [(Hour0.AddMinutes(1), 1.0, 1)]))));
            Assert.Equal(1, Pass(Hour0.AddHours(1).AddMinutes(3)).HoursMerged);

            // Hour 1 holds rows of u only and does not verify: never merged in this process.
            _now = Hour0.AddMinutes(70);
            AssertStored(Put(Block(("u", [(Hour0.AddMinutes(61), 2.0, 2)]))));
            _server.Job.AfterDecode = (_, batch) => batch.Columns[1].DoubleValues[0] += 0.5;
            Assert.Equal(1, Pass(Hour0.AddHours(2).AddMinutes(3)).Mismatches);
            _server.Job.AfterDecode = null;

            // l is kept for ever from now on, and every write of a placement fails (the disk is full): the product is never
            // reconciled.
            Assert.True((await _server.Cache.UpdateSensorAsync(new SensorUpdate { Id = l.Id, KeepHistory = TimeIntervalModel.None })).IsOk);
            _server.Batches.Checkpoint = p =>
            {
                if (p == "placement-writing")
                    throw new IOException("There is not enough space on the disk.");
            };

            var faults = _server.Batches.MergeStatistics.Faults;
            var p2 = new DateTime(WeekStart + 3 * Week, DateTimeKind.Utc);
            var report = Pass(p2);
            Assert.Equal(1, report.ProductsNotReconciled);

            // Retention ran for the product all the same: u's expired week database is dropped and the expired blocks of
            // hour 1 are deleted (disk given back), while l's database is kept by its setting.
            Assert.Equal((1, 1), (report.WeeksDropped, report.ExpiredHoursDeleted));
            Assert.DoesNotContain(new TableBatchWeek(WeekStart, 1), _server.Batches.GetWeeks(_product));
            Assert.Contains(new TableBatchWeek(WeekStart, 2), _server.Batches.GetWeeks(_product));
            Assert.DoesNotContain(_server.Blocks.GetBlocks(_product), e => e.ArrivalHourTicks == Hour0.AddHours(1).Ticks);
            Assert.Single(Rows(l));
            Assert.Empty(Rows(u));

            // Counted and logged once per hour of the pass's clock, reported by every pass.
            Assert.Equal(1, _server.Batches.MergeStatistics.ProductsNotReconciled);
            Assert.Equal(faults + 1, _server.Batches.MergeStatistics.Faults);
            Assert.Equal(1, Pass(p2.AddMinutes(1)).ProductsNotReconciled);
            Assert.Equal(1, Pass(p2.AddMinutes(59)).ProductsNotReconciled);
            Assert.Equal(1, _server.Batches.MergeStatistics.ProductsNotReconciled);
            Assert.Equal(faults + 1, _server.Batches.MergeStatistics.Faults);
            Assert.Equal(0, Pass(p2.AddHours(1)).WeeksDropped);
            Assert.Equal(2, _server.Batches.MergeStatistics.ProductsNotReconciled);
            Assert.Single(Rows(l));
        }

        [Fact]
        public async Task A_shortened_period_frees_nothing_until_the_class_that_holds_the_rows_has_passed_while_the_move_has_not_run()
        {
            // Retention and the rule of expiry on their own, before any move: once the move runs, its expired step deletes
            // rows whose new class has passed before the old one has (TableBatchRetention.IsExpired, "The move").
            // What holds while the rows wait for the move (#1850 part 4c): the move held off.
            HoldMoves();

            // Kept for five weeks; hour 0 merged into W P5, hour 1 never merged (it does not verify).
            var s = await RegisterAsync("s", keep: new TimeIntervalModel(TimeSpan.FromDays(35).Ticks));

            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("s", [(Hour0.AddMinutes(1), 1.0, 1)]))));
            Assert.Equal(1, Pass(Hour0.AddHours(1).AddMinutes(3)).HoursMerged);

            _now = Hour0.AddMinutes(70);
            AssertStored(Put(Block(("s", [(Hour0.AddMinutes(61), 2.0, 2)]))));
            _server.Job.AfterDecode = (_, batch) => batch.Columns[1].DoubleValues[0] += 0.5;
            Assert.Equal(1, Pass(Hour0.AddHours(2).AddMinutes(3)).Mismatches);
            _server.Job.AfterDecode = null;

            // Shortened to one week while every placement write fails: the setting (P1) has passed for the week, the stored
            // placement (P5) has not. Nothing is freed.
            Assert.True((await _server.Cache.UpdateSensorAsync(new SensorUpdate { Id = s.Id, KeepHistory = new TimeIntervalModel(TimeSpan.FromDays(7).Ticks) })).IsOk);
            _server.Batches.Checkpoint = p =>
            {
                if (p == "placement-writing")
                    throw new IOException("the disk refused the write");
            };

            var p1 = new DateTime(WeekStart + 2 * Week, DateTimeKind.Utc);
            var report = Pass(p1);
            Assert.Equal(1, report.ProductsNotReconciled);
            Assert.Equal((0, 0), (report.WeeksDropped, report.ExpiredHoursDeleted));
            Assert.Contains(_server.Blocks.GetBlocks(_product), e => e.ArrivalHourTicks == Hour0.AddHours(1).Ticks);

            // Recorded: the target and the setting are P1, the class that holds hours 0 and 1 is P5. Still nothing is freed.
            _server.Batches.Checkpoint = null;
            Assert.Equal(0, Pass(p1.AddMinutes(1)).ProductsNotReconciled);
            var placement = _server.Batches.GetPlacement(_product, s.Id);
            Assert.Equal((1, 5), (placement.Target, placement.ClassOf(Hour0.AddHours(1).Ticks)));

            report = Pass(p1.AddHours(1));
            Assert.Equal((0, 0), (report.WeeksDropped, report.ExpiredHoursDeleted));
            Assert.Contains(_server.Blocks.GetBlocks(_product), e => e.ArrivalHourTicks == Hour0.AddHours(1).Ticks);
            Assert.Equal(2, Rows(s).Count);

            // Once P5 has passed for the week, the database and the blocks go.
            report = Pass(new DateTime(WeekStart + 6 * Week, DateTimeKind.Utc));
            Assert.Equal((1, 1), (report.WeeksDropped, report.ExpiredHoursDeleted));
            Assert.Empty(_server.Blocks.GetBlocks(_product));
        }

        [Fact]
        public async Task A_rebuilt_placement_covers_an_older_hour_merged_after_the_rebuild_and_a_later_change_keeps_it_where_it_was_merged()
        {
            // What holds while the rows wait for the move (#1850 part 4c): the move held off.
            HoldMoves();

            // The review's sequence. Hour 0 holds the first rows of s and does not verify; hour 1 is committed with s's first
            // batch.
            var s = await RegisterAsync("s", keep: new TimeIntervalModel(TimeSpan.FromDays(7).Ticks));

            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("s", [(Hour0.AddMinutes(1), 1.0, 1)]))));
            _server.Job.AfterDecode = (_, batch) => batch.Columns[1].DoubleValues[0] += 0.5;
            Assert.Equal(1, Pass(Hour0.AddHours(1).AddMinutes(3)).Mismatches);
            _server.Job.AfterDecode = null;

            _now = Hour0.AddMinutes(70);
            AssertStored(Put(Block(("s", [(Hour0.AddMinutes(61), 2.0, 2)]))));
            Assert.Equal(1, Pass(Hour0.AddHours(2).AddMinutes(3)).HoursMerged);
            Assert.False(_server.Batches.IsHourMerged(_product, Hour0.Ticks));

            // The placements are lost and rebuilt from the indexes: s's first hour is its first hour with a batch, hour 1.
            await RestartAsync(crash: false, beforeOpen: settings => Directory.Delete(Path.Combine(settings.PathToTableBlocks,
                TableBatchDatabase.FolderName, _product.ToString("N"), TableBatchDatabase.PlacementsName), recursive: true));
            Assert.Equal(new TablePlacement(1, Hour0.AddHours(1).Ticks), _server.Batches.GetPlacement(_product, s.Id));

            // After the restart hour 0 is merged, into the class that holds it (P1); its merge lowers the first hour to it.
            Assert.Equal(1, Pass(Hour0.AddHours(2).AddMinutes(4)).HoursMerged);
            Assert.True(_server.Batches.IsHourMerged(_product, Hour0.Ticks));
            Assert.Equal(new TablePlacement(1, Hour0.Ticks), _server.Batches.GetPlacement(_product, s.Id));

            // s is kept for ever from now on: the segment of the old class starts at hour 0, so hour 0 is looked for where it
            // was merged.
            Assert.True((await _server.Cache.UpdateSensorAsync(new SensorUpdate { Id = s.Id, KeepHistory = TimeIntervalModel.None })).IsOk);
            Pass(Hour0.AddHours(3));
            var placement = _server.Batches.GetPlacement(_product, s.Id);
            Assert.Equal((0, 1, 1), (placement.Target, placement.ClassOf(Hour0.Ticks), placement.ClassOf(Hour0.AddHours(1).Ticks)));
            Assert.Equal(2, Rows(s).Count);

            // And the P1 database of the week, which holds both hours, is kept past P1's time.
            Assert.Equal(0, Pass(new DateTime(WeekStart + 2 * Week, DateTimeKind.Utc)).WeeksDropped);
            Assert.Equal(2, Rows(s).Count);
        }

        [Fact]
        public async Task Retention_drops_a_week_by_its_class_when_the_period_was_shortened_while_the_move_has_not_run()
        {
            // Retention and the rule of expiry on their own, before any move: once the move runs, its expired step deletes
            // rows whose new class has passed before the old one has (TableBatchRetention.IsExpired, "The move").
            // What holds while the rows wait for the move (#1850 part 4c): the move held off.
            HoldMoves();

            var b = await RegisterAsync("b", keep: new TimeIntervalModel(TimeSpan.FromDays(35).Ticks));

            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("b", [(Hour0.AddMinutes(1), 1.0, 1)]))));
            Pass(Hour0.AddHours(1).AddMinutes(3));
            Assert.True((await _server.Cache.UpdateSensorAsync(new SensorUpdate { Id = b.Id, KeepHistory = new TimeIntervalModel(TimeSpan.FromDays(7).Ticks) })).IsOk);
            Pass(Hour0.AddHours(2));

            // Nothing holds the P5 database past its own period, and nothing drops it earlier (the move deletes the rows).
            Assert.Equal(0, Pass(new DateTime(WeekStart + 2 * Week, DateTimeKind.Utc)).WeeksDropped);
            Assert.Equal(1, Pass(new DateTime(WeekStart + 6 * Week, DateTimeKind.Utc)).WeeksDropped);
            Assert.Equal(0, _server.Batches.MergeStatistics.MoveWeeksPending);
        }


        // === Helpers ===

        private sealed record Server(IDatabaseCore Database, ITreeValuesCache Cache, TableBlockService Service, TableBlockDatabase Blocks, TableBatchDatabase Batches)
        {
            public TableMergeJob Job => Service.Merge;
        }

        // The steps of the move held off in every job of the test, restarts included (TableMergeJob.MoveStepsPerPass = 0).
        private bool _movesHeld;

        private void HoldMoves()
        {
            _movesHeld = true;
            _server.Job.MoveStepsPerPass = 0;
        }

        private Server Build(IDatabaseCore database, ITreeValuesCache cache, TableBlockOptions options = null)
        {
            var service = new TableBlockService(database, cache, options ?? new TableBlockOptions { MinFreeDiskMb = 0 });
            if (_movesHeld)
                service.Merge.MoveStepsPerPass = 0;
            var blocks = (TableBlockDatabase)database.TableBlocks;
            var batches = (TableBatchDatabase)database.TableBatches;

            blocks.Clock = () => _now;
            batches.Clock = () => _now;

            return new Server(database, cache, service, blocks, batches);
        }

        private TableMergePassReport Pass(DateTime now)
        {
            _now = now;
            return _server.Job.RunPass(now);
        }

        private void Crash(string point)
        {
            void Throw(string p)
            {
                if (p == point)
                    throw new IOException($"crash at {p}");
            }

            _server.Job.Checkpoint = Throw;
            _server.Batches.Checkpoint = Throw;
            _server.Blocks.FaultHook = Throw;
        }

        // A restart of the server over the same folders; with crash, the batch store stops without flushing its databases.
        private async Task RestartAsync(bool crash, Action<HSMDatabase.AccessManager.IDatabaseSettings> beforeOpen = null, TableBlockOptions options = null)
        {
            var settings = _server.Database.DatabaseSettings;

            if (crash)
                _server.Batches.CloseWithoutFlush();

            _server.Database.Dispose();
            beforeOpen?.Invoke(settings);

            var restart = ((HSMDatabase.Settings.DatabaseSettings)settings) with
            {
                ServerLayoutDatabaseName = settings.ServerLayoutDatabaseName + $"_r{++_restarts}",
                ChangeSetsDatabaseName = settings.ChangeSetsDatabaseName + $"_r{_restarts}",
                SnaphotsDatabaseName = settings.SnaphotsDatabaseName + $"_r{_restarts}",
            };

            var database = new HSMDatabase.DatabaseWorkCore.DatabaseCore(restart);
            var snapshot = new Moq.Mock<HSMServer.Core.TreeStateSnapshot.ITreeStateSnapshot>();
            snapshot.Setup(s => s.Keys).Returns(new HSMServer.Core.TreeStateSnapshot.StateCollection<
                HSMServer.Core.TreeStateSnapshot.States.LastKeyState, HSMDatabase.AccessManager.DatabaseEntities.SnapshotEntity.LastKeyStateEntity>());

            var cache = new TreeValuesCache(database, snapshot.Object, new HSMServer.Core.Journal.JournalService(database),
                new HSMServer.Core.Schedule.AlertScheduleProvider(database), new HSMServer.Core.ChangeSets.ChangeSetJournal(database));

            _restarted.Add(cache);
            _databases.Add(database);
            _server = Build(database, cache, options);

            await Task.CompletedTask;
        }

        // Sensor a (the product's period) and b (one week): hour 0 holds blocks 1 and 2, hour 1 a late block with rows of
        // hour 0, equal times across all three.
        private async Task<(BaseSensorModel A, BaseSensorModel B)> SeedAsync()
        {
            var a = await RegisterAsync("a");
            var b = await RegisterAsync("b");
            Assert.True((await _server.Cache.UpdateSensorAsync(new SensorUpdate { Id = b.Id, KeepHistory = new TimeIntervalModel(TimeSpan.FromDays(7).Ticks) })).IsOk);

            _now = Hour0.AddMinutes(10);
            AssertStored(Put(Block(("a", [(Hour0.AddMinutes(5), null, 10), (Hour0.AddMinutes(1), 2.0, 20)]), ("b", [(Hour0.AddMinutes(5), 1.0, 1)]))));
            _now = Hour0.AddMinutes(20);
            AssertStored(Put(Block(("a", [(Hour0.AddMinutes(9), 4.0, 40), (Hour0.AddMinutes(5), 3.0, 30)]), ("b", [(Hour0.AddMinutes(5), 2.0, 2)]))));
            _now = Hour0.AddMinutes(70);
            AssertStored(Put(Block(("a", [(Hour0.AddMinutes(65), 6.0, 60), (Hour0.AddMinutes(5), 5.0, null)]), ("b", [(Hour0.AddMinutes(5), 3.0, 3)]))));

            return (a, b);
        }

        private async Task<BaseSensorModel> RegisterAsync(string name, Guid? product = null, TableSchema schema = null, TimeIntervalModel keep = null)
        {
            var path = $"{_root}/{name}";
            var result = await _server.Cache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(product ?? _product, path)
            {
                Update = new SensorUpdate { Id = Guid.Empty },
                Type = SensorType.Table,
                TypeIsExplicit = true,
                Schema = schema ?? Quotes,
            });

            Assert.True(result.IsOk, result.Error);
            Assert.True(_server.Cache.TryGetSensorByPath(product ?? _product, path, out var sensor));

            if (keep is not null)
                Assert.True((await _server.Cache.UpdateSensorAsync(new SensorUpdate { Id = sensor.Id, KeepHistory = keep })).IsOk);

            return sensor;
        }

        private TableBlockPutResult Put(byte[] block) => _server.Service.Put(_product, _producer, ++_producerSequence, block);

        private static void AssertStored(TableBlockPutResult result)
        {
            Assert.Equal(TableBlockPutOutcome.Stored, result.Outcome);
            Assert.Empty(result.Refusals);
        }

        private List<(long, double?, long?)> Rows(BaseSensorModel sensor)
        {
            var result = _server.Service.Read(sensor.Id, From, To);
            Assert.True(result.IsOk, result.Error);
            Assert.Equal(0, result.Value.BlocksFailed);
            Assert.Equal(0, result.Value.BatchesFailed);
            return Snapshot(result.Value);
        }

        // The rows of several sensors one after another, a separator between them.
        private List<(long, double?, long?)> All(params BaseSensorModel[] sensors) =>
            sensors.SelectMany(s => Rows(s).Append((-1L, null, null))).ToList();

        private static List<(long, double?, long?)> Snapshot(TableRows rows) =>
            Enumerable.Range(0, rows.RowCount).Select(r => (rows.Time[r],
                rows.Columns[1].IsNull(r) ? null : (double?)rows.Columns[1].DoubleValues[r],
                rows.Columns[2].IsNull(r) ? null : (long?)rows.Columns[2].Int64Values[r])).ToList();

        // Every stored batch of the sensors in hour 0, in order.
        private List<byte[]> Stored(List<BaseSensorModel> sensors) =>
            sensors.SelectMany(s => _server.Batches.ReadSensor(_product, s.Id, TablePeriodClass.Of(s, DateTime.UtcNow), [Hour0.Ticks]).Select(r => r.Batch)).ToList();

        // One block of tables of the schema; a null bid or size is a cell without a value.
        private byte[] Block(params (string Name, (DateTime Time, double? Bid, long? Size)[] Rows)[] tables)
        {
            using var writer = new TableBlockWriter();

            foreach (var (name, rows) in tables)
            {
                var bidNulls = new byte[TableNullMap.ByteLength(rows.Length)];
                var sizeNulls = new byte[TableNullMap.ByteLength(rows.Length)];
                bool anyBid = false, anySize = false;

                for (int i = 0; i < rows.Length; i++)
                {
                    if (rows[i].Bid is null) { TableNullMap.SetNull(bidNulls, i); anyBid = true; }
                    if (rows[i].Size is null) { TableNullMap.SetNull(sizeNulls, i); anySize = true; }
                }

                writer.BeginTable(Encoding.UTF8.GetBytes($"{_root}/{name}"), Quotes.Id, rows.Length);
                writer.AddTimeColumn(rows.Select(r => r.Time.Ticks).ToArray());
                writer.AddDoubleColumn(rows.Select(r => r.Bid ?? -7.0).ToArray(), anyBid ? bidNulls : null);
                writer.AddInt64Column(rows.Select(r => r.Size ?? -7L).ToArray(), anySize ? sizeNulls : null);
            }

            return writer.ToArray();
        }

        // One block of tables of a schema of a time column and double columns, random values, one in five null.
        private byte[] WideBlock(TableSchema schema, string[] names, int rows, Random random)
        {
            using var writer = new TableBlockWriter();

            foreach (var name in names)
            {
                writer.BeginTable(Encoding.UTF8.GetBytes($"{_root}/{name}"), schema.Id, rows);
                writer.AddTimeColumn(Enumerable.Range(0, rows).Select(_ => Hour0.AddSeconds(random.Next(3600)).Ticks).ToArray());

                for (int c = 1; c < schema.Columns.Count; c++)
                {
                    var nulls = new byte[TableNullMap.ByteLength(rows)];
                    var values = new double[rows];

                    for (int i = 0; i < rows; i++)
                        if (random.Next(5) == 0)
                            TableNullMap.SetNull(nulls, i);
                        else
                            values[i] = random.Next(100_000) / 100.0;

                    writer.AddDoubleColumn(values, nulls);
                }
            }

            return writer.ToArray();
        }
    }
}
