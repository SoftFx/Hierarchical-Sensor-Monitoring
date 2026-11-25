using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using HSMCommon.Model;
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
    public sealed class TableChartBenchmarkFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(TableChartBenchmark);
    }


    /// <summary>
    /// The chart read of one table (#1847): its time and the managed memory it allocates, for 10 million rows over merged
    /// hours (the default scan limit), and for one hour not merged yet of 60,000 rows of the table in blocks of a thousand
    /// tables (the read decompresses every block of the hour). Synthetic random-walk data.
    /// <c>HSM_TABLE_STORE_BENCH=1 dotnet test --filter "FullyQualifiedName~TableChartBenchmark" --logger "console;verbosity=detailed"</c>
    /// </summary>
    [Trait("Category", "Benchmark")]
    [Collection("Database collection")]
    public sealed class TableChartBenchmark : MonitoringCoreTestsBase<TableChartBenchmarkFixture>
    {
        private static readonly DateTime Hour0 = new(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);

        private static readonly TableSchema Schema = new([
            new TableSchemaColumn("time", TableColumnType.Time),
            new TableSchemaColumn("bid", TableColumnType.Double),
            new TableSchemaColumn("ask", TableColumnType.Double),
        ]);

        private readonly ITestOutputHelper _output;
        private readonly string _root = $"k{Guid.NewGuid():N}"[..12];
        private readonly Guid _product = TestProductsManager.ProductId;
        private DateTime _now = Hour0;


        public TableChartBenchmark(TableChartBenchmarkFixture fixture, DatabaseRegisterFixture registerFixture, ITestOutputHelper output) : base(fixture, registerFixture)
        {
            _output = output;
        }


        [TableStoreBenchmarkFact]
        public async Task Chart_of_ten_million_merged_rows_and_of_one_unmerged_hour()
        {
            _output.WriteLine($"runtime {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}, {Environment.ProcessorCount} logical CPUs; machine shared with other work");

            var service = new TableBlockService(_databaseCoreManager.DatabaseCore, _valuesCache, new TableBlockOptions { MinFreeDiskMb = 0 });
            var blocks = (HSMDatabase.LevelDB.TableBlocks.TableBlockDatabase)_databaseCoreManager.DatabaseCore.TableBlocks;
            var batches = (HSMDatabase.LevelDB.TableBatches.TableBatchDatabase)_databaseCoreManager.DatabaseCore.TableBatches;
            blocks.Clock = () => _now;
            batches.Clock = () => _now;

            var random = new Random(1847);
            var producer = Guid.NewGuid();
            long sequence = 0;

            // 1. Ten million rows of one table over 100 hours, 100,000 an hour in ten blocks, all merged.
            var merged = await RegisterAsync("merged");
            const int Hours = 100, BlocksPerHour = 10, RowsPerBlock = 10_000;
            double price = 1.1;

            for (int h = 0; h < Hours; h++)
                for (int b = 0; b < BlocksPerHour; b++)
                {
                    _now = Hour0.AddHours(h).AddMinutes(b * 6 + 1);
                    var start = Hour0.AddHours(h).AddMinutes(b * 6).Ticks;
                    using var writer = new TableBlockWriter();
                    AddTable(writer, "merged", RowsPerBlock, start, TimeSpan.FromMinutes(6).Ticks, random, ref price);
                    Assert.Equal(TableBlockPutOutcome.Stored, service.Put(_product, producer, ++sequence, writer.ToArray()).Outcome);
                }

            for (var pass = 0; pass < 10; pass++)
            {
                _now = Hour0.AddHours(Hours + 1).AddMinutes(3 + pass);
                var report = service.Merge.RunPass(_now);
                Assert.Null(report.Fault);
                if (report.HoursMerged == 0)
                    break;
            }

            Assert.True(batches.IsHourMerged(_product, Hour0.AddHours(Hours - 1).Ticks));

            var range = (From: Hour0, To: Hour0.AddHours(Hours));
            Measure("10,000,000 rows over 100 merged hours, 1,000 buckets, bid and ask", () => service.Reduce(merged.Id, range.From, range.To, [1, 2], 1000));

            // For comparison: a read of the default 1,000,000 rows of the same range holds the hour gate for its whole length.
            var readTimer = Stopwatch.StartNew();
            var page = service.Read(merged.Id, range.From, range.To).Value;
            _output.WriteLine($"for comparison, a read of the same range: {page.RowCount:N0} rows (truncated: {page.Truncated}) in {readTimer.ElapsedMilliseconds} ms");

            // 2. One hour not merged: 360 blocks of a thousand tables, 167 rows of the table charted and 7 of each other
            // table in each block (60,120 rows of the table in the hour).
            var tables = Enumerable.Range(0, 999).Select(i => $"o{i:D3}").ToArray();
            foreach (var name in tables)
                await RegisterAsync(name);

            var unmerged = await RegisterAsync("unmerged");
            var hour = Hour0.AddHours(Hours + 2);

            for (int b = 0; b < 360; b++)
            {
                _now = hour.AddSeconds(10 * b + 5);
                var start = hour.AddSeconds(10 * b).Ticks;
                using var writer = new TableBlockWriter();
                AddTable(writer, "unmerged", 167, start, TimeSpan.FromSeconds(10).Ticks, random, ref price);
                foreach (var name in tables)
                    AddTable(writer, name, 7, start, TimeSpan.FromSeconds(10).Ticks, random, ref price);

                Assert.Equal(TableBlockPutOutcome.Stored, service.Put(_product, producer, ++sequence, writer.ToArray()).Outcome);
            }

            Measure("one unmerged hour, 60,120 rows of the table in 360 blocks of 1,000 tables, 1,000 buckets", () => service.Reduce(unmerged.Id, hour, hour.AddHours(1), [1, 2], 1000));

            var timer = Stopwatch.StartNew();
            var read = service.Read(unmerged.Id, hour, hour.AddHours(1)).Value;
            _output.WriteLine($"for comparison, the read of the same hour: {read.RowCount:N0} rows in {timer.ElapsedMilliseconds} ms");
        }

        private void Measure(string what, Func<HSMCommon.TaskResult.TaskResult<TableChart>> reduce)
        {
            // A warm-up run, then the measured one.
            Assert.True(reduce().IsOk);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            var allocated = GC.GetTotalAllocatedBytes(true);
            var timer = Stopwatch.StartNew();

            var result = reduce();

            timer.Stop();
            allocated = GC.GetTotalAllocatedBytes(true) - allocated;
            Assert.True(result.IsOk, result.Error);
            var chart = result.Value;

            _output.WriteLine($"{what}: {chart.RowCount:N0} rows, {chart.BlocksRead} blocks and {chart.BatchesRead} batches read, " +
                $"complete: {chart.CompleteToTicks is null}; {timer.ElapsedMilliseconds} ms, {allocated / 1024.0 / 1024:F1} MB allocated");
        }

        private void AddTable(TableBlockWriter writer, string name, int rows, long start, long span, Random random, ref double price)
        {
            var time = new long[rows];
            var bid = new double[rows];
            var ask = new double[rows];

            for (int i = 0; i < rows; i++)
            {
                price += (random.NextDouble() - 0.5) * 0.0002;
                time[i] = start + span * i / rows;
                bid[i] = Math.Round(price, 5);
                ask[i] = Math.Round(price + 0.0001 + random.Next(3) * 0.00001, 5);
            }

            writer.BeginTable(Encoding.UTF8.GetBytes($"{_root}/{name}"), Schema.Id, rows);
            writer.AddTimeColumn(time);
            writer.AddDoubleColumn(bid);
            writer.AddDoubleColumn(ask);
        }

        private async Task<BaseSensorModel> RegisterAsync(string name)
        {
            var path = $"{_root}/{name}";
            var result = await _valuesCache.AddOrUpdateSensorAsync(new SensorAddOrUpdateRequest(_product, path)
            {
                Update = new SensorUpdate { Id = Guid.Empty },
                Type = SensorType.Table,
                TypeIsExplicit = true,
                Schema = Schema,
            });

            Assert.True(result.IsOk, result.Error);
            Assert.True(_valuesCache.TryGetSensorByPath(_product, path, out var sensor));
            return sensor;
        }
    }
}
