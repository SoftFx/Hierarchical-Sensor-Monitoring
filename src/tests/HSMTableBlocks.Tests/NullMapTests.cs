using HSMTableBlocks;
using Xunit;
using T = HSMTableBlocks.TableColumnType;

namespace HSMTableBlocks.Tests
{
    /// <summary>
    /// Null maps (#1859): blocks made by hand with a valid checksum, so each case reaches the check it is about. Every
    /// refusal is asserted by its code through <c>Read</c>, <c>ReadTable</c> and <c>ReadDirectory</c> (the put's check).
    /// </summary>
    public class NullMapTests
    {
        private const byte Flag = 0x80;
        private static readonly long T0 = Samples.T0;
        private static readonly long Bits15 = BitConverter.DoubleToInt64Bits(1.5);
        private static readonly long Bits25 = BitConverter.DoubleToInt64Bits(2.5);

        private static byte[] Block(PayloadBuilder payload, int rows = 2, int tables = 1, long first = 0, long last = 0) =>
            RawBlock.Seal(new RawBlock.Fields { RowCount = rows, TableCount = tables, FirstTime = first, LastTime = last }, payload.Build());

        // Table "a": two rows, time and a double column with a map.
        private static PayloadBuilder TwoRows(byte valueType = (byte)T.Double | Flag) =>
            new PayloadBuilder().Entry(new byte[] { (byte)'a' }, 7, 2, (byte)T.Time, valueType);


        [Fact]
        public void A_hand_made_block_with_a_null_map_reads_its_null()
        {
            var block = Block(TwoRows().Values(T0, T0 + 1, Bits15, 0).Maps(0x02), first: T0, last: T0 + 1);

            var column = TableBlockReader.Read(block).Tables[0].Columns[1];

            Assert.Equal(T.Double, column.Type);
            Assert.True(column.HasNulls);
            Assert.False(column.IsNull(0));
            Assert.True(column.IsNull(1));
            Assert.Equal(new byte[] { 0x02 }, column.NullMap);
            Assert.Equal(new[] { 1.5, 0.0 }, column.DoubleValues);
        }

        [Fact]
        public void A_map_on_the_time_column_is_refused()
        {
            var payload = new PayloadBuilder().Entry(new byte[] { (byte)'a' }, 7, 2, (byte)T.Time | Flag, (byte)T.Double)
                .Values(T0, 0, Bits15, Bits25).Maps(0x02);

            var ex = Samples.AssertRefused(TableBlockError.CorruptNullMap, Block(payload, first: 0, last: T0));
            Assert.Contains("column 0", ex.Message);
        }

        [Fact]
        public void A_map_on_a_later_time_column_is_allowed()
        {
            var payload = new PayloadBuilder().Entry(new byte[] { (byte)'a' }, 7, 2, (byte)T.Time, (byte)T.Time | Flag)
                .Values(T0, T0 + 1, 0, T0).Maps(0x01);

            var column = TableBlockReader.Read(Block(payload, first: T0, last: T0 + 1)).Tables[0].Columns[1];

            Assert.Equal(T.Time, column.Type);
            Assert.True(column.IsNull(0));
            Assert.False(column.IsNull(1));
        }

        [Fact]
        public void Padding_bits_set_are_refused()
        {
            // Two rows: bits 2..7 are padding.
            Samples.AssertRefused(TableBlockError.CorruptNullMap, Block(TwoRows().Values(T0, T0 + 1, Bits15, 0).Maps(0x06), first: T0, last: T0 + 1));
            Samples.AssertRefused(TableBlockError.CorruptNullMap, Block(TwoRows().Values(T0, T0 + 1, Bits15, 0).Maps(0x82), first: T0, last: T0 + 1));

            // Nine rows: the second byte holds row 8 and seven padding bits.
            var nine = Enumerable.Range(0, 9).Select(i => T0 + i).ToArray();
            var values = Enumerable.Repeat(Bits15, 8).Concat(new[] { 0L }).ToArray();
            PayloadBuilder Nine() => new PayloadBuilder().Entry(new byte[] { (byte)'a' }, 7, 9, (byte)T.Time, (byte)T.Double | Flag).Values(nine).Values(values);

            TableBlockReader.Read(Block(Nine().Maps(0x00, 0x01), rows: 9, first: T0, last: T0 + 8));
            Samples.AssertRefused(TableBlockError.CorruptNullMap, Block(Nine().Maps(0x00, 0x03), rows: 9, first: T0, last: T0 + 8));
            Samples.AssertRefused(TableBlockError.CorruptNullMap, Block(Nine().Maps(0x00, 0x81), rows: 9, first: T0, last: T0 + 8));
        }

