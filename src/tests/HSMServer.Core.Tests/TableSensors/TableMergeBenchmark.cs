using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Core.Cache.UpdateEntities;
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
    public sealed class TableMergeBenchmarkFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(TableMergeBenchmark);
    }


    /// <summary>
    /// The merge of one busy synthetic hour end to end with the stores (#1850 part 3): 1,000 table sensors (time, bid,
    /// ask), 360 blocks of about 6,700 rows (the feed of <see cref="TableBlockStoreBenchmark"/>, made up from a random walk,
    /// no real data), put through the block service, then one merge pass: its time, the managed memory it allocated, the
    /// peak of the process's private memory during it, bytes before and after; the same with a memory budget that forces
    /// several passes; the read of the busiest table before and after the merge.
    /// <c>HSM_TABLE_STORE_BENCH=1 dotnet test --filter "FullyQualifiedName~TableMergeBenchmark" --logger "console;verbosity=detailed"</c>
    /// </summary>
    [Trait("Category", "Benchmark")]
    public sealed class TableMergeBenchmark : MonitoringCoreTestsBase<TableMergeBenchmarkFixture>
    {
        private const int Tables = 1000;
        private const int RowsPerBlock = 6700;
        private const int BlocksPerHour = 360;

        private static readonly DateTime Hour0 = new(2026, 11, 24, 9, 0, 0, DateTimeKind.Utc);

        private static readonly TableSchema Schema = new([
            new TableSchemaColumn("time", TableColumnType.Time),
            new TableSchemaColumn("bid", TableColumnType.Double),
            new TableSchemaColumn("ask", TableColumnType.Double),
        ]);

        private readonly ITestOutputHelper _output;
        private readonly string _root = $"b{Guid.NewGuid():N}"[..12];


        public TableMergeBenchmark(TableMergeBenchmarkFixture fixture, DatabaseRegisterFixture registerFixture, ITestOutputHelper output) : base(fixture, registerFixture)
        {
            _output = output;
        }


        [TableStoreBenchmarkFact]
        public async Task Merge_of_one_busy_hour()
        {
            _output.WriteLine($"runtime {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}, {Environment.ProcessorCount} logical CPUs; machine shared with other work");

            var product = TestProductsManager.ProductId;
            for (int t = 0; t < Tables; t++)
            {
                var result = await _valuesCache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(product, $"{_root}/t{t:D4}")
                {
                    Update = new SensorUpdate { Id = Guid.Empty },
                    Type = SensorType.Table,
                    TypeIsExplicit = true,
                    Schema = Schema,
                });
                Assert.True(result.IsOk, result.Error);
            }

            var service = new TableBlockService(_databaseCoreManager.DatabaseCore, _valuesCache, new TableBlockOptions { MinFreeDiskMb = 0 });
            var blocks = (HSMDatabase.LevelDB.TableBlocks.TableBlockDatabase)_databaseCoreManager.DatabaseCore.TableBlocks;
            var batches = (HSMDatabase.LevelDB.TableBatches.TableBatchDatabase)_databaseCoreManager.DatabaseCore.TableBatches;
            var now = Hour0;
            blocks.Clock = () => now;
            batches.Clock = () => now;

            var producer = Guid.NewGuid();
            long rows = 0, blockBytes = 0;
            var timer = Stopwatch.StartNew();

            foreach (var (block, index) in BuildHour().Select((b, i) => (b, i)))
            {
                now = Hour0.AddSeconds(10 * index);
                var put = service.Put(product, producer, index, block);
                Assert.Equal(TableBlockPutOutcome.Stored, put.Outcome);
                rows += TableBlockReader.ReadHeader(block).RowCount;
                blockBytes += block.Length;
            }

            _output.WriteLine($"put {BlocksPerHour} blocks, {rows:N0} rows, {blockBytes / 1024.0 / 1024:F1} MB ({blockBytes / (double)rows:F2} B/row) in {timer.ElapsedMilliseconds} ms");

            var busiest = _valuesCache.GetSensors().OfType<HSMServer.Core.Model.TableSensorModel>().First(s => s.Path.EndsWith("/t0000"));
            var fromBlocks = Measure(() => service.Read(busiest.Id, Hour0, Hour0.AddHours(2)).Value.RowCount, out var readBlocksMs);
            _output.WriteLine($"read of the busiest table ({fromBlocks:N0} rows) from blocks: {readBlocksMs} ms");

            Run(service, "one pass (budget 256 MB)", Hour0.AddHours(1).AddMinutes(3), null, batches, rows);

            var fromBatches = Measure(() => service.Read(busiest.Id, Hour0, Hour0.AddHours(2)).Value.RowCount, out var readBatchesMs);
            Assert.Equal(fromBlocks, fromBatches);
            _output.WriteLine($"read of the busiest table from batches: {readBatchesMs} ms");

            // The same hour again with a budget of 32 MB: the batch store dropped, the blocks are still there (the delay).
            var settings = _databaseCoreManager.DatabaseCore.DatabaseSettings;
            batches.Dispose();
            System.IO.Directory.Delete(System.IO.Path.Combine(settings.PathToTableBlocks, HSMDatabase.LevelDB.TableBatches.TableBatchDatabase.FolderName), recursive: true);
            var second = new HSMDatabase.LevelDB.TableBatches.TableBatchDatabase(settings.PathToTableBlocks) { Clock = () => now };
            var core = new Moq.Mock<HSMServer.Core.DataLayer.IDatabaseCore>();
            core.Setup(c => c.TableBlocks).Returns(blocks);
            core.Setup(c => c.TableBatches).Returns(second);
            core.Setup(c => c.DatabaseSettings).Returns(settings);
            var budgeted = new TableBlockService(core.Object, _valuesCache, new TableBlockOptions { MinFreeDiskMb = 0, MergeMemoryBudgetMb = 32 });

            Run(budgeted, "budget 32 MB", Hour0.AddHours(1).AddMinutes(3), budgeted.Merge, second, rows);
            second.Dispose();
        }

        private void Run(TableBlockService service, string name, DateTime at, TableMergeJob job, HSMDatabase.LevelDB.TableBatches.TableBatchDatabase batches, long rows)
        {
            job ??= service.Merge;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var privateBefore = process.PrivateMemorySize64;
            var managedBefore = GC.GetTotalMemory(false);
            var allocatedBefore = GC.GetTotalAllocatedBytes(true);
            long peak = privateBefore, managedPeak = managedBefore;

            using var stop = new CancellationTokenSource();
            var sampler = Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    process.Refresh();
                    peak = Math.Max(peak, process.PrivateMemorySize64);
                    managedPeak = Math.Max(managedPeak, GC.GetTotalMemory(false));
                    await Task.Delay(20);
                }
            });

            var timer = Stopwatch.StartNew();
            var report = job.RunPass(at);
            timer.Stop();
            stop.Cancel();
            sampler.Wait();

            Assert.Null(report.Fault);
            Assert.Equal(1, report.HoursMerged);

            var stats = batches.MergeStatistics;
            _output.WriteLine($"{name}: {job.LastPasses} passes over the blocks, {timer.ElapsedMilliseconds} ms ({timer.Elapsed.TotalMilliseconds * 1000 / rows:F2} µs/row), " +
                              $"allocated {(GC.GetTotalAllocatedBytes(true) - allocatedBefore) / 1048576.0:F0} MB, managed heap peak +{(managedPeak - managedBefore) / 1048576.0:F0} MB, " +
                              $"private memory peak +{(peak - privateBefore) / 1048576.0:F0} MB (sampled every 20 ms); " +
                              $"blocks {stats.BlockBytesMerged / 1048576.0:F1} MB -> batches {stats.BatchBytesWritten / 1048576.0:F1} MB " +
                              $"({stats.BatchBytesWritten / (double)stats.RowsMerged:F2} B/row), {batches.Statistics.BatchesWritten:N0} batches");
        }

        private static int Measure(Func<int> run, out long ms)
        {
            run();
            var timer = Stopwatch.StartNew();
            var result = run();
            ms = timer.ElapsedMilliseconds;
            return result;
        }

        // The feed of TableBlockStoreBenchmark with this test's paths and schema id.
        private System.Collections.Generic.List<byte[]> BuildHour()
        {
            var random = new Random(1844);
            var weights = Enumerable.Range(0, Tables).Select(i => 1.0 / Math.Pow(i + 1, 0.9)).ToArray();
            var total = weights.Sum();
            var price = Enumerable.Range(0, Tables).Select(_ => 1 + random.NextDouble() * 2000).ToArray();
            var decimals = Enumerable.Range(0, Tables).Select(_ => random.Next(2, 6)).ToArray();
            var blocks = new System.Collections.Generic.List<byte[]>(BlocksPerHour);

            using var writer = new TableBlockWriter();

            for (int b = 0; b < BlocksPerHour; b++)
            {
                writer.Reset();
                var tickStart = Hour0.AddSeconds(10 * b).Ticks;

                for (int t = 0; t < Tables; t++)
                {
                    var n = (int)Math.Round(RowsPerBlock * weights[t] / total * (0.5 + random.NextDouble()));
                    if (n == 0)
                        continue;

                    var time = new long[n];
                    var bid = new double[n];
                    var ask = new double[n];

                    for (int i = 0; i < n; i++)
                    {
                        time[i] = tickStart + random.Next(0, 10_000) * TimeSpan.TicksPerMillisecond;
                        price[t] = Math.Max(0.0001, price[t] * (1 + (random.NextDouble() - 0.5) * 0.0004));
                        bid[i] = Math.Round(price[t], decimals[t]);
                        ask[i] = Math.Round(bid[i] + Math.Pow(10, -decimals[t]) * random.Next(1, 4), decimals[t]);
                    }

                    if (random.Next(4) != 0)
                        Array.Sort(time);

                    writer.BeginTable(Encoding.UTF8.GetBytes($"{_root}/t{t:D4}"), Schema.Id, n);
                    writer.AddTimeColumn(time);
                    writer.AddDoubleColumn(bid);
                    writer.AddDoubleColumn(ask);
                }

                blocks.Add(writer.ToArray());
            }

            return blocks;
        }
    }
}
