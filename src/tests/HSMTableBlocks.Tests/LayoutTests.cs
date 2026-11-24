using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using HSMTableBlocks;
using Xunit;
using T = HSMTableBlocks.TableColumnType;

namespace HSMTableBlocks.Tests
{
    /// <summary>
    /// The column-major layout of the payload, and the identifier checks at the largest default table count.
    /// </summary>
    public class LayoutTests
    {
        private static readonly long T0 = Samples.T0;


        [Fact]
        public void The_payload_is_column_major()
        {
            // a: 2 rows, columns time, int64, double. b: 1 row, time only. c: 3 rows, time, int64.
            var tables = new[]
            {
                new TestTable("a", 1, new[] { T0, T0 + 1 }, new[] { 10L, 11L }, new[] { 0.5, -0.0 }),
                new TestTable("b", 2, new[] { T0 + 2 }),
                new TestTable("c", 3, new[] { T0 + 3, T0 + 4, T0 + 5 }, new[] { 30L, 31L, 32L }),
            };
            var block = Samples.Write(tables);

            // The same payload, written by hand: the directory, then pass 0 (a.time, b.time, c.time),
            // pass 1 (a.int64, c.int64), pass 2 (a.double).
            var expected = new PayloadBuilder()
                .Entry(Encoding.UTF8.GetBytes("a"), 1, 2, (byte)T.Time, (byte)T.Int64, (byte)T.Double)
                .Entry(Encoding.UTF8.GetBytes("b"), 2, 1, (byte)T.Time)
                .Entry(Encoding.UTF8.GetBytes("c"), 3, 3, (byte)T.Time, (byte)T.Int64)
                .Values(T0, T0 + 1, T0 + 2, T0 + 3, T0 + 4, T0 + 5)
                .Values(10, 11, 30, 31, 32)
                .Values(BitConverter.DoubleToInt64Bits(0.5), BitConverter.DoubleToInt64Bits(-0.0))
                .Build();

            Assert.Equal(expected, RawBlock.Payload(block));
            Samples.AssertSame(tables, TableBlockReader.Read(block));
        }

        [Fact]
        public void Tables_with_different_column_counts_round_trip_in_any_order()
        {
            var rng = new Rng(0xC0_1A);
            var tables = new List<TestTable>();

            for (int i = 0; i < 200; i++)
            {
                int rows = 1 + rng.Next(30);
                var time = Enumerable.Range(0, rows).Select(r => T0 + rng.Next(1000)).ToArray();
                var others = Enumerable.Range(0, rng.Next(6)).Select(c => c % 2 == 0
                    ? (object)Enumerable.Range(0, rows).Select(r => (long)rng.Next()).ToArray()
                    : Enumerable.Range(0, rows).Select(r => rng.NextDouble()).ToArray()).ToArray();
                tables.Add(new TestTable($"t{i}", (uint)i, time, others));
            }

            var block = Samples.Write(tables);

            Samples.AssertSame(tables, TableBlockReader.Read(block));
            foreach (int i in new[] { 0, 57, 199 })
                Samples.AssertSame(tables[i], TableBlockReader.ReadTable(block, tables[i].Id));
        }

        [Fact]
        public void A_hand_made_column_major_block_reads_one_table_from_the_middle()
        {
            var payload = new PayloadBuilder()
                .Entry("a", 1, T.Time, T.Int64)
                .Entry("b", 2, T.Time, T.Int64, T.Int64)
                .Entry("c", 1, T.Time)
                .Values(T0, T0 + 1, T0 + 2, T0 + 3)   // pass 0: a, b, b, c
                .Values(100, 200, 201)                  // pass 1: a, b, b
                .Values(300, 301)                       // pass 2: b, b
                .Build();
            var block = RawBlock.Seal(new RawBlock.Fields { RowCount = 4, TableCount = 3, FirstTime = T0, LastTime = T0 + 3 }, payload);

            var b = TableBlockReader.ReadTable(block, Encoding.UTF8.GetBytes("b"));

            Assert.Equal(new[] { T0 + 1, T0 + 2 }, b.Time);
            Assert.Equal(new[] { 200L, 201L }, b.Columns[1].Int64Values);
            Assert.Equal(new[] { 300L, 301L }, b.Columns[2].Int64Values);
        }

        /// <summary>
        /// 65,536 tables (the default limit) whose 1,000-byte identifiers share a 995-byte prefix: the uniqueness check
        /// sorts by identifier bytes, so this costs O(n log n) comparisons. A duplicate among them is still found.
        /// </summary>
        [Fact]
        public void The_largest_default_table_count_with_long_shared_prefixes_reads_in_bounded_time()
        {
            const int count = TableBlockLimits.DefaultMaxTables;
            const int idLength = 1000;
            var prefix = Enumerable.Repeat((byte)'p', idLength - 5).ToArray();
            byte[] Id(int i) => prefix.Concat(Encoding.ASCII.GetBytes(i.ToString("D5"))).ToArray();

            byte[] block;
            var write = Stopwatch.StartNew();
            using (var writer = new TableBlockWriter())
            {
                var time = new[] { T0 };
                for (int i = 0; i < count; i++)
                {
                    writer.BeginTable(Id(i), 1, 1);
                    writer.AddTimeColumn(time);
                }

                block = writer.ToArray();
            }
            write.Stop();

            var read = Stopwatch.StartNew();
            var result = TableBlockReader.Read(block);
            read.Stop();

            Assert.Equal(count, result.Tables.Count);
            Assert.True(write.Elapsed < TimeSpan.FromSeconds(30), $"write took {write.Elapsed}");
            Assert.True(read.Elapsed < TimeSpan.FromSeconds(30), $"read took {read.Elapsed}");

            // The last identifier made equal to the first: found, in the same bounded time.
            var payload = RawBlock.Payload(block);
            int lastIdOffset = (count - 1) * (TableBlockFormat.EntryFixedSize + idLength + 1) + 2;
            Assert.Equal(Id(count - 1), payload.Skip(lastIdOffset).Take(idLength).ToArray());
            Id(0).CopyTo(payload, lastIdOffset);
            var duplicate = RawBlock.Seal(RawBlock.FieldsOf(block), payload);

            var refused = Stopwatch.StartNew();
            Samples.AssertRefused(TableBlockError.DuplicateTable, duplicate);
            Assert.True(refused.Elapsed < TimeSpan.FromSeconds(60), $"refusing took {refused.Elapsed}");
        }
    }
}