        [Fact]
        public void A_map_without_a_null_row_is_refused()
        {
            var ex = Samples.AssertRefused(TableBlockError.CorruptNullMap, Block(TwoRows().Values(T0, T0 + 1, Bits15, Bits25).Maps(0x00), first: T0, last: T0 + 1));
            Assert.Contains("no null", ex.Message);
        }

        [Fact]
        public void A_null_cell_that_is_not_zero_is_refused()
        {
            var ex = Samples.AssertRefused(TableBlockError.CorruptNullMap, Block(TwoRows().Values(T0, T0 + 1, Bits15, Bits25).Maps(0x02), first: T0, last: T0 + 1));
            Assert.Contains("row 1", ex.Message);

            // -0.0 is not zero bytes.
            Samples.AssertRefused(TableBlockError.CorruptNullMap,
                Block(TwoRows().Values(T0, T0 + 1, Bits15, BitConverter.DoubleToInt64Bits(-0.0)).Maps(0x02), first: T0, last: T0 + 1));
        }

        [Fact]
        public void Map_lengths_that_do_not_add_up_are_refused()
        {
            // The flag is set and the map is missing.
            Samples.AssertRefused(TableBlockError.DirectoryOutOfBounds, Block(TwoRows().Values(T0, T0 + 1, Bits15, 0), first: T0, last: T0 + 1));

            // A byte after the last map.
            Samples.AssertRefused(TableBlockError.LengthMismatch, Block(TwoRows().Values(T0, T0 + 1, Bits15, 0).Maps(0x02, 0x00), first: T0, last: T0 + 1));

            // A map without its flag.
            Samples.AssertRefused(TableBlockError.LengthMismatch, Block(TwoRows((byte)T.Double).Values(T0, T0 + 1, Bits15, 0).Maps(0x02), first: T0, last: T0 + 1));
        }

        [Fact]
        public void Maps_follow_the_columns_pass_by_pass_and_table_by_table()
        {
            // a: 2 rows, row 1 null in column 1. b: 9 rows, row 8 null in column 1. The maps follow in directory order;
            // swapped, a's map would read 0x00 (no null row) and b's would be one byte short.
            PayloadBuilder Two() => new PayloadBuilder()
                .Entry(new byte[] { (byte)'a' }, 7, 2, (byte)T.Time, (byte)T.Int64 | Flag)
                .Entry(new byte[] { (byte)'b' }, 7, 9, (byte)T.Time, (byte)T.Int64 | Flag)
                .Values(T0, T0, T0, T0, T0, T0, T0, T0, T0, T0, T0)
                .Values(5, 0, 1, 2, 3, 4, 5, 6, 7, 8, 0);

            var read = TableBlockReader.Read(Block(Two().Maps(0x02, 0x00, 0x01), rows: 11, tables: 2, first: T0, last: T0));
            Assert.True(read.Tables[0].Columns[1].IsNull(1));
            Assert.True(read.Tables[1].Columns[1].IsNull(8));
            Assert.Equal(1, Enumerable.Range(0, 9).Count(read.Tables[1].Columns[1].IsNull));

            Samples.AssertRefused(TableBlockError.CorruptNullMap, Block(Two().Maps(0x00, 0x01, 0x02), rows: 11, tables: 2, first: T0, last: T0));
        }

        // === Writer and round trips ===

