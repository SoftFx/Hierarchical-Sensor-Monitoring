using System.Diagnostics;
using System.Text;
using HSMTableBlocks;
using Xunit;
using Xunit.Abstractions;

namespace HSMTableBlocks.Tests
{
    /// <summary>Runs only when HSM_TABLE_BLOCK_BENCH=1; skipped otherwise (and so in CI).</summary>
    public sealed class BenchmarkFactAttribute : FactAttribute
    {
        public BenchmarkFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("HSM_TABLE_BLOCK_BENCH") != "1")
                Skip = "benchmark: set HSM_TABLE_BLOCK_BENCH=1 to run";
        }
    }


    /// <summary>
    /// Size and speed of one block of 1,000 tables and about 24,000 rows of made-up quotes, at zstd 3: bytes per row,
    /// encode and decode microseconds per row, and the cost of 8-byte against 30-byte table identifiers.
    /// <c>HSM_TABLE_BLOCK_BENCH=1 dotnet test --filter Category=Benchmark --logger "console;verbosity=detailed"</c>
    /// </summary>
    [Trait("Category", "Benchmark")]
    public class BlockBenchmark
    {
        private const int Tables = 1000;
        private const int Runs = 30;

        private readonly ITestOutputHelper _output;

        public BlockBenchmark(ITestOutputHelper output) => _output = output;


        [BenchmarkFact]
        public void Bytes_and_microseconds_per_row()
        {
            _output.WriteLine($"runtime {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, zstd {TableBlockWriter.DefaultCompressionLevel}, {Tables} tables, median of {Runs} runs");
            _output.WriteLine($"{"shape",-28} {"id B",4} {"rows",7} {"block B",9} {"B/row",6} {"raw B/row",9} {"enc us/row",10} {"dec us/row",10} {"1 table us",10}");

            foreach (int idLength in new[] { 8, 30 })
            {
                Run("time, bid, ask", idLength, Columns3);
                Run("time, bid, bsize, ask, asize", idLength, Columns5);
            }
        }


        private void Run(string shape, int idLength, Func<Rng, int, object[]> columns)
        {
            var tables = Build(idLength, columns);
            int rows = tables.Sum(t => t.Rows);

            using var writer = new TableBlockWriter();
            byte[] block = null;
            double encode = Median(() =>
            {
                writer.Reset();
                Samples.Add(writer, tables);
                block = writer.ToArray();
            });

            double decode = Median(() => TableBlockReader.Read(block));
            var one = tables[Tables / 2].Id;
            double single = Median(() => TableBlockReader.ReadTable(block, one));
            int payload = TableBlockReader.ReadHeader(block).DecompressedLength;

            _output.WriteLine($"{shape,-28} {idLength,4} {rows,7:N0} {block.Length,9:N0} {(double)block.Length / rows,6:F2} {(double)payload / rows,9:F2} {encode * 1000 / rows,10:F3} {decode * 1000 / rows,10:F3} {single * 1000,10:F0}");
        }

        /// <summary>A thousand tables of 1..~100 rows (about 24 on average, a few busy ones), times moving forward with some steps back.</summary>
        private static List<TestTable> Build(int idLength, Func<Rng, int, object[]> columns)
        {
            var rng = new Rng(0xBE_7C4);
            var tables = new List<TestTable>(Tables);

            for (int i = 0; i < Tables; i++)
            {
                // Skewed row counts: most tables are quiet, a few are busy.
                double u = rng.NextDouble();
                int rows = 1 + (int)(u * u * u * 95);
                // A unique 5-digit tail behind a path-like prefix cut to the length.
                string id = $"lp{i % 9}/quotes/FX/MAJORS/".PadRight(idLength, '_').Substring(0, idLength - 5) + i.ToString("D5");

                var others = columns(rng, rows);
                var time = new long[rows];
                long t = Samples.T0 + rng.Next(10_000_000) * TimeSpan.TicksPerMillisecond / 1000;
                for (int r = 0; r < rows; r++)
                {
                    t += (rng.Next(4) == 0 ? -rng.Next(50) : rng.Next(400)) * TimeSpan.TicksPerMillisecond;
                    time[r] = t;
                }

                tables.Add(new TestTable(id, 7, time, others));
            }

            return tables;
        }

        private static object[] Columns3(Rng rng, int rows)
        {
            Prices(rng, rows, out var bid, out var ask);
            return new object[] { bid, ask };
        }

        private static object[] Columns5(Rng rng, int rows)
        {
            Prices(rng, rows, out var bid, out var ask);
            var bsize = new long[rows];
            var asize = new long[rows];
            for (int r = 0; r < rows; r++)
            {
                bsize[r] = 100_000L * (1 + rng.Next(50));
                asize[r] = rng.Next(3) == 0 ? bsize[r] : 100_000L * (1 + rng.Next(50));
            }

            return new object[] { bid, bsize, ask, asize };
        }

        private static void Prices(Rng rng, int rows, out double[] bid, out double[] ask)
        {
            bid = new double[rows];
            ask = new double[rows];
            long units = 50_000 + rng.Next(20_000_000);
            for (int r = 0; r < rows; r++)
            {
                units += rng.Next(7) - 3;
                bid[r] = units / 100_000.0;
                ask[r] = (units + 2 + rng.Next(4)) / 100_000.0;
            }
        }

        /// <summary>Median milliseconds of <see cref="Runs"/> runs after three warm-up runs.</summary>
        private static double Median(Action action)
        {
            for (int i = 0; i < 3; i++)
                action();

            var times = new double[Runs];
            for (int i = 0; i < Runs; i++)
            {
                var sw = Stopwatch.StartNew();
                action();
                times[i] = sw.Elapsed.TotalMilliseconds;
            }

            Array.Sort(times);
            return times[Runs / 2];
        }
    }
}
