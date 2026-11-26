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

        /// <summary>
        /// Retention (#1850 part 4): the time to drop a year of week databases of one product (53 weeks of class P53, every hour
        /// written), each week by <see cref="TableBatchDatabase.DropWeek"/> on a store opened fresh, as retention finds them
        /// after a restart (closed weeks; the newest is opened by nothing).
        /// <c>HSM_TABLE_BATCH_STORE_BENCH=1 dotnet test --filter "FullyQualifiedName~Dropping_a_year"</c>
        /// </summary>
        [TableBatchStoreBenchmarkFact]
        public void Dropping_a_year_of_week_databases()
        {
            int share = int.TryParse(Environment.GetEnvironmentVariable("HSM_TABLE_BATCH_STORE_BENCH_SHARE"), out var s) ? s : 4;
            var (hour, _, _) = BuildHour(share);
            var root = Path.Combine(_root, "drop");
            var product = Guid.NewGuid();
            const int weeks = 53;

            var timer = Stopwatch.StartNew();
            using (var store = new TableBatchDatabase(root))
            {
                for (long h = Week0; h < Week0 + weeks * Week; h += Hour)
                    store.WriteHour(product, Commit(h, h), [new TableBatchHourPart(53, Shift(hour, h - Week0))]);
            }

            var bytes = Size(root);
            output.WriteLine($"runtime {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}");
            output.WriteLine($"== {weeks} week databases of one product, {bytes / 1073741824.0:F1} GB, written in {timer.Elapsed.TotalSeconds:F0} s");

            using (var store = new TableBatchDatabase(root))
            {
                timer.Restart();
                var listed = store.GetWeeks(product);
                var openMs = timer.Elapsed.TotalMilliseconds;

                var each = new List<double>();
                timer.Restart();
                foreach (var week in listed)
                {
                    var one = Stopwatch.StartNew();
                    store.DropWeek(product, week);
                    each.Add(one.Elapsed.TotalMilliseconds);
                }

                var total = timer.Elapsed.TotalMilliseconds;
                each.Sort();

                Assert.Empty(store.GetWeeks(product));
                Assert.Empty(store.GetHours(product));
                output.WriteLine($"product opened and its weeks listed in {openMs:F0} ms; {listed.Count} weeks dropped in {total:F0} ms " +
                                 $"({bytes / 1048576.0 / Math.Max(total / 1000, 0.001):F0} MB/s); per week: median {each[each.Count / 2]:F0} ms, max {each[^1]:F0} ms; " +
                                 $"left on disk {Size(root) / 1048576.0:F1} MB");
            }

            Directory.Delete(root, recursive: true);
        }

        /// <summary>
        /// What retention and a read's plan scan with years of hourly commits (#1850 part 4b, a recorded limitation): one
        /// product with five years of merged hours (43,800 records in Hours, 261 week databases of one class kept for
        /// ever, 100 sensors with a placement), each hour one small batch, so that only the number of hours and weeks
        /// counts. Measured on a store opened fresh: the open (every record of Hours loaded), the calls a retention pass
        /// and a plan make under the product's lock (GetHours when its list must be built again and when it is cached,
        /// GetHoursWithoutDatabases, GetWeeks, GetPlacements), and the rebuild of Hours and of Placements at an open.
        /// <c>HSM_TABLE_BATCH_STORE_BENCH=1 dotnet test --filter "FullyQualifiedName~Five_years_of_hourly_commits"</c>
        /// </summary>
        [TableBatchStoreBenchmarkFact]
        public void Five_years_of_hourly_commits()
        {
            const int Hours = 5 * 365 * 24;
            var product = Guid.NewGuid();
            var root = Path.Combine(_root, "five-years");
            var sensors = Enumerable.Range(1, 100).Select(i => new Guid(i, 0, 0, new byte[8])).ToArray();

            output.WriteLine($"runtime {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}");

            var store = new TableBatchDatabase(root);
            foreach (var sensor in sensors)
                store.SetPlacement(product, sensor, new TablePlacement(0, Week0));

            var watch = Stopwatch.StartNew();
            for (int h = 0; h < Hours; h++)
            {
                long hour = Week0 + h * Hour;
                var batch = new TableBatchToWrite(sensors[h % sensors.Length], hour + 1, hour + 2, 2, [1, 2, 3, 4, 5, 6, 7, 8]);
                store.WriteHour(product, Commit(hour, h), [new TableBatchHourPart(0, [batch])]);
            }
            output.WriteLine($"written: {Hours:N0} hours in {store.GetWeeks(product).Count} week databases, {watch.Elapsed.TotalSeconds:N0} s, {Size(root) / 1048576.0:N1} MB");
            store.Dispose();

            double Ms(Action action, int times = 1)
            {
                var w = Stopwatch.StartNew();
                for (int i = 0; i < times; i++)
                    action();
                return w.Elapsed.TotalMilliseconds / times;
            }

            // A fresh open: the first call loads Hours and Placements.
            store = new TableBatchDatabase(root);
            output.WriteLine($"open (Hours and Placements loaded, through GetWeeks): {Ms(() => store.GetWeeks(product)):N1} ms");
            output.WriteLine($"GetHours, its list built ({Hours:N0} hours x has-a-database): {Ms(() => store.GetHours(product)):N1} ms");
            output.WriteLine($"GetHours, cached: {Ms(() => store.GetHours(product), 100):N3} ms");
            output.WriteLine($"GetHoursWithoutDatabases (never cached; once per product and retention pass): {Ms(() => store.GetHoursWithoutDatabases(product), 5):N1} ms");
            output.WriteLine($"GetWeeks (sorted copy): {Ms(() => store.GetWeeks(product), 100):N3} ms; GetPlacements (copy of 100): {Ms(() => store.GetPlacements(product), 100):N3} ms");
            output.WriteLine($"IsHourMerged: {Ms(() => store.IsHourMerged(product, Week0 + 1000 * Hour), 1000) * 1000:N2} us");

            // The list of GetHours is built again after every commit, drop and forgotten hour: one more hour, then the list.
            long next = Week0 + (long)Hours * Hour;
            store.WriteHour(product, Commit(next, Hours), [new TableBatchHourPart(0, [new TableBatchToWrite(sensors[0], next + 1, next + 2, 2, [1, 2, 3])])]);
            output.WriteLine($"GetHours after one more commit (built again): {Ms(() => store.GetHours(product)):N1} ms");
            store.Dispose();

            // The rebuilds at an open: Hours from the marks, Placements from the indexes.
            Directory.Delete(Path.Combine(root, TableBatchDatabase.FolderName, product.ToString("N"), TableBatchDatabase.HoursName), recursive: true);
            store = new TableBatchDatabase(root);
            output.WriteLine($"open with Hours rebuilt from the marks of every week database: {Ms(() => store.GetWeeks(product)):N0} ms ({store.Statistics.HoursRebuilt:N0} hours)");
            store.Dispose();

            Directory.Delete(Path.Combine(root, TableBatchDatabase.FolderName, product.ToString("N"), TableBatchDatabase.PlacementsName), recursive: true);
            store = new TableBatchDatabase(root);
            output.WriteLine($"open with Placements rebuilt from the indexes: {Ms(() => store.GetWeeks(product)):N0} ms ({store.GetPlacements(product).Count} sensors)");
            store.Dispose();
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