        [Fact]
        public void The_worked_example_of_the_specification_byte_for_byte()
        {
            // a: (t1, 1.5), (t2, null). b: (t3, 2.5). The specification gives this payload, 77 bytes.
            long t1 = T0, t2 = T0 + 1, t3 = T0 + 2;
            var tables = new[]
            {
                new TestTable("a", 7, new[] { t1, t2 }, new[] { 1.5, 99.0 }).WithNulls(1, 1),
                new TestTable("b", 7, new[] { t3 }, new[] { 2.5 }),
            };

            var expected = new byte[] { 0x01, 0x00, 0x61, 0x07, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x02, 0x01, 0x83,
                                        0x01, 0x00, 0x62, 0x07, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02, 0x01, 0x03 }
                .Concat(BitConverter.GetBytes(t1)).Concat(BitConverter.GetBytes(t2)).Concat(BitConverter.GetBytes(t3))
                .Concat(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF8, 0x3F })
                .Concat(new byte[8])
                .Concat(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04, 0x40 })
                .Concat(new byte[] { 0x02 })
                .ToArray();

            var block = Samples.Write(tables);

            Assert.Equal(77, expected.Length);
            Assert.Equal(expected, RawBlock.Payload(block));
            Assert.Equal(77, TableBlockReader.ReadHeader(block).DecompressedLength);
            Samples.AssertSame(tables, TableBlockReader.Read(block));
        }

        [Fact]
        public void Nulls_round_trip_in_every_nullable_column_with_extreme_values_around_them()
        {
            var nine = Enumerable.Range(0, 9).Select(i => T0 + i).ToArray();
            var all = Enumerable.Range(0, 9).ToArray();
            var tables = new[]
            {
                // Extremes under and beside nulls; a later time column with a null; an int64 column null in every row.
                new TestTable("extremes", 1, RoundTripTests.ExtremeTimes, RoundTripTests.ExtremeDoubles, RoundTripTests.ExtremeLongs,
                        new TimeColumn(RoundTripTests.ExtremeTimes.Reverse().ToArray()))
                    .WithNulls(1, 0, 1, 10, 17).WithNulls(2, 3, 4).WithNulls(3, 17),
                new TestTable("nine", 2, nine, Enumerable.Repeat(-1L, 9).ToArray(), new double[9]).WithNulls(1, all).WithNulls(2, 8),
                new TestTable("eight", 3, nine.Take(8).ToArray(), Enumerable.Range(0, 8).Select(i => i + 0.5).ToArray()).WithNulls(1, 7),
                new TestTable("one", 4, new[] { T0 }, new[] { 5L }, new[] { 6.5 }).WithNulls(1, 0).WithNulls(2, 0), // a row of nulls only
                new TestTable("dense", 5, nine, Enumerable.Range(0, 9).Select(i => (long)i).ToArray()),
            };

            var block = Samples.Write(tables);
            var read = TableBlockReader.Read(block);

            Samples.AssertSame(tables, read);
            Assert.False(read.Tables[4].Columns[1].HasNulls);
            Assert.Null(read.Tables[4].Columns[1].NullMap);

            var single = TableBlockReader.ReadTable(block, read.Tables[1].TableId);
            Samples.AssertSame(tables[1], single);
        }

        [Fact]
        public void A_block_without_nulls_is_written_the_same_through_every_overload()
        {
            var tables = Enumerable.Range(0, 20).Select(i => Samples.Quotes($"T{i}", 1 + i, (ulong)i)).ToList();
            var plain = Samples.Write(tables);

            // Empty maps, and maps of the right length with no row set: no flag, no map, the same bytes.
            using var writer = new TableBlockWriter();
            foreach (var zeroed in new[] { false, true })
            {
                writer.Reset();
                foreach (var t in tables)
                {
                    var empty = zeroed ? new byte[TableNullMap.ByteLength(t.Rows)] : Array.Empty<byte>();
                    writer.BeginTable(t.Id, t.SchemaId, t.Rows);
                    writer.AddTimeColumn((long[])t.Columns[0]);
                    writer.AddDoubleColumn((double[])t.Columns[1], empty);
                    writer.AddDoubleColumn((double[])t.Columns[2], empty);
                    writer.AddInt64Column((long[])t.Columns[3], empty);
                }

                Assert.Equal(plain, writer.ToArray());
            }
        }

        [Fact]
        public void The_first_column_takes_a_map_that_marks_no_row_as_every_column_does()
        {
            // A collector that keeps a map buffer per column passes one for the time too: all zero, it is no map.
            var tables = new[] { Samples.Quotes("q", 9, 11), Samples.Quotes("r", 1, 12) };
            var plain = Samples.Write(tables);

            using var writer = new TableBlockWriter();
            foreach (var t in tables)
            {
                writer.BeginTable(t.Id, t.SchemaId, t.Rows);
                writer.AddTimeColumn((long[])t.Columns[0], new byte[TableNullMap.ByteLength(t.Rows)]);
                writer.AddDoubleColumn((double[])t.Columns[1]);
                writer.AddDoubleColumn((double[])t.Columns[2]);
                writer.AddInt64Column((long[])t.Columns[3]);
            }

            Assert.Equal(plain, writer.ToArray());
        }

        [Fact]
        public void The_first_column_refuses_a_marked_row_and_a_map_of_the_wrong_length()
        {
            using var writer = new TableBlockWriter();
            writer.BeginTable(new byte[] { 1 }, 1, 9);
            var ticks = Enumerable.Repeat(T0, 9).ToArray();

            Assert.Throws<InvalidOperationException>(() => writer.AddTimeColumn(ticks, new byte[] { 0, 0x01 }));
            Assert.Throws<ArgumentException>(() => writer.AddTimeColumn(ticks, new byte[1]));
            Assert.Throws<ArgumentException>(() => writer.AddTimeColumn(ticks, new byte[3]));
            Assert.Throws<ArgumentException>(() => writer.AddTimeColumn(ticks, new byte[] { 0, 0x02 }));

            // Nothing was added: the time column can still be added.
            writer.AddTimeColumn(ticks);
            Assert.Single(TableBlockReader.Read(writer.ToArray()).Tables);
        }

        [Fact]
        public void The_writer_checks_its_null_maps()
        {
            using var writer = new TableBlockWriter();
            writer.BeginTable(new byte[] { 1 }, 1, 9);

            // The time of a row is never null.
            Assert.Throws<InvalidOperationException>(() => writer.AddTimeColumn(new long[9], new byte[] { 0x01, 0 }));
            writer.AddTimeColumn(Enumerable.Repeat(T0, 9).ToArray());

            Assert.Throws<ArgumentException>(() => writer.AddInt64Column(new long[9], new byte[1]));
            Assert.Throws<ArgumentException>(() => writer.AddInt64Column(new long[9], new byte[3]));
            Assert.Throws<ArgumentException>(() => writer.AddDoubleColumn(new double[9], new byte[] { 0, 0x02 }));
            Assert.Throws<ArgumentException>(() => writer.AddTimeColumn(new long[9], new byte[] { 0, 0x80 }));

            // A time under a null is not checked: it is written as 0.
            writer.AddTimeColumn(Enumerable.Repeat(-1L, 9).ToArray(), Enumerable.Repeat((byte)0xFF, 1).Concat(new byte[] { 0x01 }).ToArray());
            var column = TableBlockReader.Read(writer.ToArray()).Tables[0].Columns[1];
            Assert.Equal(new long[9], column.Int64Values);
            Assert.True(Enumerable.Range(0, 9).All(column.IsNull));
        }

        [Fact]
        public void The_null_maps_count_toward_the_payload_limit()
        {
            // 9 rows: a 12-byte entry with a 1-byte identifier, two type bytes, the time and one int64 column (144
            // bytes), plus a 2-byte map: 160 bytes. One byte less refuses the column with its map.
            var limits = new TableBlockLimits(maxDecompressedBytes: 159);
            using var writer = new TableBlockWriter(3, limits);
            writer.BeginTable(new byte[] { 1 }, 1, 9);
            writer.AddTimeColumn(Enumerable.Repeat(T0, 9).ToArray());
            int before = writer.DecompressedLength;

            Assert.Throws<ArgumentOutOfRangeException>(() => writer.AddInt64Column(new long[9], new byte[] { 1, 0 }));
            Assert.Equal(before, writer.DecompressedLength);

            using var roomy = new TableBlockWriter(3, new TableBlockLimits(maxDecompressedBytes: 160));
            roomy.BeginTable(new byte[] { 1 }, 1, 9);
            roomy.AddTimeColumn(Enumerable.Repeat(T0, 9).ToArray());
            roomy.AddInt64Column(new long[9], new byte[] { 1, 0 });
            Assert.Equal(160, roomy.DecompressedLength);
            Assert.Single(TableBlockReader.Read(roomy.ToArray(), roomy.Limits).Tables);
        }

        [Fact]
        public void A_reused_writer_forgets_the_maps_of_the_previous_block()
        {
            var withNulls = new[] { Samples.Quotes("q", 30, 5).WithNulls(1, 3, 29).WithNulls(3, 0) };
            var without = new[] { Samples.Quotes("q", 30, 5) };
            using var writer = new TableBlockWriter();

            foreach (var tables in new[] { withNulls, without, withNulls })
            {
                writer.Reset();
                Samples.Add(writer, tables);
                var block = writer.ToArray();

                Samples.AssertSame(tables, TableBlockReader.Read(block));
                Assert.Equal(Samples.Write(tables), block);
            }
        }

        [Fact]
        public void Adding_columns_with_nulls_allocates_nothing_on_a_reused_writer()
        {
            var tables = Enumerable.Range(0, 200).Select(i => Samples.Quotes($"T{i}", 1 + i % 40, (ulong)i).WithNulls(1, 0).WithNulls(2, i % (1 + i % 40))).ToList();
            var maps = tables.Select(t => (bid: Samples.NullMap(t.Rows, t.NullRows[1]), ask: Samples.NullMap(t.Rows, t.NullRows[2]))).ToList();
            using var writer = new TableBlockWriter();

            void AddAll()
            {
                writer.Reset();
                for (int i = 0; i < tables.Count; i++)
                {
                    var t = tables[i];
                    writer.BeginTable(t.Id, t.SchemaId, t.Rows);
                    writer.AddTimeColumn((long[])t.Columns[0]);
                    writer.AddDoubleColumn((double[])t.Columns[1], maps[i].bid);
                    writer.AddDoubleColumn((double[])t.Columns[2], maps[i].ask);
                    writer.AddInt64Column((long[])t.Columns[3]);
                }
            }

            AddAll();
            writer.ToArray();

            long allocated = MalformedBlockTests.Allocated(AddAll);
            MalformedBlockTests.AssertAllocatedAtMost(allocated, 0);
        }

        [Fact]
        public void The_directory_gives_plain_types_and_which_columns_have_nulls()
        {
            var schema = new TableSchema(new[]
            {
                new TableSchemaColumn("time", T.Time), new TableSchemaColumn("bid", T.Double),
                new TableSchemaColumn("ask", T.Double), new TableSchemaColumn("size", T.Int64),
            });
            var block = Samples.Write(new[] { Samples.Quotes("q", 10, 3, schema.Id).WithNulls(2, 4) });

            var entry = TableBlockReader.ReadDirectory(block).Entries[0];

            // The types compare with the schema as they are: what the put checks.
            Assert.True(schema.HasColumnTypes(entry.ColumnTypes));
            Assert.Equal(new[] { false, false, true, false }, Enumerable.Range(0, 4).Select(entry.HasNulls));
            Assert.Throws<ArgumentOutOfRangeException>(() => entry.HasNulls(4));

            var dense = TableBlockReader.ReadDirectory(Samples.Write(new[] { Samples.Quotes("q", 10, 3) })).Entries[0];
            Assert.False(Enumerable.Range(0, 4).Any(dense.HasNulls));
        }


        [Fact]
        public void Flagged_columns_whose_size_overflows_32_bits_are_refused_from_the_directory()
        {
            // 1,100,000 rows of 255 flagged columns: past int.MaxValue with or without the maps; counted in 64 bits.
            const int rows = 1_100_000;
            var types = new[] { (byte)T.Time }.Concat(Enumerable.Repeat((byte)((byte)T.Int64 | Flag), 254)).ToArray();
            var wide = new PayloadBuilder().Entry(new byte[] { 1 }, 1, rows, types).Values(new long[rows]);

            Samples.AssertRefused(TableBlockError.DirectoryOutOfBounds, Block(wide, rows: rows, first: 0, last: 0), new TableBlockLimits(maxColumnsPerTable: 255));
        }
    }
}
