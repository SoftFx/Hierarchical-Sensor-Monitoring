using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using HSMDatabase.SealedHistory;
using HSMDatabase.SealedHistory.Format;
using HSMTableBlocks;
using Xunit;
using Xunit.Abstractions;
using ZstdSharp;

namespace HSMDatabase.SealedHistory.Tests
{
    /// <summary>Runs only when HSM_TABLE_BATCH_BENCH=1; skipped otherwise (and so in CI).</summary>
    public sealed class TableBatchBenchmarkFactAttribute : FactAttribute
    {
        public TableBatchBenchmarkFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("HSM_TABLE_BATCH_BENCH") != "1")
                Skip = "benchmark: set HSM_TABLE_BATCH_BENCH=1 to run";
        }
    }


    /// <summary>
    /// The stored batch against its alternatives on a synthetic hour shaped like the measured real hour of quotes (#1850):
    /// 1,000 tables, about 2.4 M rows, a median of about 100 rows per table and about 14% of the tables with 4,096 rows
    /// or more; time at millisecond steps, bid on a tick walk, ask a sticky spread above, 38% of the rows repeating the
    /// previous one. No real data. Bytes per row include the 48-byte header and 70 bytes of key and index record per
    /// batch. The synthetic walk compresses worse than real quotes (2.75 against 1.95 bytes per row with the coders as
    /// they are): the ratios are what it measures.
    /// <c>HSM_TABLE_BATCH_BENCH=1 dotnet test --filter "FullyQualifiedName~TableBatchBenchmark" --logger "console;verbosity=detailed"</c>
    /// </summary>
    [Trait("Category", "Benchmark")]
    public sealed class TableBatchBenchmark(ITestOutputHelper output)
    {
        private const int KeyAndIndexBytes = 70;

        [TableBatchBenchmarkFact]
        public void The_rule_against_raw_and_the_coders_as_they_are()
        {
            output.WriteLine($"runtime {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}");

            foreach (var (name, nullPercent, outliers) in new[] { ("clean", 0, false), ("nulls 0.05%", -5, false), ("nulls 5%", 5, false), ("outliers 0.05% in ask", 0, true) })
            {
                var batches = Hour(nullPercent, outliers);
                long rows = batches.Sum(b => (long)b.RowCount);
                output.WriteLine("");
                output.WriteLine($"== {name}: {batches.Count} batches, {rows:N0} rows");

                Measure("raw columns + zstd 15", batches, rows, Raw);
                Measure("the coders as they are", batches, rows, AsTheyAre);

                var stats = new TableBatchEncodeStats();
                Measure("the rule against raw (TableBatchCodec)", batches, rows, b => TableBatchCodec.Encode(b, stats));
                output.WriteLine($"   streams raw/coded: time {stats.TimeRaw}/{stats.TimeCoded}, double {stats.DoubleRaw}/{stats.DoubleCoded}; " +
                                 $"exception cells {stats.ExceptionCells}, decimal kept by exceptions {stats.DecimalKeptByExceptions}, given up {stats.DecimalGivenUp}, null cells {stats.NullCells}");

                var decode = Stopwatch.StartNew();
                foreach (var b in batches)
                    TableBatchCodec.Decode(TableBatchCodec.Encode(b));
                output.WriteLine($"   encode + decode of every batch: {decode.Elapsed.TotalMilliseconds * 1000 / rows:F3} us/row");
            }
        }

        private void Measure(string name, List<TableBatch> batches, long rows, Func<TableBatch, byte[]> encode)
        {
            long bytes = 0;
            var timer = Stopwatch.StartNew();
            foreach (var b in batches)
                bytes += encode(b).Length + KeyAndIndexBytes;
            output.WriteLine($"{name,-42} {bytes / (double)rows:F3} B/row, encode {timer.Elapsed.TotalMilliseconds * 1000 / rows:F3} us/row");
        }

        // Plain zstd: the columns as raw 8-byte values (nulls as 0), one zstd 15 frame, the same header.
        private static byte[] Raw(TableBatch batch)
        {
            var payload = new byte[batch.RowCount * batch.Columns.Count * 8];
            int o = 0;
            foreach (var column in batch.Columns)
                for (int r = 0; r < batch.RowCount; r++, o += 8)
                    BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(o), column.Type == TableColumnType.Double ? BitConverter.DoubleToInt64Bits(column.DoubleValues[r]) : column.Int64Values[r]);
            using var compressor = new Compressor(15);
            return new byte[TableBatchCodec.HeaderSize + compressor.Wrap(payload).Length];
        }

        // The sealed coders choosing their mode before zstd, nulls left out with the same presence map, one zstd 15 frame.
        private static byte[] AsTheyAre(TableBatch batch)
        {
            var sink = new ByteSink(batch.RowCount * 8);
            IntColumn.Write(sink, batch.Time);

            for (int c = 1; c < batch.Columns.Count; c++)
            {
                var column = batch.Columns[c];
                var present = Enumerable.Range(0, batch.RowCount).Select(r => !column.IsNull(r)).ToArray();
                Presence.Write(sink, present);
                if (!present.Contains(true))
                    continue;

                if (column.Type == TableColumnType.Double)
                    DoubleColumn.Write(sink, Enumerable.Range(0, batch.RowCount).Where(r => present[r]).Select(r => (ulong)BitConverter.DoubleToInt64Bits(column.DoubleValues[r])).ToArray());
                else
                    IntColumn.Write(sink, Enumerable.Range(0, batch.RowCount).Where(r => present[r]).Select(r => column.Int64Values[r]).ToArray());
            }

            using var compressor = new Compressor(15);
            return new byte[TableBatchCodec.HeaderSize + compressor.Wrap(sink.Written).Length];
        }

        // One hour, every table sorted by time and cut evenly into batches of at most 4,096 rows. nullPercent below zero
        // is per ten thousand (-5: 0.05%).
        private static List<TableBatch> Hour(int nullPercent, bool outliers)
        {
            const int tables = 1000;
            const long total = 2_400_000;
            var g = new SeededRows(1850);
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

            bool Null() => nullPercent > 0 ? g.Chance(nullPercent) : nullPercent < 0 && g.Below(10_000) < -nullPercent;

            var batches = new List<TableBatch>();
            for (int t = 1; t <= tables; t++)
            {
                int n = (int)Math.Max(1, Math.Min(lo, Shape(t)) * (0.9 + 0.2 * g.Below(1000) / 1000.0));
                int decimals = 2 + (int)g.Below(4);
                double tick = Math.Pow(10, -decimals);
                long price = (long)((1 + g.Below(2000)) / tick), spread = 1 + g.Below(3);

                var time = new long[n];
                for (int i = 0; i < n; i++)
                    time[i] = TableBatchKit.Start + g.Below(3_600_000) * TimeSpan.TicksPerMillisecond;
                Array.Sort(time);

                var bid = new double[n];
                var ask = new double[n];
                var bidNull = new bool[n];
                var askNull = new bool[n];

                for (int i = 0; i < n; i++)
                {
                    if (i > 0 && g.Chance(38))
                    {
                        time[i] = time[i - 1];
                        bid[i] = bid[i - 1];
                        ask[i] = ask[i - 1];
                        continue;
                    }

                    long r = g.Below(100);
                    if (r < 30) price += g.Chance(50) ? -1 : 1;
                    else if (r < 40) price += g.Below(11) - 5;
                    price = Math.Max(1, price);
                    if (g.Chance(5)) spread = 1 + g.Below(3);

                    bid[i] = Math.Round(price * tick, decimals);
                    ask[i] = Math.Round((price + spread) * tick, decimals);
                    if (outliers && g.Below(10_000) < 5)
                        ask[i] = 1e12 + g.Below(1000) * 0.123456789;
                    if (Null()) { bidNull[i] = true; bid[i] = 0; }
                    if (Null()) { askNull[i] = true; ask[i] = 0; }
                }

                int count = (n + TableBatchCodec.MaxRows - 1) / TableBatchCodec.MaxRows;
                int size = n / count, larger = n % count, start = 0;
                for (int c = 0; c < count; c++)
                {
                    int m = size + (c < larger ? 1 : 0);
                    int s = start;
                    batches.Add(TableBatchKit.Batch(TableBatchColumn.Time(time[s..(s + m)]),
                        TableBatchColumn.Double(bid[s..(s + m)], TableBatchKit.Map(m, k => bidNull[s + k])),
                        TableBatchColumn.Double(ask[s..(s + m)], TableBatchKit.Map(m, k => askNull[s + k]))));
                    start += m;
                }
            }

            return batches;
        }
    }
}
