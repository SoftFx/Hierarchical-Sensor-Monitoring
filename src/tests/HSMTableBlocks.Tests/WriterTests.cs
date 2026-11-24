using System.Text;
using HSMTableBlocks;
using Xunit;

namespace HSMTableBlocks.Tests
{
    public class WriterTests
    {
        private static readonly byte[] Id = Encoding.UTF8.GetBytes("id");


        [Fact]
        public void An_empty_block_is_not_written()
        {
            using var writer = new TableBlockWriter();
            Assert.Throws<InvalidOperationException>(() => writer.ToArray());
        }

        [Fact]
        public void A_table_needs_a_row_an_identifier_and_its_time_column_first()
        {
            using var writer = new TableBlockWriter();

            Assert.Throws<InvalidOperationException>(() => writer.AddTimeColumn(new[] { 1L }));
            Assert.Throws<ArgumentOutOfRangeException>(() => writer.BeginTable(Id, 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => writer.BeginTable(ReadOnlySpan<byte>.Empty, 1, 1));

            writer.BeginTable(Id, 1, 1);
            Assert.Throws<InvalidOperationException>(() => writer.AddDoubleColumn(new[] { 1.0 }));
            Assert.Throws<InvalidOperationException>(() => writer.AddInt64Column(new[] { 1L }));
            Assert.Throws<InvalidOperationException>(() => writer.ToArray());
        }

        [Fact]
        public void Every_column_has_the_table_row_count()
        {
            using var writer = new TableBlockWriter();
            writer.BeginTable(Id, 1, 2);

            Assert.Throws<ArgumentException>(() => writer.AddTimeColumn(new[] { 1L }));
            writer.AddTimeColumn(new[] { 1L, 2L });
            Assert.Throws<ArgumentException>(() => writer.AddDoubleColumn(new[] { 1.0, 2.0, 3.0 }));
        }

        [Theory]
        [InlineData(-1L)]
        [InlineData(long.MinValue)]
        [InlineData(TableBlockFormat.MaxTimeTicks + 1)]
        [InlineData(long.MaxValue)]
        public void Times_outside_the_DateTime_range_are_refused(long ticks)
        {
            using var writer = new TableBlockWriter();
            writer.BeginTable(Id, 1, 2);

            Assert.Throws<ArgumentOutOfRangeException>(() => writer.AddTimeColumn(new[] { 1L, ticks }));
        }

        [Fact]
        public void A_table_identifier_appears_once_per_block()
        {
            using var writer = new TableBlockWriter();
            writer.BeginTable(Id, 1, 1);
            writer.AddTimeColumn(new[] { 1L });
            writer.BeginTable(Encoding.UTF8.GetBytes("other"), 2, 1);
            writer.AddTimeColumn(new[] { 1L });
            writer.BeginTable(Encoding.UTF8.GetBytes("id"), 3, 1);
            writer.AddTimeColumn(new[] { 1L });

            Assert.Throws<ArgumentException>(() => writer.ToArray());

            writer.Reset();
            writer.BeginTable(Id, 1, 1);
            writer.AddTimeColumn(new[] { 1L });
            Assert.Single(TableBlockReader.Read(writer.ToArray()).Tables);
        }

        [Fact]
        public void The_writer_keeps_to_its_limits()
        {
            var limits = new TableBlockLimits(maxDecompressedBytes: 200, maxTables: 2, maxRows: 10, maxColumnsPerTable: 2, maxTableIdLength: 4);
            using var writer = new TableBlockWriter(3, limits);

            Assert.Throws<ArgumentOutOfRangeException>(() => writer.BeginTable(Encoding.UTF8.GetBytes("12345"), 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => writer.BeginTable(Id, 1, 11));

            writer.BeginTable(Id, 1, 2);
            writer.AddTimeColumn(new[] { 1L, 2L });
            writer.AddInt64Column(new[] { 1L, 2L });
            Assert.Throws<ArgumentOutOfRangeException>(() => writer.AddInt64Column(new[] { 1L, 2L }));

            writer.BeginTable(Encoding.UTF8.GetBytes("b"), 1, 8);
            writer.AddTimeColumn(new long[8]);
            Assert.Throws<ArgumentOutOfRangeException>(() => writer.BeginTable(Encoding.UTF8.GetBytes("c"), 1, 1));

            var read = TableBlockReader.Read(writer.ToArray(), limits);
            Assert.Equal(10, read.Header.RowCount);
        }

        [Fact]
        public void The_payload_limit_is_checked_before_a_column_is_copied()
        {
            using var writer = new TableBlockWriter(3, new TableBlockLimits(maxDecompressedBytes: 100));
            writer.BeginTable(Id, 1, 6);
            writer.AddTimeColumn(new long[6]);
            int before = writer.DecompressedLength;

            Assert.Throws<ArgumentOutOfRangeException>(() => writer.AddDoubleColumn(new double[6]));
            Assert.Equal(before, writer.DecompressedLength);
            Assert.Throws<ArgumentOutOfRangeException>(() => writer.BeginTable(Encoding.UTF8.GetBytes("b"), 1, 20));
        }

        [Fact]
        public void A_block_written_at_the_limits_is_read_with_the_same_limits()
        {
            var limits = new TableBlockLimits(maxDecompressedBytes: 4096, maxTables: 3, maxRows: 30, maxColumnsPerTable: 3, maxTableIdLength: 8);
            var tables = Enumerable.Range(0, 3).Select(i => new TestTable($"table-0{i}", 1, Enumerable.Repeat(Samples.T0, 10).ToArray(), new double[10], new long[10])).ToList();
            var block = Samples.Write(tables, limits: limits);

            Samples.AssertSame(tables, TableBlockReader.Read(block, limits));
        }

        [Fact]
        public void Reset_reuses_the_writer_and_writes_the_same_bytes()
        {
            var tables = Enumerable.Range(0, 50).Select(i => Samples.Quotes($"T{i}", 1 + i, (ulong)i)).ToList();
            using var writer = new TableBlockWriter();

            Samples.Add(writer, tables);
            var first = writer.ToArray();
            Assert.Equal(first, writer.ToArray());

            writer.Reset();
            Assert.Equal(0, writer.TableCount);
            Assert.Equal(0, writer.RowCount);
            Assert.Equal(0, writer.DecompressedLength);

            Samples.Add(writer, tables);
            Assert.Equal(first, writer.ToArray());
            Assert.Equal(tables.Count, writer.TableCount);
            Assert.Equal(tables.Sum(t => t.Rows), writer.RowCount);
            Assert.Equal(RawBlock.Payload(first).Length, writer.DecompressedLength);
        }

        [Fact]
        public void A_reused_writer_writes_correct_blocks_smaller_and_larger_than_the_previous_one()
        {
            // The writer keeps its payload and compressed buffers between blocks: a block after a larger one must not
            // carry its leftovers, a block after a smaller one must grow them.
            var medium = Enumerable.Range(0, 100).Select(i => Samples.Quotes($"m{i}", 50, (ulong)i)).ToList();
            var small = new[] { Samples.Quotes("s", 3, 77) };
            var large = Enumerable.Range(0, 400).Select(i => Samples.Quotes($"l{i}", 300, (ulong)i + 1000)).ToList();
            using var writer = new TableBlockWriter();

            foreach (var tables in new IReadOnlyList<TestTable>[] { medium, small, large, small })
            {
                writer.Reset();
                Samples.Add(writer, tables);
                var block = writer.ToArray();

                Samples.AssertSame(tables, TableBlockReader.Read(block));
                Assert.Equal(Samples.Write(tables), block);
            }
        }

        [Fact]
        public void Arguments_of_the_constructor_and_limits_are_checked()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TableBlockWriter(23));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TableBlockLimits(maxDecompressedBytes: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TableBlockLimits(maxDecompressedBytes: TableBlockLimits.FormatMaxDecompressedBytes + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TableBlockLimits(maxColumnsPerTable: 256));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TableBlockLimits(maxTableIdLength: 65_536));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TableBlockLimits(maxTables: -1));
        }

        [Fact]
        public void A_disposed_writer_refuses_work()
        {
            var writer = new TableBlockWriter();
            writer.Dispose();
            writer.Dispose();

            Assert.Throws<ObjectDisposedException>(() => writer.BeginTable(Id, 1, 1));
        }
    }
}
