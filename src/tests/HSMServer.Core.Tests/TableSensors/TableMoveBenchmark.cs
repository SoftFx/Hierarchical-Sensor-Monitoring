using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMDatabase.AccessManager.TableBatches;
using HSMDatabase.LevelDB.TableBatches;
using HSMDatabase.SealedHistory;
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
using Xunit.Abstractions;

namespace HSMServer.Core.Tests.TableSensors
{
    public sealed class TableMoveBenchmarkFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(TableMoveBenchmark);
    }


    /// <summary>
    /// The move between period classes (#1850 part 4c), measured with the merge job over a real tree and batch store. The
    /// feed is the synthetic quotes hour of <c>TableBatchStoreBenchmark</c> (1,000 tables, about 2.4 M rows an hour, real
    /// encoded batches, no real data), every fourth table (HSM_TABLE_STORE_BENCH_SHARE to change it), written straight into
    /// the store as merged hours of class P53: (a) one copy step of the busiest table's week, (b) the compaction of a week
    /// database after 10%, 50% and 100% of it is deleted, (c) an expired step of a week, (d) the move of a year (53 weeks,
    /// every table but one) from P53 to P5 at the default budget, with the chart latency of the table that stays, and (e)
    /// the cost of the union of an hour's class ranges (feature doc, Known Issues): a 24-hour chart of a P5 sensor in a
    /// product where a P1 sensor has one row four weeks old in every hour, against the same product without that row.
    /// <c>HSM_TABLE_STORE_BENCH=1 dotnet test --filter "FullyQualifiedName~TableMoveBenchmark" --logger "console;verbosity=detailed"</c>
    /// </summary>
    [Trait("Category", "Benchmark")]
    public sealed class TableMoveBenchmark : MonitoringCoreTestsBase<TableMoveBenchmarkFixture>
    {
        private static readonly DateTime Week0 = new(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);
        private const long Hour = TimeSpan.TicksPerHour;
        private const long Week = 7 * TimeSpan.TicksPerDay;

        private static readonly TableSchema Schema = new([
            new TableSchemaColumn("time", TableColumnType.Time),
            new TableSchemaColumn("bid", TableColumnType.Double),
            new TableSchemaColumn("ask", TableColumnType.Double),
        ]);

        private readonly ITestOutputHelper _output;
        private readonly string _root = $"v{Guid.NewGuid():N}"[..12];
        private DateTime _now;


        public TableMoveBenchmark(TableMoveBenchmarkFixture fixture, DatabaseRegisterFixture registerFixture, ITestOutputHelper output) : base(fixture, registerFixture)
        {
            _output = output;
        }


        [TableStoreBenchmarkFact]
        public async Task The_move_between_period_classes()
        {
            int share = int.TryParse(Environment.GetEnvironmentVariable("HSM_TABLE_STORE_BENCH_SHARE"), out var s) ? s : 4;
            var feed = BuildHour(share);
            var core = _databaseCoreManager.DatabaseCore;
            var batches = (TableBatchDatabase)core.TableBatches;
            batches.Clock = () => _now;
            ((HSMDatabase.LevelDB.TableBlocks.TableBlockDatabase)core.TableBlocks).Clock = () => _now;
            var service = new TableBlockService(core, _valuesCache, new TableBlockOptions { MinFreeDiskMb = 0 });
            var job = service.Merge;

            _output.WriteLine($"runtime {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}, {Environment.ProcessorCount} logical CPUs; machine shared with other work; one run");
            _output.WriteLine($"hour: {feed.Count} batches of {feed.Select(b => b.Table).Distinct().Count()} tables, {feed.Sum(b => (long)b.Rows):N0} rows, " +
                              $"{feed.Sum(b => (long)b.Batch.Length) / 1048576.0:F2} MB (every {share}th table); busiest table {feed.Where(b => b.Table == 1).Sum(b => b.Rows):N0} rows/h, " +
                              $"{feed.Where(b => b.Table == 1).Sum(b => (long)b.Batch.Length) / 1024.0:F0} KB/h");

            await CopyStep(batches, job, feed);
            await Compactions(batches, feed);
            await ExpiredStep(batches, job, feed);
            await Year(batches, service, feed);
        }

        // (a) One copy step: the busiest table's week, P53 -> P5.
        private async Task CopyStep(TableBatchDatabase batches, TableMergeJob job, List<FeedBatch> feed)
        {
            var (product, ids) = await ProductAsync("a", feed);
            WriteWeeks(batches, product, ids, feed, 1);
            var busy = ids[1];
            var week = new TableBatchWeek(Week0.Ticks, 53);
            var bytes = batches.GetSensorHourBytes(product, busy, week, Week0.Ticks, Week0.Ticks + Week);

            await KeepAsync(busy, 35);
            var free = Free();
            var marks = new Dictionary<string, double>();
            var watch = new Stopwatch();
            job.Checkpoint = p =>
            {
                if (p == "reconciled")
                    watch.Restart();
                else if (p.StartsWith("move-"))
                    marks.TryAdd(p, watch.Elapsed.TotalMilliseconds);
            };

            _now = Week0.AddDays(7).AddHours(2);
            var report = job.RunPass(_now);
            job.Checkpoint = null;
            Assert.Null(report.Fault);

            _output.WriteLine("");
            _output.WriteLine($"(a) a copy step of the busiest table's week ({bytes.Batches:N0} batches, {bytes.Rows:N0} rows, {bytes.Bytes / 1048576.0:F1} MB): " +
                              $"record {marks["move-recorded"]:F0} ms, copy (C2) {marks["move-copied"] - marks["move-recorded"]:F0} ms, " +
                              $"switch (C3) {marks["move-switched"] - marks["move-copied"]:F0} ms, delete of the source (C4) {marks["move-source-deleted"] - marks["move-switched"]:F0} ms, " +
                              $"record cleared {marks["move-done"] - marks["move-source-deleted"]:F0} ms; {report.MovedBytes / 1048576.0:F1} MB written; " +
                              $"free disk {free / 1073741824.0:F2} -> {Free() / 1073741824.0:F2} GB; the source's deleted share {batches.GetDeletedShare(product, week).Share:P1}");
        }

        // (b) The compaction of a week database after 10%, 50% and 100% of its bytes are deleted (largest tables first).
        private async Task Compactions(TableBatchDatabase batches, List<FeedBatch> feed)
        {
            _output.WriteLine("");

            foreach (var target in new[] { 0.10, 0.50, 1.0 })
            {
                var (product, ids) = await ProductAsync($"b{target * 100:F0}", feed);
                WriteWeeks(batches, product, ids, feed, 1);
                var week = new TableBatchWeek(Week0.Ticks, 53);
                long weekStart = Week0.Ticks, weekEnd = weekStart + Week;

                var bySize = ids.Values.OrderByDescending(id => batches.GetSensorHourBytes(product, id, week, weekStart, weekEnd).Bytes).ToList();
                var deleteWatch = Stopwatch.StartNew();
                foreach (var id in bySize)
                {
                    if (target < 1 && batches.GetDeletedShare(product, week).Share >= target)
                        break;

                    batches.DeleteSensorHours(product, id, week, weekStart, weekEnd);
                }
                deleteWatch.Stop();

                var before = batches.GetDeletedShare(product, week);
                var watch = Stopwatch.StartNew();
                batches.CompactWeek(product, week);
                var ms = watch.Elapsed.TotalMilliseconds;
                var after = batches.GetDeletedShare(product, week);

                _output.WriteLine($"(b) compaction after {before.Share:P0} deleted (target {target:P0}; deletes took {deleteWatch.ElapsedMilliseconds} ms): {ms:F0} ms, " +
                                  $"folder {before.FolderBytes / 1048576.0:F1} MB -> {after.FolderBytes / 1048576.0:F1} MB");
            }
        }

        // (c) An expired step: the busiest table's week, P53 -> P1 three weeks later.
        private async Task ExpiredStep(TableBatchDatabase batches, TableMergeJob job, List<FeedBatch> feed)
        {
            var (product, ids) = await ProductAsync("c", feed);
            WriteWeeks(batches, product, ids, feed, 1);
            await KeepAsync(ids[1], 7);

            var marks = new Dictionary<string, double>();
            var watch = new Stopwatch();
            job.Checkpoint = p =>
            {
                if (p == "move-expiring")
                    watch.Restart();
                else if (p.StartsWith("expired-"))
                    marks.TryAdd(p, watch.Elapsed.TotalMilliseconds);
            };

            _now = Week0.AddDays(21).AddHours(2);
            var report = job.RunPass(_now);
            job.Checkpoint = null;
            Assert.Null(report.Fault);

            _output.WriteLine("");
            _output.WriteLine($"(c) an expired step of the busiest table's week: delete (E1) {marks["expired-deleted"]:F0} ms, switch (E2) {marks["expired-switched"] - marks["expired-deleted"]:F0} ms; " +
                              $"{report.MoveExpiredRows:N0} rows deleted; pass {report.MoveSteps} steps");
        }

        // (d) A year (53 weeks, every table but the median one) from P53 to P5 at the default budget.
        private async Task Year(TableBatchDatabase batches, TableBlockService service, List<FeedBatch> feed)
        {
            var (product, ids) = await ProductAsync("d", feed);
            var other = ids[501];
            var timer = Stopwatch.StartNew();
            WriteWeeks(batches, product, ids, feed, 53);
            var folder = Path.Combine(_databaseCoreManager.DatabaseCore.DatabaseSettings.PathToTableBlocks, TableBatchDatabase.FolderName, product.ToString("N"));
            var initial = Size(folder);

            _output.WriteLine("");
            _output.WriteLine($"(d) a year: 53 week databases of P53, {initial / 1073741824.0:F2} GB, written in {timer.Elapsed.TotalSeconds:F0} s");

            await KeepAsync(other, 371);
            Assert.True((await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product, KeepHistory = new TimeIntervalModel(TimeSpan.FromDays(35).Ticks) }, CancellationToken.None)).IsOk);

            // The chart of the table that stays, the last day of the year, in a loop while the passes run.
            var latencies = new List<double>();
            int replans = 0;
            service.BeforeChartReplan = _ => Interlocked.Increment(ref replans);
            using var stop = new CancellationTokenSource();
            var end = Week0.AddDays(7 * 53);
            var chart = Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    var w = Stopwatch.StartNew();
                    var result = service.Reduce(other, end.AddDays(-1), end, [1, 2], 300);
                    Assert.True(result.IsOk, result.Error);
                    lock (latencies)
                        latencies.Add(w.Elapsed.TotalMilliseconds);
                }
            });

            var job = service.Merge;
            long peak = initial;
            int passes = 0;
            double busiest = 0;
            long moved = 0, expired = 0;
            int steps = 0, compactions = 0;
            var stats = batches.MergeStatistics;
            timer.Restart();

            for (_now = end.AddHours(1); passes < 1000; _now = _now.AddMinutes(1))
            {
                var w = Stopwatch.StartNew();
                var report = job.RunPass(_now);
                busiest = Math.Max(busiest, w.Elapsed.TotalSeconds);
                passes++;
                Assert.Null(report.Fault);

                moved += report.MovedBytes;
                expired += report.MoveExpiredRows;
                steps += report.MoveSteps;
                compactions += report.Compactions;
                peak = Math.Max(peak, Size(folder));

                if (passes % 10 == 0)
                    _output.WriteLine($"   pass {passes}: {steps} steps, {compactions} compactions, pending {Interlocked.Read(ref stats.MoveStepsPending)}, folder {Size(folder) / 1073741824.0:F2} GB");

                if (report.MoveSteps == 0 && report.Compactions == 0 && Interlocked.Read(ref stats.MoveStepsPending) == 0)
                    break;
            }

            stop.Cancel();
            await chart;
            service.BeforeChartReplan = null;

            latencies.Sort();
            _output.WriteLine($"(d) {passes} passes (the last with nothing to do) in {timer.Elapsed.TotalSeconds:F0} s, the longest pass {busiest:F1} s; {steps} steps, " +
                              $"{moved / 1048576.0:F0} MB copied, {expired:N0} rows expired by the move, {compactions} compactions; folder {initial / 1073741824.0:F2} GB -> " +
                              $"{Size(folder) / 1073741824.0:F2} GB, peak extra {(peak - initial) / 1048576.0:F0} MB (sampled after each pass)");
            _output.WriteLine($"    the chart of the table that stays (a day, 300 buckets) during the passes: {latencies.Count} charts, median {latencies[latencies.Count / 2]:F0} ms, " +
                              $"p95 {latencies[(int)(latencies.Count * 0.95)]:F0} ms, max {latencies[^1]:F0} ms; {replans} plans made again");
        }

        /// <summary>
        /// (e) The cost of the union of an hour's class ranges (feature doc, Known Issues): 24-hour charts of R (P5) in a
        /// product where a P1 sensor L has one row four weeks old in every hour, and in the same product without that row;
        /// over the last day and over a day four weeks back; cold (a store opened fresh) and warm.
        /// <c>HSM_TABLE_STORE_BENCH=1 dotnet test --filter "FullyQualifiedName~The_cost_of_the_union"</c>
        /// </summary>
        [TableStoreBenchmarkFact]
        public async Task The_cost_of_the_union_of_class_ranges()
        {
            _output.WriteLine($"runtime {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}; machine shared with other work; one run");
            var hours = 29 * 24;
            var start = Week0.AddDays(7);
            var end = start.AddHours(hours);
            var random = new Random(1850);

            foreach (var wide in new[] { true, false })
            {
                var product = (await _valuesCache.AddProductAsync($"e{(wide ? "w" : "n")}{Guid.NewGuid():N}"[..10], Guid.Empty)).Id;
                var l = await RegisterAsync(product, "l");
                var r = await RegisterAsync(product, "r");
                await KeepAsync(l, 7);
                await KeepAsync(r, 35);

                var root = Path.Combine(Path.GetTempPath(), $"hsm-union-bench-{Guid.NewGuid():N}");
                var store = new TableBatchDatabase(root) { Clock = () => _now };
                store.SetPlacement(product, l, new TablePlacement(1, start.Ticks));
                store.SetPlacement(product, r, new TablePlacement(5, start.Ticks));

                for (int h = 0; h < hours; h++)
                {
                    long hour = start.Ticks + h * Hour;
                    _now = new DateTime(hour, DateTimeKind.Utc);
                    var times = Enumerable.Range(0, 100).Select(i => hour + i * 36 * TimeSpan.TicksPerSecond).ToArray();
                    var bid = times.Select(_ => Math.Round(1 + random.NextDouble(), 4)).ToArray();
                    var rBatch = TableBatchCodec.Encode(new TableBatch(Schema.Id, [TableBatchColumn.Time(times), TableBatchColumn.Double(bid), TableBatchColumn.Double(bid)]));
                    var lTimes = Enumerable.Range(0, 10).Select(i => hour + i * 360 * TimeSpan.TicksPerSecond).ToList();
                    if (wide)
                        lTimes.Insert(0, hour - 4 * Week);
                    var lBid = lTimes.Select(_ => 1.0).ToArray();
                    var lBatch = TableBatchCodec.Encode(new TableBatch(Schema.Id, [TableBatchColumn.Time(lTimes.ToArray()), TableBatchColumn.Double(lBid), TableBatchColumn.Double(lBid)]));

                    store.WriteHour(product, Commit(hour, h + 1), [
                        new TableBatchHourPart(1, [new TableBatchToWrite(l, lTimes[0], lTimes[^1], lTimes.Count, lBatch)]),
                        new TableBatchHourPart(5, [new TableBatchToWrite(r, times[0], times[^1], times.Length, rBatch)]),
                    ]);
                }

                store.Dispose();

                foreach (var (window, from) in new[] { ("the last day", end.AddHours(-24)), ("a day four weeks back", start.AddDays(1)) })
                foreach (var cold in new[] { true, false })
                {
                    var opened = new TableBatchDatabase(root) { Clock = () => _now };
                    var counting = CountingStore.Over(opened);
                    var db = new Moq.Mock<HSMServer.Core.DataLayer.IDatabaseCore>();
                    db.Setup(c => c.TableBlocks).Returns(_databaseCoreManager.DatabaseCore.TableBlocks);
                    db.Setup(c => c.TableBatches).Returns((ITableBatchStore)counting);
                    db.Setup(c => c.DatabaseSettings).Returns(_databaseCoreManager.DatabaseCore.DatabaseSettings);
                    var service = new TableBlockService(db.Object, _valuesCache, new TableBlockOptions { MinFreeDiskMb = 0 });

                    if (!cold)
                        service.Reduce(r, from, from.AddHours(24), [1, 2], 300);

                    ((CountingStore)(object)counting).Reads = 0;
                    var w = Stopwatch.StartNew();
                    var chart = service.Reduce(r, from, from.AddHours(24), [1, 2], 300).Value;
                    var ms = w.Elapsed.TotalMilliseconds;

                    _output.WriteLine($"(e) {(wide ? "with" : "without")} the wide P1 sensor, {(cold ? "cold (store opened fresh)" : "warm")}, {window}: a 24-hour chart of the P5 sensor " +
                                      $"{ms:F1} ms, {((CountingStore)(object)counting).Reads} sources read (ReadSensor calls), {chart.RowCount:N0} rows, {chart.BatchesRead} batches, " +
                                      $"{opened.Statistics.DatabasesOpened} databases opened");
                    opened.Dispose();
                }

                Directory.Delete(root, recursive: true);
            }
        }


        // === Helpers ===

        public class CountingStore : DispatchProxy
        {
            public ITableBatchStore Inner;
            public int Reads;

            public static ITableBatchStore Over(ITableBatchStore inner)
            {
                var proxy = Create<ITableBatchStore, CountingStore>();
                ((CountingStore)(object)proxy).Inner = inner;
                return proxy;
            }

            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (method.Name == nameof(ITableBatchStore.ReadSensor))
                    Interlocked.Increment(ref Reads);

                try
                {
                    return method.Invoke(Inner, args);
                }
                catch (TargetInvocationException ex) when (ex.InnerException is not null)
                {
                    throw ex.InnerException;
                }
            }
        }

        private sealed record FeedBatch(int Table, long First, long Last, int Rows, byte[] Batch);

        // A product of the tree with a table sensor per table of the feed (kept 53 weeks), and its placements (P53).
        private async Task<(Guid Product, Dictionary<int, Guid> Ids)> ProductAsync(string name, List<FeedBatch> feed)
        {
            var product = (await _valuesCache.AddProductAsync($"{name}{Guid.NewGuid():N}"[..10], Guid.Empty)).Id;
            Assert.True((await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product, KeepHistory = new TimeIntervalModel(TimeSpan.FromDays(371).Ticks) }, CancellationToken.None)).IsOk);

            var ids = new Dictionary<int, Guid>();
            foreach (var table in feed.Select(b => b.Table).Distinct())
                ids[table] = await RegisterAsync(product, $"m{table:D4}");

            var batches = _databaseCoreManager.DatabaseCore.TableBatches;
            foreach (var id in ids.Values)
                batches.SetPlacement(product, id, new TablePlacement(53, Week0.Ticks));

            return (product, ids);
        }

        private async Task<Guid> RegisterAsync(Guid product, string name)
        {
            var path = $"{_root}/{name}";
            var result = await _valuesCache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(product, path)
            {
                Update = new SensorUpdate { Id = Guid.Empty },
                Type = SensorType.Table,
                TypeIsExplicit = true,
                Schema = Schema,
            });
            Assert.True(result.IsOk, result.Error);
            Assert.True(_valuesCache.TryGetSensorByPath(product, path, out var sensor));
            return sensor.Id;
        }

        private async Task KeepAsync(Guid sensor, int days) =>
            Assert.True((await _valuesCache.UpdateSensorAsync(new SensorUpdate { Id = sensor, KeepHistory = new TimeIntervalModel(TimeSpan.FromDays(days).Ticks) })).IsOk);

        // Every hour of the weeks from Week0, P53, then one hour of the week after (so the last week is not the newest).
        private void WriteWeeks(TableBatchDatabase batches, Guid product, Dictionary<int, Guid> ids, List<FeedBatch> feed, int weeks)
        {
            for (long h = 0; h <= weeks * 168; h++)
            {
                long hour = Week0.Ticks + h * Hour;
                _now = new DateTime(hour, DateTimeKind.Utc);
                long by = h * Hour;
                batches.WriteHour(product, Commit(hour, h + 1),
                    [new TableBatchHourPart(53, feed.Select(b => new TableBatchToWrite(ids[b.Table], b.First + by, b.Last + by, b.Rows, b.Batch)).ToList())]);
            }
        }

        private static TableBatchHourCommit Commit(long hour, long sequence) => new()
        {
            HourTicks = hour,
            FirstBlockSequence = sequence,
            LastBlockSequence = sequence,
            BlocksMerged = 360,
            Watermarks = new Dictionary<Guid, long> { [Guid.Empty] = sequence },
        };

        private long Free() =>
            new DriveInfo(Path.GetPathRoot(Path.GetFullPath(_databaseCoreManager.DatabaseCore.DatabaseSettings.PathToTableBlocks))).AvailableFreeSpace;

        private static long Size(string folder) =>
            new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f =>
            {
                try
                {
                    return f.Length;
                }
                catch (IOException)
                {
                    return 0;
                }
            });

        // The synthetic quotes hour of TableBatchStoreBenchmark (the same seed and shape), every share-th table by rank, as
        // offsets from Week0. Table 1 is the busiest, table 501 the median.
        private static List<FeedBatch> BuildHour(int share)
        {
            const int tables = 1000;
            const long total = 2_400_000;
            var random = new Random(1850);
            int half = tables / 2;
            double Shape(int i) => i <= half ? 102 * Math.Pow((double)half / i, 2.89) : 102 * Math.Pow((double)half / i, 4.35);

            double lo = 4096, hi = 1e7;
            for (int it = 0; it < 60; it++)
            {
                double cap = (lo + hi) / 2, sum = 0;
                for (int i = 1; i <= tables; i++)
                    sum += Math.Max(1, Math.Min(cap, Shape(i)));
                if (sum > total) hi = cap; else lo = cap;
            }

            var result = new List<FeedBatch>();
            for (int t = 1; t <= tables; t++)
            {
                if ((t - 1) % share != 0)
                    continue;

                int n = (int)Math.Max(1, Math.Min(lo, Shape(t)));
                int decimals = 2 + random.Next(4);
                double tick = Math.Pow(10, -decimals);
                long price = (long)((1 + random.Next(2000)) / tick), spread = 1 + random.Next(3);

                var time = new long[n];
                for (int i = 0; i < n; i++)
                    time[i] = Week0.Ticks + random.Next(3_600_000) * TimeSpan.TicksPerMillisecond;
                Array.Sort(time);

                var bid = new double[n];
                var ask = new double[n];
                for (int i = 0; i < n; i++)
                {
                    if (i > 0 && random.Next(100) < 38)
                    {
                        time[i] = time[i - 1];
                        bid[i] = bid[i - 1];
                        ask[i] = ask[i - 1];
                        continue;
                    }

                    int r = random.Next(100);
                    if (r < 30) price += random.Next(2) == 0 ? -1 : 1;
                    else if (r < 40) price += random.Next(-5, 6);
                    price = Math.Max(1, price);
                    if (random.Next(100) < 5) spread = 1 + random.Next(3);
                    bid[i] = Math.Round(price * tick, decimals);
                    ask[i] = Math.Round((price + spread) * tick, decimals);
                }

                int count = (n + TableBatchCodec.MaxRows - 1) / TableBatchCodec.MaxRows, size = n / count, larger = n % count, start = 0;
                for (int c = 0; c < count; c++)
                {
                    int m = size + (c < larger ? 1 : 0);
                    var bytes = TableBatchCodec.Encode(new TableBatch(Schema.Id, [TableBatchColumn.Time(time[start..(start + m)]),
                        TableBatchColumn.Double(bid[start..(start + m)]), TableBatchColumn.Double(ask[start..(start + m)])]));
                    result.Add(new FeedBatch(t, time[start], time[start + m - 1], m, bytes));
                    start += m;
                }
            }

            return result;
        }
    }
}
