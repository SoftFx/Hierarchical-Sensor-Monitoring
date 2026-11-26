using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using HSMDatabase.AccessManager.TableBlocks;
using HSMDatabase.LevelDB.TableBlocks;
using HSMServer.Core.TableSensors;
using HSMTableBlocks;
using Xunit;
using Xunit.Abstractions;

namespace HSMServer.Core.Tests.TableSensors
{
    /// <summary>Runs only when HSM_TABLE_STORE_BENCH=1; skipped otherwise (and so in CI).</summary>
    public sealed class TableStoreBenchmarkFactAttribute : FactAttribute
    {
        public TableStoreBenchmarkFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("HSM_TABLE_STORE_BENCH") != "1")
                Skip = "benchmark: set HSM_TABLE_STORE_BENCH=1 to run";
        }
    }


    /// <summary>
    /// The block store under a synthetic feed shaped like the measured real hour (#1844): one collector, a block every
    /// 10 seconds of about 6,700 rows over 1,000 tables (a skewed number of rows per table), columns time, bid, ask, made
    /// up by the block writer from a random walk (no real data). Seven day-equivalents (60,480 blocks) into one database,
    /// and the same into one database per day-equivalent (standing in for one database per week: what a split costs).
    /// After every hour the hour before the previous one is deleted as one range and that range compacted, which is what
    /// the merge step (#1850) will do. Measured: bytes the process wrote against bytes stored, real compactions in LevelDB's
    /// LOG during ingest and during the deletes, the open time, the read of one table for one hour, the latency of a
    /// synced put, and the memory of the in-memory index per block.
    /// <c>HSM_TABLE_STORE_BENCH=1 dotnet test --filter "FullyQualifiedName~TableBlockStoreBenchmark" --logger "console;verbosity=detailed"</c>
    /// (HSM_TABLE_STORE_BENCH_DAYS changes the number of day-equivalents.)
    /// </summary>
    [Trait("Category", "Benchmark")]
    public sealed class TableBlockStoreBenchmark : IDisposable
    {
        private const int Tables = 1000;
        private const int RowsPerBlock = 6700;
        private const int BlocksPerHour = 360;
        private const int BlocksPerDay = 24 * BlocksPerHour;

        private static readonly DateTime Start = new(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);

        private readonly ITestOutputHelper _output;
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"hsm-table-bench-{Guid.NewGuid():N}");
        private readonly Guid _product = Guid.NewGuid();


        public TableBlockStoreBenchmark(ITestOutputHelper output) => _output = output;

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException) { }
        }


        [TableStoreBenchmarkFact]
        public void One_database_against_one_per_period()
        {
            var days = int.TryParse(Environment.GetEnvironmentVariable("HSM_TABLE_STORE_BENCH_DAYS"), out var d) ? d : 7;
            var hour = BuildHour(out var rowsPerHour);

            _output.WriteLine($"runtime {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}");
            _output.WriteLine($"feed: {Tables} tables, {RowsPerBlock} rows per block, {BlocksPerHour} blocks an hour, {hour.Average(b => b.Length) / 1024.0:F1} KB per block, " +
                              $"{hour.Sum(b => (long)b.Length) / (double)rowsPerHour:F2} block bytes per row; {days} day-equivalents = {days * BlocksPerDay:N0} blocks");

            foreach (var split in new[] { false, true })
                Run(hour, days, split);

            MeasureMemoryAndOpen(hour);
        }


        private void Run(List<byte[]> hour, int days, bool split)
        {
            var name = split ? "one database per day-equivalent" : "one database";
            var latencies = new List<double>(days * BlocksPerDay);
            long stored = 0, ingestCompactions = 0, deleteCompactions = 0, deleteMs = 0;
            var now = Start;
            var clock = new Func<DateTime>(() => now);
            var producer = Guid.NewGuid();

            var stores = new List<(TableBlockDatabase Store, string Folder)>();
            TableBlockDatabase store = null;
            string folder = null;

            var written = WrittenBytes();
            var timer = Stopwatch.StartNew();

            for (int b = 0; b < days * BlocksPerDay; b++)
            {
                if (store is null || split && b % BlocksPerDay == 0)
                {
                    folder = Path.Combine(_root, split ? $"split-{b / BlocksPerDay}" : "single");
                    store = new TableBlockDatabase(folder) { Clock = clock };
                    stores.Add((store, folder));
                }

                now = Start.AddSeconds(10.0 * b);
                var block = hour[b % BlocksPerHour];
                var header = TableBlockReader.ReadHeader(block);

                var started = Stopwatch.GetTimestamp();
                var ticket = store.Reserve();
                var result = store.Append(ticket, new TableBlockEnvelope { ProductId = _product, ProducerId = producer, ProducerSequence = b },
                    block, header.FirstTime, header.LastTime, header.RowCount, header.TableCount);
                latencies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                stored += result.Entry.StoredLength + TableBlockDatabase.KeySize;

                // At the end of every hour, delete the hour before the previous one (keeps about two hours unmerged).
                if (b % BlocksPerHour == BlocksPerHour - 1 && b >= 2 * BlocksPerHour)
                {
                    var target = TableBlockDatabase.FloorHour(now) - TimeSpan.TicksPerHour;
                    var owner = stores.LastOrDefault(s => s.Store.GetBlocks(_product).Any(e => e.ArrivalHourTicks == target));
                    if (owner.Store is not null)
                    {
                        var before = CountCompactions(owner.Folder);
                        var deleteTimer = Stopwatch.StartNew();
                        owner.Store.DeleteArrivalHour(target);
                        deleteMs += deleteTimer.ElapsedMilliseconds;
                        deleteCompactions += CountCompactions(owner.Folder) - before;
                    }
                }
            }

            var ingestSeconds = timer.Elapsed.TotalSeconds;
            var writtenBytes = WrittenBytes() - written;

            foreach (var (s, f) in stores)
            {
                s.Dispose();
            }

            foreach (var (_, f) in stores)
                ingestCompactions += CountCompactions(f);
            ingestCompactions -= deleteCompactions;

            // Open the last database again and read one table for the last full hour.
            var (_, lastFolder) = stores[^1];
            var open = Stopwatch.StartNew();
            var reopened = new TableBlockDatabase(lastFolder) { Clock = clock };
            var live = reopened.GetBlocks(_product);
            var openMs = open.Elapsed.TotalMilliseconds;
            var count = live.Count;

            // The blocks of the last arrival hour; every block of the feed reuses the times of the first hour, so the
            // read covers that hour and starts at the last hour's first sequence.
            var lastHour = live[^1].ArrivalHourTicks;
            var firstOfHour = live.First(e => e.ArrivalHourTicks == lastHour).Sequence;
            var readMs = Median(5, () => ReadOneTable(reopened, firstOfHour, "t0050", out _));
            var rowsRead = ReadOneTable(reopened, firstOfHour, "t0050", out var rows);
            reopened.Dispose();

            latencies.Sort();
            _output.WriteLine("");
            _output.WriteLine($"== {name}: {stores.Count} database(s)");
            _output.WriteLine($"ingest: {days * BlocksPerDay:N0} blocks in {ingestSeconds:F0} s; stored {stored / 1048576.0:N0} MB; process wrote {writtenBytes / 1048576.0:N0} MB = {writtenBytes / (double)stored:F2}x the stored bytes (hourly range deletes included)");
            _output.WriteLine($"real compactions in LOG: {ingestCompactions} during ingest, {deleteCompactions} during the {days * 24 - 2} hourly range deletes (total {deleteMs} ms)");
            _output.WriteLine($"synced put latency: median {latencies[latencies.Count / 2]:F2} ms, p99 {latencies[(int)(latencies.Count * 0.99)]:F2} ms, max {latencies[^1]:F1} ms");
            _output.WriteLine($"open of the last database ({count} blocks live): {openMs:F0} ms (first call that opens)");
            _output.WriteLine($"read one table (t0050) for the last hour: {readMs:F1} ms median of 5, {rows} rows ({rowsRead} blocks decompressed)");
            _output.WriteLine($"size on disk now: {DirectorySize(_root) / 1048576.0:N1} MB in {_root}");

            Directory.Delete(_root, recursive: true);
        }

        private void MeasureMemoryAndOpen(List<byte[]> hour)
        {
            // One collector-day without deletes: the bound of the in-memory index and the duplicate map until #1850 merges.
            var folder = Path.Combine(_root, "day");
            var producer = Guid.NewGuid();
            var store = new TableBlockDatabase(folder) { Clock = () => Start };
            var written = WrittenBytes();
            long stored = 0;

            for (int b = 0; b < BlocksPerDay; b++)
            {
                var block = hour[b % BlocksPerHour];
                var header = TableBlockReader.ReadHeader(block);
                var ticket = store.Reserve();
                stored += store.Append(ticket, new TableBlockEnvelope { ProductId = _product, ProducerId = producer, ProducerSequence = b }, block,
                    header.FirstTime, header.LastTime, header.RowCount, header.TableCount).Entry.StoredLength + TableBlockDatabase.KeySize;
            }

            store.Dispose();
            var writtenBytes = WrittenBytes() - written;
            var compactions = CountCompactions(folder);

            var before = GC.GetTotalMemory(forceFullCollection: true);
            var open = Stopwatch.StartNew();
            var reopened = new TableBlockDatabase(folder);
            _ = reopened.NextSequence;
            var openMs = open.Elapsed.TotalMilliseconds;
            var after = GC.GetTotalMemory(forceFullCollection: true);

            _output.WriteLine("");
            _output.WriteLine($"== one collector-day ({BlocksPerDay:N0} blocks, {DirectorySize(folder) / 1048576.0:N0} MB) without deletes");
            _output.WriteLine($"ingest only: process wrote {writtenBytes / 1048576.0:N0} MB = {writtenBytes / (double)stored:F2}x the stored bytes; {compactions} real compactions in LOG");
            _output.WriteLine($"open (index load): {openMs:F0} ms; managed memory of the store after the open: {(after - before) / 1024.0:N0} KB = {(after - before) / (double)BlocksPerDay:F0} bytes per block");

            GC.KeepAlive(reopened);
            reopened.Dispose();
        }


        // === Data ===

        // One hour of blocks: 1,000 tables with a skewed share of rows (a few busy instruments, many quiet ones), prices on
        // a random walk with 2 to 5 decimals, ask = bid + spread; times within each 10-second tick, partly out of order.
        private static List<byte[]> BuildHour(out long rows)
        {
            var random = new Random(1844);
            var weights = Enumerable.Range(0, Tables).Select(i => 1.0 / Math.Pow(i + 1, 0.9)).ToArray();
            var total = weights.Sum();
            var price = Enumerable.Range(0, Tables).Select(_ => 1 + random.NextDouble() * 2000).ToArray();
            var decimals = Enumerable.Range(0, Tables).Select(_ => random.Next(2, 6)).ToArray();
            var blocks = new List<byte[]>(BlocksPerHour);
            rows = 0;

            using var writer = new TableBlockWriter();

            for (int b = 0; b < BlocksPerHour; b++)
            {
                writer.Reset();
                var tickStart = Start.AddSeconds(10 * b).Ticks;

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

                    writer.BeginTable(Encoding.ASCII.GetBytes($"product/quotes/t{t:D4}"), 7, n);
                    writer.AddTimeColumn(time);
                    writer.AddDoubleColumn(bid);
                    writer.AddDoubleColumn(ask);
                    rows += n;
                }

                blocks.Add(writer.ToArray());
            }

            return blocks;
        }

        private int ReadOneTable(TableBlockDatabase store, long firstSequence, string table, out int rows)
        {
            var schema = new TableSchema([new TableSchemaColumn("time", TableColumnType.Time), new TableSchemaColumn("bid", TableColumnType.Double), new TableSchemaColumn("ask", TableColumnType.Double)]);
            var collector = new TableRowCollector(schema, Start.Ticks, Start.AddHours(1).Ticks, int.MaxValue / 4);

            var result = collector.Collect(store, store.GetBlocks(_product), firstSequence, Encoding.ASCII.GetBytes($"product/quotes/{table}"));
            rows = result.RowCount;
            return result.BlocksRead;
        }


        // === Measuring ===

        private static double Median(int runs, Func<double> run)
        {
            var times = new List<double>();
            for (int i = 0; i < runs; i++)
            {
                var timer = Stopwatch.StartNew();
                run();
                times.Add(timer.Elapsed.TotalMilliseconds);
            }

            times.Sort();
            return times[runs / 2];
        }

        private static long CountCompactions(string folder)
        {
            long count = 0;

            foreach (var database in new[] { TableBlockDatabase.BlocksName, TableBlockDatabase.IndexName })
            {
                var path = Path.Combine(folder, database);
                if (!Directory.Exists(path))
                    continue;

                foreach (var file in Directory.GetFiles(path, "LOG*"))
                {
                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream);
                    count += Regex.Matches(reader.ReadToEnd(), @"Compacting \d+@\d+ \+ \d+@\d+ files").Count;
                }
            }

            return count;
        }

        private static long DirectorySize(string folder) =>
            Directory.Exists(folder) ? new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;

        // Bytes this process wrote to files so far: the kernel's I/O counter (Windows) or /proc/self/io (Linux).
        private static long WrittenBytes()
        {
            if (OperatingSystem.IsWindows())
                return GetProcessIoCounters(Process.GetCurrentProcess().Handle, out var counters) ? (long)counters.WriteTransferCount : -1;

            if (File.Exists("/proc/self/io"))
                foreach (var line in File.ReadAllLines("/proc/self/io"))
                    if (line.StartsWith("write_bytes:"))
                        return long.Parse(line["write_bytes:".Length..].Trim());

            return -1;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
            public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessIoCounters(IntPtr process, out IoCounters counters);
    }
}
