using System.Diagnostics;
using System.Runtime.InteropServices;
using HSMDatabase.AccessManager.TableBatches;
using HSMDatabase.LevelDB.TableBatches;
using HSMDatabase.SealedHistory;
using Xunit;
using Xunit.Abstractions;

namespace HSMDatabase.LevelDB.Tests.TableBatchTests
{
    /// <summary>Runs only when HSM_TABLE_BATCH_STORE_BENCH=1; skipped otherwise (and so in CI).</summary>
    public sealed class TableBatchStoreBenchmarkFactAttribute : FactAttribute
    {
        public TableBatchStoreBenchmarkFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("HSM_TABLE_BATCH_STORE_BENCH") != "1")
                Skip = "benchmark: set HSM_TABLE_BATCH_STORE_BENCH=1 to run";
        }
    }


    /// <summary>
    /// How many week databases cost (#1850, the owner's question): a period of a year (53 week databases of one product)
    /// and ten products with a period of four weeks. The feed is the synthetic quotes hour of the codec benchmark (1,000
    /// tables, about 2.4 M rows an hour, real encoded batches) cut to every fourth table, so a quarter of the busy rate
    /// and of the disk (HSM_TABLE_BATCH_STORE_BENCH_SHARE=1 for the whole feed); the busiest and the median table are among
    /// the tables kept, at their full rate. Every hour of every week is written through the store. Measured: the open of a
    /// closed week with and without the flush at its close, the read of one table over the whole period with the bound on
    /// open databases and with every database open, the process's handles and memory in both cases.
    /// <c>HSM_TABLE_BATCH_STORE_BENCH=1 dotnet test --filter "FullyQualifiedName~TableBatchStoreBenchmark" --logger "console;verbosity=detailed"</c>
    /// </summary>
    [Trait("Category", "Benchmark")]
    public sealed class TableBatchStoreBenchmark(ITestOutputHelper output) : IDisposable
    {
        private static readonly long Week0 = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc).Ticks;
        private const long Hour = TimeSpan.TicksPerHour;
        private const long Week = 7 * TimeSpan.TicksPerDay;

        private readonly string _root = Path.Combine(Path.GetTempPath(), $"hsm-batch-store-bench-{Guid.NewGuid():N}");

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException) { }
        }


        [TableBatchStoreBenchmarkFact]
        public void A_year_of_one_product_and_four_weeks_of_ten_products()
        {
            int share = int.TryParse(Environment.GetEnvironmentVariable("HSM_TABLE_BATCH_STORE_BENCH_SHARE"), out var s) ? s : 4;
            var (hour, busy, median) = BuildHour(share);

            output.WriteLine($"runtime {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}");
            output.WriteLine($"hour: {hour.Count} batches, {hour.Sum(b => (long)b.RowCount):N0} rows, {hour.Sum(b => (long)b.Batch.Length) / 1048576.0:F2} MB " +
                             $"(every {share}th table of the synthetic quotes hour); busy table {hour.Where(b => b.SensorId == busy).Sum(b => b.RowCount):N0} rows/h, median {hour.Where(b => b.SensorId == median).Sum(b => b.RowCount)} rows/h");

            ClosedWeekOpen(hour);
            Year(hour, busy, median);
            TenProducts(hour, median);
        }

        // One week written, then closed with the flush (the store's close) or without it (a stop), then opened by a read.
        private void ClosedWeekOpen(List<TableBatchToWrite> hour)
        {
            var product = Guid.NewGuid();

            foreach (var flushed in new[] { true, false })
            {
                var root = Path.Combine(_root, flushed ? "closed-flushed" : "closed-unflushed");
                var store = new TableBatchDatabase(root);
                for (int h = 0; h < 168; h++)
                    store.WriteHour(product, Commit(Week0 + h * Hour, h), [new TableBatchHourPart(4, Shift(hour, h * Hour))]);

                var week = store.WeekPath(product, Week0, 4);
                if (flushed)
                    store.Dispose();
                else
                    store.CloseWithoutFlush();

                long log = Directory.GetFiles(week, "*.log").Sum(f => new FileInfo(f).Length);
                int files = Directory.GetFiles(week).Length;

                var reopened = new TableBatchDatabase(root);
                reopened.GetHours(product);
                var timer = Stopwatch.StartNew();
                reopened.ReadSensor(product, hour[^1].SensorId, 4, [Week0]);
                var ms = timer.Elapsed.TotalMilliseconds;
                reopened.Dispose();

                output.WriteLine($"closed week {(flushed ? "flushed at its close" : "closed without a flush")}: {Size(week) / 1048576.0:N0} MB in {files} files, log {log / 1024.0:N0} KB; " +
                                 $"its open by a read: {ms:F0} ms");
                Directory.Delete(root, recursive: true);
            }
        }

        private void Year(List<TableBatchToWrite> hour, Guid busy, Guid median)
        {
            var root = Path.Combine(_root, "year");
            var product = Guid.NewGuid();
            const int weeks = 53;
            var hours = Enumerable.Range(0, weeks * 168).Select(h => Week0 + h * Hour).ToArray();

            var timer = Stopwatch.StartNew();
            using (var store = new TableBatchDatabase(root))
            {
                foreach (var h in hours)
                    store.WriteHour(product, Commit(h, h), [new TableBatchHourPart(53, Shift(hour, h - Week0))]);
            }

            output.WriteLine("");
            output.WriteLine($"== a year: {weeks} week databases of one product, {Size(root) / 1073741824.0:F1} GB, written in {timer.Elapsed.TotalSeconds:F0} s");

            foreach (var bound in new[] { 8, TableBatchDatabase.DefaultMaxOpenDatabases, 1000 })
            {
                var before = Snapshot();
                using var store = new TableBatchDatabase(root) { MaxOpenDatabases = bound };

                timer.Restart();
                store.GetHours(product);
                var hoursMs = timer.Elapsed.TotalMilliseconds;

                var (medianMs, medianRows, medianBatches) = Read(store, product, median, 53, hours, decode: true);
                var afterMedian = Snapshot();
                var (busyMs, busyRows, busyBatches) = Read(store, product, busy, 53, hours, decode: false);
                var after = Snapshot();

                output.WriteLine($"bound {bound}: merged hours loaded in {hoursMs:F0} ms; median table, a year: {medianMs:F0} ms, {medianRows:N0} rows in {medianBatches:N0} batches (decoded); " +
                                 $"busy table, a year: {busyMs:F0} ms, {busyRows:N0} rows in {busyBatches:N0} batches (read, not decoded); " +
                                 $"open now {store.OpenDatabases}, opened {store.Statistics.DatabasesOpened}, closed {store.Statistics.DatabasesClosed}");
                output.WriteLine($"   process after the median read: {Delta(before, afterMedian)}; after the busy read: {Delta(before, after)}");
            }

            Directory.Delete(root, recursive: true);
        }

        private void TenProducts(List<TableBatchToWrite> hour, Guid median)
        {
            var root = Path.Combine(_root, "ten");
            var products = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToArray();
            var hours = Enumerable.Range(0, 5 * 168).Select(h => Week0 + h * Hour).ToArray();

            var timer = Stopwatch.StartNew();
            using (var store = new TableBatchDatabase(root))
            {
                foreach (var h in hours)
                    foreach (var product in products)
                        store.WriteHour(product, Commit(h, h), [new TableBatchHourPart(4, Shift(hour, h - Week0))]);
            }

            output.WriteLine("");
            output.WriteLine($"== ten products, five week databases each (a period of four weeks and the current week): {Size(root) / 1073741824.0:F1} GB, written in {timer.Elapsed.TotalSeconds:F0} s");

            foreach (var bound in new[] { TableBatchDatabase.DefaultMaxOpenDatabases, 1000 })
            {
                var before = Snapshot();
                using var store = new TableBatchDatabase(root) { MaxOpenDatabases = bound };

                timer.Restart();
                long rows = 0;
                foreach (var product in products)
                    rows += Read(store, product, median, 4, hours, decode: true).Rows;

                output.WriteLine($"bound {bound}: the median table of each product for five weeks: {timer.Elapsed.TotalMilliseconds:F0} ms, {rows:N0} rows; " +
                                 $"open now {store.OpenDatabases}, opened {store.Statistics.DatabasesOpened}; process: {Delta(before, Snapshot())}");
            }

            Directory.Delete(root, recursive: true);
        }

        private static (double Ms, long Rows, int Batches) Read(TableBatchDatabase store, Guid product, Guid sensor, int period, long[] hours, bool decode)
        {
            var timer = Stopwatch.StartNew();
            long rows = 0;
            int batches = 0;

            // A week at a time, as a read of a long range does.
            foreach (var week in hours.Chunk(168))
            {
                foreach (var batch in store.ReadSensor(product, sensor, period, week))
                {
                    rows += decode ? TableBatchCodec.Decode(batch.Batch).RowCount : batch.RowCount;
                    batches++;
                }
            }

            return (timer.Elapsed.TotalMilliseconds, rows, batches);
        }


        // === The feed ===

        // The synthetic quotes hour of TableBatchBenchmark (sealed history tests), every share-th table by rank, encoded
        // with the codec. Returns the batches and the busiest and the median table's sensor ids.
        private static (List<TableBatchToWrite> Hour, Guid Busy, Guid Median) BuildHour(int share)
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

            var result = new List<TableBatchToWrite>();
            for (int t = 1; t <= tables; t++)
            {
                if ((t - 1) % share != 0)
                    continue;

                var sensor = new Guid(t, 0, 0, new byte[8]);
                int n = (int)Math.Max(1, Math.Min(lo, Shape(t)));
                int decimals = 2 + random.Next(4);
                double tick = Math.Pow(10, -decimals);
                long price = (long)((1 + random.Next(2000)) / tick), spread = 1 + random.Next(3);

                var time = new long[n];
                for (int i = 0; i < n; i++)
                    time[i] = Week0 + random.Next(3_600_000) * TimeSpan.TicksPerMillisecond;
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
                    var bytes = TableBatchCodec.Encode(new TableBatch(1, [TableBatchColumn.Time(time[start..(start + m)]),
                        TableBatchColumn.Double(bid[start..(start + m)]), TableBatchColumn.Double(ask[start..(start + m)])]));
                    result.Add(new TableBatchToWrite(sensor, time[start], time[start + m - 1], m, bytes));
                    start += m;
                }
            }

            return (result, new Guid(1, 0, 0, new byte[8]), new Guid(501, 0, 0, new byte[8]));
        }

        private static List<TableBatchToWrite> Shift(List<TableBatchToWrite> hour, long by) =>
            hour.Select(b => b with { FirstTime = b.FirstTime + by, LastTime = b.LastTime + by }).ToList();

        private static TableBatchHourCommit Commit(long hour, long sequence) => new()
        {
            HourTicks = hour,
            FirstBlockSequence = sequence,
            LastBlockSequence = sequence,
            BlocksMerged = 360,
            Watermarks = new Dictionary<Guid, long> { [Guid.Empty] = sequence },
        };


        // === Measuring ===

        private static long Size(string folder) => new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);

        private static (int Handles, long Private, long WorkingSet, long Managed) Snapshot()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var process = Process.GetCurrentProcess();
            process.Refresh();
            return (process.HandleCount, process.PrivateMemorySize64, process.WorkingSet64, GC.GetTotalMemory(true));
        }

        private static string Delta((int Handles, long Private, long WorkingSet, long Managed) before, (int Handles, long Private, long WorkingSet, long Managed) after) =>
            $"handles +{after.Handles - before.Handles}, private memory +{(after.Private - before.Private) / 1048576.0:N0} MB, " +
            $"working set +{(after.WorkingSet - before.WorkingSet) / 1048576.0:N0} MB, managed +{(after.Managed - before.Managed) / 1048576.0:N0} MB";
    }
}
