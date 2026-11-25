using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Model;
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
    /// and the refusals of a request. No timing: the stores' clocks are seams.
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

            // Both read hour 0 from its batches: the rows of the two readable blocks, and no failure counted (the record of
            // the commit lists the block it kept; the read of a merged hour does not count it, as Read does not).
            Assert.Equal(after.BucketRows, during.BucketRows);
            Assert.Equal((after.RowCount, after.BlocksFailed, after.BatchesFailed, after.IsIncomplete),
                (during.RowCount, during.BlocksFailed, during.BatchesFailed, during.IsIncomplete));
            Assert.Equal((2L, 0, 0, false), (during.RowCount, during.BlocksFailed, during.BatchesFailed, during.IsIncomplete));
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


        // === Helpers ===

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
