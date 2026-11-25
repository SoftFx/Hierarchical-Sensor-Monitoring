using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMDatabase.AccessManager.TableBatches;
using HSMDatabase.LevelDB.TableBatches;
using HSMDatabase.LevelDB.TableBlocks;
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
using static HSMServer.Core.Tests.TableSensors.TableChartReducerTests;

namespace HSMServer.Core.Tests.TableSensors
{
    public sealed class TableChartReadFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(TableChartReadTests);
    }


    /// <summary>
    /// The chart read of a table sensor through the block service (#1847): hours not merged and merged alike, an hour
    /// committed between the plan and its read, the exact rows of a short range, the scan limit across hours, cancellation
    /// and the refusals of a request; and the plan of both readers taken at one generation of the batch store. No timing:
    /// the stores' clocks are seams.
    /// </summary>
    [Collection("Database collection")]
    public class TableChartReadTests : MonitoringCoreTestsBase<TableChartReadFixture>
    {
        private static readonly DateTime Hour0 = new(2026, 11, 24, 9, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime From = Hour0.AddHours(-1), To = Hour0.AddHours(4);

        private static readonly TableSchema Quotes = new([
            new TableSchemaColumn("time", TableColumnType.Time),
            new TableSchemaColumn("bid", TableColumnType.Double),
            new TableSchemaColumn("size", TableColumnType.Int64),
        ]);

        private static readonly int[] Columns = [1, 2];

        private readonly Guid _product = TestProductsManager.ProductId;
        private readonly Guid _producer = Guid.NewGuid();
        private readonly string _root = $"c{Guid.NewGuid():N}"[..12];
        private long _producerSequence;
        private DateTime _now = Hour0;

        private TableBlockService _service;
        private TableBlockDatabase _blocks;
        private TableBatchDatabase _batches;


        public TableChartReadTests(TableChartReadFixture fixture, DatabaseRegisterFixture registerFixture) : base(fixture, registerFixture)
        {
            Build(null);
        }


        [Fact]
        public async Task A_range_over_hours_not_merged_and_merged_is_reduced_the_same_before_and_after_the_merge()
        {
            var (a, rows) = await SeedAsync(new Random(1847));

            var before = Reduce(a, 300);
            AssertMatchesReference(before, rows, From.Ticks, To.Ticks, Columns);
            Assert.Equal(0, before.BatchesRead);

            Assert.Null(Pass(Hour0.AddHours(1).AddMinutes(3)).Fault);
            Assert.True(_batches.IsHourMerged(_product, Hour0.Ticks));

            var after = Reduce(a, 300);
            AssertMatchesReference(after, rows, From.Ticks, To.Ticks, Columns);
            Assert.True(after.BatchesRead > 0);
            Assert.True(after.BlocksRead > 0);
            Assert.Equal(before.BucketRows, after.BucketRows);
            Assert.False(after.IsIncomplete);
        }

        [Fact]
        public async Task An_hour_committed_and_deleted_between_the_plan_and_its_read_is_read_from_its_batches()
        {
            Build(new TableBlockOptions { MinFreeDiskMb = 0, DeleteMergedBlocksAfterHours = 0 });
            var (a, rows) = await SeedAsync(new Random(18));

            var replanned = new List<long>();
            _service.BeforeChartReplan = replanned.Add;
            _service.AfterChartPlan = plan =>
            {
                if (plan != 1)
                    return;

                // The merge commits hour 0 and, with no delay, deletes its blocks: the chart holds no gate between its plan
                // and its first source, so the deletion is not blocked.
                Assert.Null(Pass(Hour0.AddHours(1).AddMinutes(3)).Fault);
                Assert.Null(Pass(Hour0.AddHours(1).AddMinutes(4)).Fault);
                Assert.DoesNotContain(_blocks.GetBlocks(_product), e => e.ArrivalHourTicks == Hour0.Ticks);
            };

            var chart = Reduce(a, 120);

            // Planned once more, and hour 0 read from its batches by the second plan.
            Assert.Equal(new[] { Hour0.Ticks }, replanned);
            AssertMatchesReference(chart, rows, From.Ticks, To.Ticks, Columns);
            Assert.True(chart.BatchesRead > 0);
            Assert.Equal(0, chart.BlocksFailed);
        }

        [Fact]
        public async Task Rows_that_reach_an_hour_after_the_plan_never_let_the_answer_certify_past_them()
        {
            // The review's sequence: range [T, T + 100 min), scan limit 1. The plan sees hour 0 open with its earliest row at
            // T + 80; then a row at T + 10 reaches hour 0 and a row at T + 0 reaches hour 1, and both hours commit.
            Build(new TableBlockOptions { MinFreeDiskMb = 0, MaxChartScanRows = 1 });
            var a = await RegisterAsync("a");
            var t = Hour0.AddHours(-4);
            var (from, to) = (t, t.AddMinutes(100));

            _now = Hour0.AddMinutes(10);
            Put(Block(("a", [Row(t.AddMinutes(80).Ticks, 80, 80)])));

            _service.AfterChartPlan = plan =>
            {
                if (plan != 1)
                    return;

                _now = Hour0.AddMinutes(20);
                Put(Block(("a", [Row(t.AddMinutes(10).Ticks, 10, 10)])));
                _now = Hour0.AddMinutes(70);
                Put(Block(("a", [Row(t.Ticks, 0, 0)])));
                Assert.Null(Pass(Hour0.AddHours(2).AddMinutes(3)).Fault);
                Assert.True(_batches.IsHourMerged(_product, Hour0.AddHours(1).Ticks));
            };

            var result = _service.Reduce(a.Id, from, to, Columns, 100);
            Assert.True(result.IsOk, result.Error);
            var chart = result.Value;

            // Either the row at T + 10 is counted, or the answer is not certified past it.
            var counted = chart.BucketRows[10] == 1;
            Assert.True(counted || chart.CompleteToTicks <= t.AddMinutes(10).Ticks,
                $"complete to {chart.CompleteToTicks}, row at T + 10 counted: {counted}");
            Assert.Equal(1, chart.BucketRows[0]);
        }

        [Theory]
        [InlineData("hours", 24)]
        [InlineData("hours", 0)]
        [InlineData("blocks", 24)]
        [InlineData("blocks", 0)]
        public async Task A_merge_between_the_steps_of_a_plan_never_lets_the_answer_certify_past_a_row(string step, int deleteAfterHours)
        {
            // The second review's sequence: one-minute buckets, scan limit 1. The plan lists hour 0 with its earliest row at
            // T + 80; between two steps of the plan a row at T + 10 reaches hour 0, a row at T + 0 reaches hour 1, both
            // hours are merged and, with no deletion delay, their blocks deleted.
            Build(new TableBlockOptions { MinFreeDiskMb = 0, MaxChartScanRows = 1, DeleteMergedBlocksAfterHours = deleteAfterHours });
            var a = await RegisterAsync("a");
            var t = Hour0.AddHours(-4);

            _now = Hour0.AddMinutes(10);
            Put(Block(("a", [Row(t.AddMinutes(80).Ticks, 80, 80)])));

            var merged = false;
            _service.PlanStep = name =>
            {
                if (name != step || merged)
                    return;

                merged = true;
                _now = Hour0.AddMinutes(20);
                Put(Block(("a", [Row(t.AddMinutes(10).Ticks, 10, 10)])));
                _now = Hour0.AddMinutes(70);
                Put(Block(("a", [Row(t.Ticks, 0, 0)])));
                Assert.Null(Pass(Hour0.AddHours(2).AddMinutes(3)).Fault);
                Assert.Null(Pass(Hour0.AddHours(2).AddMinutes(4)).Fault);
            };

            var result = _service.Reduce(a.Id, t, t.AddMinutes(100), Columns, 100);
            Assert.True(result.IsOk, result.Error);
            var chart = result.Value;

            var counted = chart.BucketRows[10] == 1;
            Assert.True(counted || chart.CompleteToTicks <= t.AddMinutes(10).Ticks,
                $"complete to {chart.CompleteToTicks}, row at T + 10 counted: {counted}");
            Assert.Equal(1, chart.BucketRows[0]);

            // A read of the same range after it agrees on what it holds.
            var read = _service.Read(a.Id, t, t.AddMinutes(100)).Value;
            Assert.Equal(3, read.RowCount);
        }

        [Fact]
        public async Task A_new_plan_after_sources_were_read_counts_their_rows_once()
        {
            var a = await RegisterAsync("a");
            _now = Hour0.AddMinutes(10);
            Put(Block(("a", [Row(Hour0.AddMinutes(1).Ticks, 1, 1), Row(Hour0.AddMinutes(2).Ticks, 2, 2)])));
            _now = Hour0.AddMinutes(70);
            Put(Block(("a", [Row(Hour0.AddMinutes(65).Ticks, 65, 65)])));

            // While the first source (hour 0) is read, the merge commits both hours (their blocks stay: the default delay),
            // so the second source (hour 1) is found merged after the first was counted.
            var replans = new List<long>();
            _service.BeforeChartReplan = replans.Add;
            var merged = false;
            _service.BeforeBlockRead = _ =>
            {
                if (merged)
                    return;

                merged = true;
                Assert.Null(Pass(Hour0.AddHours(2).AddMinutes(3)).Fault);
            };

            var chart = Reduce(a, 100);

            Assert.Equal(new[] { Hour0.AddHours(1).Ticks }, replans);
            Assert.Equal(3, chart.RowCount);
            Assert.True(chart.BatchesRead > 0);
            AssertMatchesReference(chart, [Row(Hour0.AddMinutes(1).Ticks, 1, 1), Row(Hour0.AddMinutes(2).Ticks, 2, 2), Row(Hour0.AddMinutes(65).Ticks, 65, 65)],
                From.Ticks, To.Ticks, Columns);
        }

        [Fact]
        public async Task An_hour_committed_with_a_block_kept_unreadable_after_the_plan_is_answered_as_a_chart_planned_after_the_commit()
        {
            var a = await RegisterAsync("a");
            _now = Hour0.AddMinutes(10);
            Put(Block(("a", [Row(Hour0.AddMinutes(1).Ticks, 1, 1)])));
            var damaged = _producerSequence + 1;
            Put(Block(("a", [Row(Hour0.AddMinutes(2).Ticks, 2, 2)])));
            Put(Block(("a", [Row(Hour0.AddMinutes(3).Ticks, 3, 3)])));
            var damagedSequence = _blocks.GetBlocks(_product).Single(e => e.ProducerSequence == damaged).Sequence;

            _service.AfterChartPlan = plan =>
            {
                if (plan != 1)
                    return;

                // The merge cannot read one block of hour 0: it keeps it, lists it in the hour's record and commits the rest.
                _blocks.BeforeBlockGet = sequence =>
                {
                    if (sequence == damagedSequence)
                        throw new Exception("Corruption: block checksum mismatch: 000005.ldb");
                };
                Assert.Null(Pass(Hour0.AddHours(1).AddMinutes(3)).Fault);
                _blocks.BeforeBlockGet = null;
                Assert.Equal(new[] { damagedSequence }, _batches.GetHour(_product, Hour0.Ticks).UnreadableBlocks);
            };

            var during = Reduce(a, 100);

            _service.AfterChartPlan = null;
            var after = Reduce(a, 100);

            // Both read hour 0 from its batches: the rows of the two readable blocks, and the block the commit lists as kept
            // unreadable is counted as a failed block (#1850 part 4b), as it is before the merge: the answer is incomplete.
            Assert.Equal(after.BucketRows, during.BucketRows);
            Assert.Equal((after.RowCount, after.BlocksFailed, after.BatchesFailed, after.IsIncomplete),
                (during.RowCount, during.BlocksFailed, during.BatchesFailed, during.IsIncomplete));
            Assert.Equal((2L, 1, 0, true), (during.RowCount, during.BlocksFailed, during.BatchesFailed, during.IsIncomplete));
        }

        [Fact]
        public async Task A_commit_under_each_of_three_plans_ends_in_the_store_changed_fault()
        {
            var a = await RegisterAsync("a");

            // Blocks in arrival hours 0, 1 and 2; each plan meets the commit of the next of them.
            for (int h = 0; h < 3; h++)
            {
                _now = Hour0.AddHours(h).AddMinutes(10);
                Put(Block(("a", [Row(Hour0.AddHours(h).AddMinutes(1).Ticks, h, h)])));
            }

            var replans = 0;
            _service.BeforeChartReplan = _ => replans++;
            _service.AfterChartPlan = plan => Assert.Null(Pass(Hour0.AddHours(plan).AddMinutes(3)).Fault);

            Assert.Throws<TableReadChangedException>(() => _service.Reduce(a.Id, From, To, Columns, 100));
            Assert.Equal(TableBlockService.MaxChartPlans, replans);
        }

        [Fact]
        public async Task A_chart_cancelled_while_it_waited_for_the_gate_does_not_read_the_source()
        {
            var a = await RegisterAsync("a");
            _now = Hour0.AddMinutes(10);
            Put(Block(("a", [Row(Hour0.AddMinutes(1).Ticks, 1, 1)])));

            using var cancel = new CancellationTokenSource();
            using var release = new ManualResetEventSlim();
            using var held = new ManualResetEventSlim();
            var gate = _service.HourGate;
            var read = 0;
            _service.BeforeBlockRead = _ => read++;

            // A deletion holds the gate (on a thread of its own: the lock is released by the thread that took it).
            var deletion = Task.Run(() =>
            {
                using (gate.Delete())
                {
                    held.Set();
                    release.Wait();
                }
            });
            held.Wait();

            // Once the chart waits for the gate, it is cancelled, and then the deletion ends.
            _service.AfterChartPlan = _ => Task.Run(() =>
            {
                SpinWait.SpinUntil(() => gate.WaitingReads > 0);
                cancel.Cancel();
                release.Set();
            });

            Assert.Throws<OperationCanceledException>(() => _service.Reduce(a.Id, From, To, Columns, 100, cancel.Token));
            Assert.Equal(0, read);
            await deletion;
        }

        [Fact]
        public async Task A_short_range_carries_the_rows_exactly_as_the_read_returns_them()
        {
            var a = await RegisterAsync("a");
            _now = Hour0.AddMinutes(10);
            Put(Block(("a", [Row(Hour0.AddMinutes(5).Ticks, 2.0, 10), Row(Hour0.AddMinutes(1).Ticks, null, 20), Row(Hour0.AddMinutes(5).Ticks, 1.0, null)])));
            _now = Hour0.AddMinutes(70);
            Put(Block(("a", [Row(Hour0.AddMinutes(5).Ticks, 3.0, 30)])));
            Assert.Null(Pass(Hour0.AddHours(1).AddMinutes(3)).Fault);
            Put(Block(("a", [Row(Hour0.AddMinutes(65).Ticks, 4.0, 40)])));

            var chart = Reduce(a, 10);
            var read = _service.Read(a.Id, From, To).Value;

            Assert.NotNull(chart.Rows);
            Assert.Equal(Snapshot(read), Snapshot(chart.Rows));
            Assert.Equal(5, chart.RowCount);
        }

        [Fact]
        public async Task A_read_past_the_scan_limit_stops_between_hours_and_is_complete_up_to_a_bucket_boundary()
        {
            Build(new TableBlockOptions { MinFreeDiskMb = 0, MaxChartScanRows = 50 });
            var a = await RegisterAsync("a");
            var rows = new List<TestRow>();

            // Three arrival hours, 40 rows each, every hour's rows in that hour.
            for (int h = 0; h < 3; h++)
            {
                _now = Hour0.AddHours(h).AddMinutes(59);
                var hour = Enumerable.Range(0, 40).Select(i => Row(Hour0.AddHours(h).AddSeconds(i * 60 + 7).Ticks, i * 1.5 - h, h * 100 + i)).ToArray();
                Put(Block(("a", hour)));
                rows.AddRange(hour);
            }

            var chart = Reduce(a, 50);

            // 80 rows read, the limit of 50 passed after the second hour: the third is not read.
            Assert.Equal(80, chart.ScannedRows);
            Assert.NotNull(chart.CompleteToTicks);
            Assert.True(chart.CompleteToTicks <= Hour0.AddHours(2).AddSeconds(7).Ticks);
            Assert.Equal(0, (chart.CompleteToTicks.Value - From.Ticks) % chart.BucketTicks);
            Assert.True(chart.IsIncomplete);
            AssertMatchesReference(chart, rows, From.Ticks, To.Ticks, Columns);
        }

        [Fact]
        public async Task A_cancelled_chart_stops_before_the_next_block()
        {
            var (a, _) = await SeedAsync(new Random(3));
            using var cancel = new CancellationTokenSource();
            var read = 0;
            _service.BeforeBlockRead = _ =>
            {
                if (++read == 2)
                    cancel.Cancel();
            };

            Assert.Throws<OperationCanceledException>(() => _service.Reduce(a.Id, From, To, Columns, 100, cancel.Token));
            Assert.Equal(2, read);
        }

        [Fact]
        public async Task A_request_that_cannot_be_answered_is_an_error()
        {
            var a = await RegisterAsync("a");

            Assert.False(_service.Reduce(Guid.NewGuid(), From, To, Columns, 10).IsOk);
            Assert.False(_service.Reduce(a.Id, To, From, Columns, 10).IsOk);
            Assert.False(_service.Reduce(a.Id, From, From, Columns, 10).IsOk);
            Assert.False(_service.Reduce(a.Id, From, To, Columns, 0).IsOk);
            Assert.False(_service.Reduce(a.Id, From, To, Columns, TableChartReducer.MaxBuckets + 1).IsOk);
            Assert.False(_service.Reduce(a.Id, From, To, [0], 10).IsOk);
            Assert.False(_service.Reduce(a.Id, From, To, [3], 10).IsOk);
            Assert.False(_service.Reduce(a.Id, From, To, [1, 1], 10).IsOk);

            var empty = _service.Reduce(a.Id, From, To, [], 10);
            Assert.True(empty.IsOk, empty.Error);
            Assert.Equal(0, empty.Value.RowCount);
            Assert.Empty(empty.Value.Columns);
        }


        // === Placements and retention (#1850 part 4b) ===

        [Fact]
        public async Task A_block_kept_unreadable_is_a_failed_block_before_and_after_its_hour_is_merged()
        {
            var a = await RegisterAsync("a");
            _now = Hour0.AddMinutes(10);
            Put(Block(("a", [Row(Hour0.AddMinutes(1).Ticks, 1, 1)])));
            var damaged = _producerSequence + 1;
            Put(Block(("a", [Row(Hour0.AddMinutes(2).Ticks, 2, 2)])));
            var damagedSequence = _blocks.GetBlocks(_product).Single(e => e.ProducerSequence == damaged).Sequence;

            _blocks.BeforeBlockGet = sequence =>
            {
                if (sequence == damagedSequence)
                    throw new Exception("Corruption: block checksum mismatch: 000005.ldb");
            };

            var before = Reduce(a, 100);
            var beforeRead = _service.Read(a.Id, From, To).Value;
            Assert.Null(Pass(Hour0.AddHours(1).AddMinutes(3)).Fault);
            Assert.Equal(new[] { damagedSequence }, _batches.GetHour(_product, Hour0.Ticks).UnreadableBlocks);
            var after = Reduce(a, 100);
            var afterRead = _service.Read(a.Id, From, To).Value;
            _blocks.BeforeBlockGet = null;

            Assert.Equal((1L, 1, true), (before.RowCount, before.BlocksFailed, before.IsIncomplete));
            Assert.Equal((1L, 1, true), (after.RowCount, after.BlocksFailed, after.IsIncomplete));
            Assert.Equal((1, 1), (beforeRead.RowCount, beforeRead.BlocksFailed));
            Assert.Equal((1, 1), (afterRead.RowCount, afterRead.BlocksFailed));

            // Counted for a range that holds the block only: a range after it does not count it.
            var later = _service.Reduce(a.Id, Hour0.AddMinutes(30), To, Columns, 10).Value;
            Assert.Equal(0, later.BlocksFailed);
        }

        [Fact]
        public async Task Both_readers_see_the_rows_merged_under_a_period_before_its_change()
        {
            var a = await RegisterAsync("a");
            Assert.True((await _valuesCache.UpdateSensorAsync(new SensorUpdate { Id = a.Id, KeepHistory = new TimeIntervalModel(TimeSpan.FromDays(7).Ticks) })).IsOk);

            _now = Hour0.AddMinutes(10);
            Put(Block(("a", [Row(Hour0.AddMinutes(1).Ticks, 1, 1)])));
            Assert.Null(Pass(Hour0.AddHours(1).AddMinutes(3)).Fault);

            Assert.True((await _valuesCache.UpdateSensorAsync(new SensorUpdate { Id = a.Id, KeepHistory = new TimeIntervalModel(TimeSpan.FromDays(35).Ticks) })).IsOk);
            _now = Hour0.AddMinutes(70);
            Put(Block(("a", [Row(Hour0.AddMinutes(61).Ticks, 2, 2)])));
            Assert.Null(Pass(Hour0.AddHours(2).AddMinutes(3)).Fault);
            _now = Hour0.AddMinutes(130);
            Put(Block(("a", [Row(Hour0.AddMinutes(121).Ticks, 3, 3)])));
            Assert.Null(Pass(Hour0.AddHours(3).AddMinutes(3)).Fault);

            // Hours 0 and 1 in P1 (before the cut-off), hour 2 in P5: one database each, the reads take each from its own.
            Assert.Equal([1], _batches.GetHour(_product, Hour0.AddHours(1).Ticks).Classes.Select(c => c.PeriodWeeks));
            Assert.Equal([5], _batches.GetHour(_product, Hour0.AddHours(2).Ticks).Classes.Select(c => c.PeriodWeeks));

            var read = _service.Read(a.Id, From, To).Value;
            Assert.Equal(new[] { Hour0.AddMinutes(1).Ticks, Hour0.AddMinutes(61).Ticks, Hour0.AddMinutes(121).Ticks }, read.Time.Take(read.RowCount));
            Assert.Equal((0, 0), (read.BlocksFailed, read.BatchesFailed));

            var chart = Reduce(a, 100);
            Assert.Equal((3L, false), (chart.RowCount, chart.IsIncomplete));
            Assert.Equal(3, chart.BatchesRead);
        }

        [Fact]
        public async Task A_week_dropped_after_the_chart_planned_it_is_planned_again_and_left_out_whole()
        {
            var a = await RegisterAsync("a");
            Assert.True((await _valuesCache.UpdateSensorAsync(new SensorUpdate { Id = a.Id, KeepHistory = new TimeIntervalModel(TimeSpan.FromDays(7).Ticks) })).IsOk);

            _now = Hour0.AddMinutes(10);
            Put(Block(("a", [Row(Hour0.AddMinutes(1).Ticks, 1, 1)])));
            _now = Hour0.AddMinutes(70);
            Put(Block(("a", [Row(Hour0.AddMinutes(61).Ticks, 2, 2)])));
            Assert.Null(Pass(Hour0.AddHours(2).AddMinutes(3)).Fault);
            var week = new HSMDatabase.AccessManager.TableBatches.TableBatchWeek(HSMDatabase.AccessManager.StorageWeek.GetStartTicks(Hour0.Ticks), 1);

            var replans = new List<long>();
            _service.BeforeChartReplan = replans.Add;
            _service.AfterChartPlan = plan =>
            {
                if (plan == 1)
                    _batches.DropWeek(_product, week);
            };

            var chart = Reduce(a, 100);
            Assert.Equal([Hour0.Ticks], replans);
            Assert.Equal((0L, 0, false), (chart.RowCount, chart.BatchesRead, chart.IsIncomplete));
        }

        [Fact]
        public async Task A_batch_whose_record_survives_without_its_bytes_is_a_failed_batch_for_both_readers()
        {
            var a = await RegisterAsync("a");
            _now = Hour0.AddMinutes(10);
            Put(Block(("a", [Row(Hour0.AddMinutes(1).Ticks, 1, 1)])));
            Assert.Null(Pass(Hour0.AddHours(1).AddMinutes(3)).Fault);

            // The index record of the hour's batch is there, its bytes are not.
            _batches.BatchMissing = (hour, ordinal) => hour == Hour0.Ticks && ordinal == 0;

            var read = _service.Read(a.Id, From, To).Value;
            var chart = Reduce(a, 100);
            _batches.BatchMissing = null;

            Assert.Equal((0, 0, 1), (read.RowCount, read.BlocksFailed, read.BatchesFailed));
            Assert.Equal((0L, 1, true), (chart.RowCount, chart.BatchesFailed, chart.IsIncomplete));
        }


        [Fact]
        public async Task A_merged_hour_is_selected_by_the_range_of_the_class_that_holds_the_sensors_rows_not_through_the_gap_between_two_classes()
        {
            // The review's sequence: one hour with rows of a (kept one week, P1) only at minute 1 and rows of c (kept five
            // weeks, P5) only at minute 30.
            var a = await RegisterAsync("a");
            var c = await RegisterAsync("c");
            Assert.True((await _valuesCache.UpdateSensorAsync(new SensorUpdate { Id = a.Id, KeepHistory = new TimeIntervalModel(TimeSpan.FromDays(7).Ticks) })).IsOk);
            Assert.True((await _valuesCache.UpdateSensorAsync(new SensorUpdate { Id = c.Id, KeepHistory = new TimeIntervalModel(TimeSpan.FromDays(35).Ticks) })).IsOk);

            _now = Hour0.AddMinutes(40);
            Put(Block(("a", [Row(Hour0.AddMinutes(1).Ticks, 1, 1)]), ("c", [Row(Hour0.AddMinutes(30).Ticks, 3, 3)])));
            Assert.Null(Pass(Hour0.AddHours(1).AddMinutes(3)).Fault);
            Assert.Equal([(1, Hour0.AddMinutes(1).Ticks), (5, Hour0.AddMinutes(30).Ticks)],
                _batches.GetHour(_product, Hour0.Ticks).Classes.Select(x => (x.PeriodWeeks, x.MinTime)));

            // Every index record of the hour survives without its bytes.
            _batches.BatchMissing = (hour, _) => hour == Hour0.Ticks;

            // A range between the two classes' rows can hold no row of a: the hour is not read for it, and nothing failed.
            var (from, to) = (Hour0.AddMinutes(10), Hour0.AddMinutes(11));
            var read = _service.Read(a.Id, from, to).Value;
            var chart = _service.Reduce(a.Id, from, to, Columns, 10).Value;
            Assert.Equal((0, 0), (read.RowCount, read.BatchesFailed));
            Assert.Equal((0L, 0, false), (chart.RowCount, chart.BatchesFailed, chart.IsIncomplete));

            // A range that holds a's row reads the hour and reports the batch it could not read.
            read = _service.Read(a.Id, From, To).Value;
            chart = Reduce(a, 10);
            _batches.BatchMissing = null;
            Assert.Equal((0, 1), (read.RowCount, read.BatchesFailed));
            Assert.Equal((0L, 1, true), (chart.RowCount, chart.BatchesFailed, chart.IsIncomplete));
        }


        // === Reads during a move between period classes (#1850 part 4c: the read side) ===

        [Fact]
        public async Task Rows_moved_into_a_class_the_hours_record_names_are_read_outside_that_classs_own_range()
        {
            // s's row at minute 1 in P1, r's at minute 30 in P5: the record of hour 0 names both. s is then moved into P5,
            // whose own range in the record is minute 30 only, and the source is dropped: the copy alone serves it.
            var (s, _) = await SeedMoveAsync(1);
            MoveHour(s, Hour0.Ticks);
            _batches.DropWeek(_product, new TableBatchWeek(HSMDatabase.AccessManager.StorageWeek.GetStartTicks(Hour0.Ticks), 1));

            var (from, to) = (Hour0, Hour0.AddMinutes(20));
            var read = _service.Read(s.Id, from, to).Value;
            var chart = _service.Reduce(s.Id, from, to, Columns, 10).Value;

            Assert.Equal(Expected((Hour0.AddMinutes(1).Ticks, 0.0, 0L)), Snapshot(read));
            Assert.Equal((1, 0), (read.BatchesRead, read.BatchesFailed));
            Assert.Equal((1L, 1, 0, false), (chart.RowCount, chart.BatchesRead, chart.BatchesFailed, chart.IsIncomplete));
        }

        [Fact]
        public async Task A_move_step_of_one_sensor_plans_no_chart_of_another_again_and_a_chart_of_the_moved_sensor_plans_again_once()
        {
            var (s, r) = await SeedMoveAsync(4);
            var replans = new List<long>();
            var plans = new List<int>();
            var stale = new List<bool>();
            _service.BeforeChartReplan = replans.Add;
            _service.PlanTaken = stale.Add;

            // r's chart, s's step of hour 0 after r's plan: the chart answers from its first plan.
            _service.AfterChartPlan = plan =>
            {
                plans.Add(plan);
                if (plan == 1)
                    MoveHour(s, Hour0.Ticks);
            };

            var chart = Reduce(r, 100);
            Assert.Equal([1], plans);
            Assert.Empty(replans);
            Assert.Equal((4L, false), (chart.RowCount, chart.IsIncomplete));

            // r's chart, s's step of hour 1 between the steps of r's plan: the plan is not stale.
            (_service.AfterChartPlan, stale) = (null, []);
            _service.PlanTaken = stale.Add;
            _service.PlanStep = Once("blocks", () => MoveHour(s, Hour0.AddHours(1).Ticks));

            chart = Reduce(r, 100);
            Assert.Equal([false], stale);
            Assert.Empty(replans);
            Assert.Equal((4L, false), (chart.RowCount, chart.IsIncomplete));

            // s's read planned before the switch of hour 2 and reading after it: the plan's class still holds the rows (the
            // source is deleted only after the switch, under the write side of the gate the read holds), so it is complete.
            (_service.PlanStep, stale) = (null, []);
            _service.PlanTaken = isStale =>
            {
                stale.Add(isStale);
                if (stale.Count == 1)
                    MoveHour(s, Hour0.AddHours(2).Ticks);
            };

            var rows = _service.Read(s.Id, From, To).Value;
            Assert.Equal([false], stale);
            Assert.Equal(Expected([.. Enumerable.Range(0, 4).Select(h => (Hour0.AddHours(h).AddMinutes(1).Ticks, (double?)h, (long?)h))]), Snapshot(rows));
            Assert.Equal(0, rows.BatchesFailed);

            // s's chart across the step of hour 3, over a range that holds s's row of the hour only: it plans again once and
            // answers the moved row, outside the own range of the class it was moved into.
            _service.PlanTaken = null;
            plans.Clear();
            _service.AfterChartPlan = plan =>
            {
                plans.Add(plan);
                if (plan == 1)
                    MoveHour(s, Hour0.AddHours(3).Ticks);
            };

            chart = _service.Reduce(s.Id, Hour0.AddHours(3), Hour0.AddHours(3).AddMinutes(20), Columns, 10).Value;
            Assert.Equal([1, 2], plans);
            Assert.Equal([Hour0.AddHours(3).Ticks], replans);
            Assert.Equal((1L, 1, false), (chart.RowCount, chart.BatchesRead, chart.IsIncomplete));
            Assert.Empty(_batches.GetPlacement(_product, s.Id).Segments);
        }


        // === The plan: one generation of the store (the third review) ===

        [Fact]
        public async Task A_read_whose_three_plans_each_meet_a_commit_ends_in_the_store_changed_fault()
        {
            var a = await RegisterAsync("a");

            for (int h = 0; h < 3; h++)
            {
                _now = Hour0.AddHours(h).AddMinutes(10);
                Put(Block(("a", [Row(Hour0.AddHours(h).AddMinutes(1).Ticks, h, h)])));
            }

            // The review's sequence: each plan meets the commit of an hour between its listing of the blocks and its question
            // whether their hours are committed. The hour committed there is skipped as merged, and the list of merged hours
            // taken before has no batches of it: the plan holds that hour in neither source, and must never be read.
            var plans = 0;
            _service.PlanStep = step =>
            {
                if (step != "blocks")
                    return;

                plans++;
                Assert.Null(Pass(Hour0.AddHours(plans).AddMinutes(3)).Fault);
            };

            Assert.Throws<TableReadChangedException>(() => _service.Read(a.Id, From, To));
            Assert.Equal(TableBlockService.MaxChartPlans, plans);
        }

        [Fact]
        public async Task A_read_whose_first_plan_meets_a_commit_reads_the_hour_once_from_its_second_plan()
        {
            var a = await RegisterAsync("a");
            _now = Hour0.AddMinutes(10);
            Put(Block(("a", [Row(Hour0.AddMinutes(1).Ticks, 1, 1), Row(Hour0.AddMinutes(2).Ticks, 2, 2)])));

            var stale = new List<bool>();
            _service.PlanTaken = stale.Add;
            _service.PlanStep = Once("blocks", () => Assert.Null(Pass(Hour0.AddHours(1).AddMinutes(3)).Fault));

            var rows = _service.Read(a.Id, From, To).Value;

            Assert.Equal(new[] { true, false }, stale);
            Assert.Equal(Expected((Hour0.AddMinutes(1).Ticks, 1.0, 1L), (Hour0.AddMinutes(2).Ticks, 2.0, 2L)), Snapshot(rows));
            Assert.True(rows.BatchesRead > 0);
        }

        [Fact]
        public async Task A_commit_without_any_class_during_the_plan_of_a_read_never_answers_without_the_row_that_arrived_meanwhile()
        {
            var a = await RegisterAsync("a");
            var arrived = SeedClasslessCommit();

            var rows = _service.Read(a.Id, From, To).Value;

            Assert.Equal(Expected((arrived.Time, arrived.Bid, arrived.Size)), Snapshot(rows));
        }

        [Fact]
        public async Task A_commit_without_any_class_during_the_plan_of_a_chart_never_answers_without_the_row_that_arrived_meanwhile()
        {
            var a = await RegisterAsync("a");
            var arrived = SeedClasslessCommit();

            var chart = Reduce(a, 100);

            Assert.Equal(1, chart.RowCount);
            Assert.False(chart.IsIncomplete);
            AssertMatchesReference(chart, [Row(arrived.Time, arrived.Bid, arrived.Size)], From.Ticks, To.Ticks, Columns);
        }

        [Fact]
        public async Task A_commit_and_the_drop_of_its_week_during_a_plan_make_it_stale_although_the_merged_hours_look_the_same()
        {
            var a = await RegisterAsync("a");
            _now = Hour0.AddMinutes(10);
            Put(Block(("a", [Row(Hour0.AddMinutes(1).Ticks, 1, 1)])));

            var stale = new List<bool>();
            _service.PlanTaken = stale.Add;
            _service.PlanStep = Once("blocks", () =>
            {
                // Hour 0 is committed into its week database, which is then dropped: the list of merged hours is empty before
                // and after, and so is the list of week databases. With the placements (#1850 part 4b) the hour keeps its
                // record after the drop (it is forgotten only once its blocks are gone), so it stays committed.
                Assert.Null(Pass(Hour0.AddHours(1).AddMinutes(3)).Fault);
                Assert.Single(_batches.GetHours(_product));
                _batches.DropWeek(_product, _batches.GetWeeks(_product).Single());
                Assert.Empty(_batches.GetHours(_product));
                Assert.Empty(_batches.GetWeeks(_product));
                Assert.True(_batches.IsHourMerged(_product, Hour0.Ticks));
            });

            var rows = _service.Read(a.Id, From, To).Value;

            // Stale by the generation alone (no list changed), and planned again. The second plan finds the hour committed
            // and its week gone: the rows went with the database, and the blocks of a committed hour are not read.
            Assert.Equal(new[] { true, false }, stale);
            Assert.Equal(0, rows.RowCount);
            Assert.Equal((0, 0), (rows.BlocksFailed, rows.BatchesFailed));
        }

        [Fact]
        public async Task A_placement_written_or_hours_forgotten_during_a_plan_make_it_stale()
        {
            var a = await RegisterAsync("a");
            _now = Hour0.AddMinutes(10);
            Put(Block(("a", [Row(Hour0.AddMinutes(1).Ticks, 1, 1)])));
            Assert.Null(Pass(Hour0.AddHours(1).AddMinutes(3)).Fault);
            var placement = _batches.GetPlacement(_product, a.Id);
            Assert.NotNull(placement);

            // A placement written between two steps of a plan: the class of an hour (ClassOf) may not be the one the plan took.
            var stale = new List<bool>();
            _service.PlanTaken = stale.Add;
            _service.PlanStep = Once("blocks", () => _batches.SetPlacement(_product, a.Id, placement.Retarget(placement.Target + 1, Hour0.AddHours(5).Ticks)));

            var rows = _service.Read(a.Id, From, To).Value;
            Assert.Equal(new[] { true, false }, stale);
            Assert.Equal(Expected((Hour0.AddMinutes(1).Ticks, 1.0, 1L)), Snapshot(rows));

            // Hours forgotten between two steps: an hour the plan found committed may not be committed any more. The week
            // is dropped first (an hour is forgotten only once no database of its week is left).
            _batches.DropWeek(_product, _batches.GetWeeks(_product).Single());
            stale.Clear();
            _service.PlanStep = Once("blocks", () =>
            {
                _batches.ForgetHours(_product, [Hour0.Ticks]);
                Assert.False(_batches.IsHourMerged(_product, Hour0.Ticks));
            });

            _service.Read(a.Id, From, To);
            Assert.Equal(new[] { true, false }, stale);
        }

        [Fact]
        public async Task A_placement_written_after_the_chart_planned_is_planned_again()
        {
            var a = await RegisterAsync("a");
            _now = Hour0.AddMinutes(10);
            Put(Block(("a", [Row(Hour0.AddMinutes(1).Ticks, 1, 1)])));
            Assert.Null(Pass(Hour0.AddHours(1).AddMinutes(3)).Fault);
            var placement = _batches.GetPlacement(_product, a.Id);

            var replans = new List<long>();
            _service.BeforeChartReplan = replans.Add;
            _service.AfterChartPlan = plan =>
            {
                if (plan == 1)
                    _batches.SetPlacement(_product, a.Id, placement.Retarget(placement.Target + 1, Hour0.AddHours(5).Ticks));
            };

            var chart = Reduce(a, 100);

            Assert.Equal([Hour0.Ticks], replans);
            Assert.Equal((1L, false), (chart.RowCount, chart.IsIncomplete));
        }

        [Fact]
        public async Task A_read_cancelled_during_its_last_stale_plan_is_cancelled_not_a_changed_store()
        {
            var a = await RegisterAsync("a");

            for (int h = 0; h < 3; h++)
            {
                _now = Hour0.AddHours(h).AddMinutes(10);
                Put(Block(("a", [Row(Hour0.AddHours(h).AddMinutes(1).Ticks, h, h)])));
            }

            using var cancel = new CancellationTokenSource();
            var plans = 0;
            _service.PlanStep = step =>
            {
                if (step != "blocks")
                    return;

                plans++;
                Assert.Null(Pass(Hour0.AddHours(plans).AddMinutes(3)).Fault);

                if (plans == TableBlockService.MaxChartPlans)
                    cancel.Cancel();
            };

            Assert.Throws<OperationCanceledException>(() => _service.Read(a.Id, From, To, null, cancel.Token));
        }

        [Fact]
        public async Task A_chart_cancelled_while_its_third_plan_waited_for_the_gate_is_cancelled_not_a_changed_store()
        {
            var a = await RegisterAsync("a");

            // Arrival hours 0, 1 and 2; the row of hour 2 is the earliest, so it is the first source of every plan.
            for (int h = 0; h < 3; h++)
            {
                _now = Hour0.AddHours(h).AddMinutes(10);
                Put(Block(("a", [Row(Hour0.AddMinutes(3 - h).Ticks, h, h)])));
            }

            using var cancel = new CancellationTokenSource();
            using var release = new ManualResetEventSlim();
            using var held = new ManualResetEventSlim();
            var gate = _service.HourGate;
            Task deletion = null, waiter = null;

            _service.AfterChartPlan = plan =>
            {
                if (plan < TableBlockService.MaxChartPlans)
                {
                    // Plans 1 and 2 each meet the commit of an hour of their blocks.
                    Assert.Null(Pass(Hour0.AddHours(plan).AddMinutes(3)).Fault);
                    return;
                }

                // Plan 3: a deletion holds the gate (on a thread of its own); once the chart waits for it, hour 2, its first
                // source, is committed, the chart is cancelled, and the deletion ends.
                deletion = Task.Run(() =>
                {
                    using (gate.Delete())
                    {
                        held.Set();
                        release.Wait();
                    }
                });
                held.Wait();

                waiter = Task.Run(() =>
                {
                    SpinWait.SpinUntil(() => gate.WaitingReads > 0);
                    Assert.Null(Pass(Hour0.AddHours(3).AddMinutes(3)).Fault);
                    cancel.Cancel();
                    release.Set();
                });
            };

            Assert.Throws<OperationCanceledException>(() => _service.Reduce(a.Id, From, To, Columns, 100, cancel.Token));
            await waiter;
            await deletion;
            Assert.True(_batches.IsHourMerged(_product, Hour0.AddHours(2).Ticks));
        }


        // === Helpers ===

        private async Task KeepAsync(BaseSensorModel sensor, int days) =>
            Assert.True((await _valuesCache.UpdateSensorAsync(new SensorUpdate { Id = sensor.Id, KeepHistory = new TimeIntervalModel(TimeSpan.FromDays(days).Ticks) })).IsOk);

        // Sensors s (kept one week, P1) and r (five weeks, P5); in each of the given hours from hour 0 on, a row of s at minute
        // 1 (bid and size the hour's number) and one of r at minute 30, all merged. Then s is kept five weeks too and a pass
        // records the change: the hours merged so far stay in P1 (a segment of s's placement), owed to the move.
        private async Task<(BaseSensorModel S, BaseSensorModel R)> SeedMoveAsync(int hours)
        {
            var s = await RegisterAsync("s");
            var r = await RegisterAsync("r");
            await KeepAsync(s, 7);
            await KeepAsync(r, 35);

            for (int h = 0; h < hours; h++)
            {
                _now = Hour0.AddHours(h).AddMinutes(40);
                Put(Block(("s", [Row(Hour0.AddHours(h).AddMinutes(1).Ticks, h, h)]), ("r", [Row(Hour0.AddHours(h).AddMinutes(30).Ticks, 100 + h, 100 + h)])));
            }

            Assert.Null(Pass(Hour0.AddHours(hours).AddMinutes(3)).Fault);
            await KeepAsync(s, 35);
            Assert.Null(Pass(Hour0.AddHours(hours).AddMinutes(4)).Fault);

            var placement = _batches.GetPlacement(_product, s.Id);
            Assert.Equal(5, placement.Target);
            for (int h = 0; h < hours; h++)
            {
                Assert.Equal(1, placement.ClassOf(Hour0.AddHours(h).Ticks));
                Assert.Equal([1, 5], _batches.GetHour(_product, Hour0.AddHours(h).Ticks).Classes.Select(c => c.PeriodWeeks));
            }

            return (s, r);
        }

        // A step of the move as the move will make it (#1850 part 4c), its copy and its switch, through the store's seams: the
        // sensor's batches of the hour copied from its class (P1) into the region of moved batches of P5 with the hour's mark,
        // then its placement switched to P5 for the hour. The source stays: the placement is the only authority, and the
        // delete of the source comes with the move.
        private void MoveHour(BaseSensorModel sensor, long hour)
        {
            var week = HSMDatabase.AccessManager.StorageWeek.GetStartTicks(hour);
            var placement = _batches.GetPlacement(_product, sensor.Id);
            int from = placement.ClassOf(hour), to = placement.Target;
            Assert.NotEqual(from, to);

            var batches = _batches.ReadSensor(_product, sensor.Id, from, [hour])
                .Select(b => new TableBatchToWrite(b.SensorId, b.FirstTime, b.LastTime, b.RowCount, b.Batch)).ToList();
            Assert.NotEmpty(batches);

            _batches.WriteMoved(_product, new TableBatchWeek(week, to), hour, _batches.ReadMark(_product, new TableBatchWeek(week, from), hour), batches);
            _batches.SetPlacement(_product, sensor.Id, placement.WithoutHours(hour, hour + TimeSpan.TicksPerHour));
        }

        private static Action<string> Once(string step, Action action)
        {
            var done = false;
            return name =>
            {
                if (name != step || done)
                    return;

                done = true;
                action();
            };
        }

        // The second sequence of the third review: the plan lists the blocks of hour 0 with a readable row; then a matching row
        // arrives in hour 1, and hour 0 is committed without any batch class (a catch-up merge that found its every row
        // expired: written here as such a commit), before the plan asks whether hour 0 is committed. No list of merged hours
        // shows such a commit. Returns the row of hour 1.
        private (long Time, double Bid, long Size) SeedClasslessCommit()
        {
            _now = Hour0.AddMinutes(10);
            Put(Block(("a", [Row(Hour0.AddMinutes(1).Ticks, 1, 1)])));
            var arrived = (Time: Hour0.AddMinutes(2).Ticks, Bid: 2.0, Size: 2L);

            _service.PlanStep = Once("blocks", () =>
            {
                _now = Hour0.AddHours(1).AddMinutes(10);
                Put(Block(("a", [Row(arrived.Time, arrived.Bid, arrived.Size)])));

                var blocks = _blocks.GetBlocks(_product).Where(e => e.ArrivalHourTicks == Hour0.Ticks).ToList();
                _batches.WriteHour(_product, new TableBatchHourCommit
                {
                    HourTicks = Hour0.Ticks,
                    FirstBlockSequence = blocks.Min(e => e.Sequence),
                    LastBlockSequence = blocks.Max(e => e.Sequence),
                    BlocksMerged = blocks.Count,
                    Watermarks = _batches.GetWatermarks(_product),
                }, []);

                Assert.True(_batches.IsHourMerged(_product, Hour0.Ticks));
                Assert.Empty(_batches.GetHours(_product));
            });

            return arrived;
        }

        private void Build(TableBlockOptions options)
        {
            var database = _databaseCoreManager.DatabaseCore;
            _service = new TableBlockService(database, _valuesCache, options ?? new TableBlockOptions { MinFreeDiskMb = 0 });
            _blocks = (TableBlockDatabase)database.TableBlocks;
            _batches = (TableBatchDatabase)database.TableBatches;
            _blocks.Clock = () => _now;
            _batches.Clock = () => _now;
        }

        private TableMergePassReport Pass(DateTime now)
        {
            _now = now;
            return _service.Merge.RunPass(now);
        }

        private TableChart Reduce(BaseSensorModel sensor, int buckets)
        {
            var result = _service.Reduce(sensor.Id, From, To, Columns, buckets);
            Assert.True(result.IsOk, result.Error);
            return result.Value;
        }

        // Sensor a and b: hour 0 holds two blocks, hour 1 a block with late rows of hour 0; rows of a beyond the range too.
        private async Task<(BaseSensorModel A, List<TestRow> Rows)> SeedAsync(Random random)
        {
            var a = await RegisterAsync("a");
            await RegisterAsync("b");
            var rows = new List<TestRow>();

            foreach (var (minute, count) in new[] { (20, 400), (50, 300), (80, 300) })
            {
                _now = Hour0.AddMinutes(minute);
                var start = Hour0.AddMinutes(minute - 80).Ticks;
                var mine = RandomRows(random, count, start, Hour0.AddMinutes(minute).Ticks)
                    .Select(r => r with { Ask = 0 }).ToArray();
                var other = RandomRows(random, 50, start, Hour0.AddMinutes(minute).Ticks);

                Put(Block(("a", mine), ("b", other)));
                rows.AddRange(mine);
            }

            return (a, rows);
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

        private void Put(byte[] block)
        {
            var result = _service.Put(_product, _producer, ++_producerSequence, block);
            Assert.Equal(TableBlockPutOutcome.Stored, result.Outcome);
            Assert.Empty(result.Refusals);
        }

        private static TestRow Row(long time, double? bid, long? size) => new(time, bid, size, 0);

        private static List<(long, double?, long?)> Expected(params (long Time, double? Bid, long? Size)[] rows) => [.. rows];

        private static List<(long, double?, long?)> Snapshot(TableRows rows) =>
            Enumerable.Range(0, rows.RowCount).Select(r => (rows.Time[r],
                rows.Columns[1].IsNull(r) ? null : (double?)rows.Columns[1].DoubleValues[r],
                rows.Columns[2].IsNull(r) ? null : (long?)rows.Columns[2].Int64Values[r])).ToList();

        // One block of tables of the schema (time, bid, size); a null cell is written as a null.
        private byte[] Block(params (string Name, TestRow[] Rows)[] tables)
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
                writer.AddTimeColumn(rows.Select(r => r.Time).ToArray());
                writer.AddDoubleColumn(rows.Select(r => r.Bid ?? -7.0).ToArray(), anyBid ? bidNulls : null);
                writer.AddInt64Column(rows.Select(r => r.Size ?? -7L).ToArray(), anySize ? sizeNulls : null);
            }

            return writer.ToArray();
        }
    }
}
