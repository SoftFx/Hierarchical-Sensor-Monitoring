using System.IO.Hashing;
using System.Text;
using HSMTableBlocks;
using Xunit;

namespace HSMTableBlocks.Tests
{
    public class RoundTripTests
    {
        public static readonly double[] ExtremeDoubles =
        {
            0.0, -0.0, 1.0, -1.0, double.Epsilon, -double.Epsilon, double.MaxValue, double.MinValue,
            double.PositiveInfinity, double.NegativeInfinity, double.NaN,
            BitConverter.Int64BitsToDouble(0x7FF0_0000_0000_0001),          // signalling NaN, smallest payload
            BitConverter.Int64BitsToDouble(0x7FF8_DEAD_BEEF_0001),          // quiet NaN with a payload
            BitConverter.Int64BitsToDouble(unchecked((long)0xFFFF_FFFF_FFFF_FFFF)), // negative NaN, all payload bits
            BitConverter.Int64BitsToDouble(0x000F_FFFF_FFFF_FFFF),          // largest subnormal
            1.23456789012345e-300, 0.1, 1.00001,
        };

        public static readonly long[] ExtremeLongs =
        {
            0, 1, -1, long.MaxValue, long.MinValue, long.MaxValue - 1, long.MinValue + 1, int.MaxValue, int.MinValue,
            0x0102_0304_0506_0708, unchecked((long)0x8070_6050_4030_2010), 42, -42, 1L << 53, (1L << 53) + 1, 255, 256, 65_535,
        };

        public static readonly long[] ExtremeTimes =
        {
            0, 1, TableBlockFormat.MaxTimeTicks, TableBlockFormat.MaxTimeTicks - 1, Samples.T0, Samples.T0, Samples.T0 - 1,
            621_355_968_000_000_000 /* Unix epoch */, 1, 0, TableBlockFormat.MaxTimeTicks, Samples.T0 + 1, 5, 4, 3, 2, 1, Samples.T0 + 2,
        };


        [Fact]
        public void Every_column_type_round_trips_bit_exactly_with_extreme_values()
        {
            var table = new TestTable("extremes", uint.MaxValue, ExtremeTimes, ExtremeDoubles, ExtremeLongs, new TimeColumn(ExtremeTimes.Reverse().ToArray()));
            var block = Samples.Write(new[] { table });

            var read = TableBlockReader.Read(block);

            Samples.AssertSame(new[] { table }, read);
            Assert.Equal(0, read.Header.FirstTime);
            Assert.Equal(TableBlockFormat.MaxTimeTicks, read.Header.LastTime);
            Assert.Equal(DateTime.MaxValue, read.Header.LastTimeUtc);
            Assert.Equal(DateTimeKind.Utc, read.Header.FirstTimeUtc.Kind);
        }

        [Fact]
        public void One_table()
        {
            var tables = new[] { Samples.Quotes("EURUSD", 500, 1) };
            Samples.AssertSame(tables, TableBlockReader.Read(Samples.Write(tables)));
        }

        [Fact]
        public void A_thousand_tables()
        {
            var tables = Enumerable.Range(0, 1000).Select(i => Samples.Quotes($"lp{i % 7}/SYM{i:D4}", 1 + i % 50, (ulong)i + 100)).ToList();
            var block = Samples.Write(tables);

            var read = TableBlockReader.Read(block);

            Samples.AssertSame(tables, read);
            Assert.Equal(block.Length, read.Header.BlockLength);
        }

        [Fact]
        public void A_table_with_one_row_and_only_the_time_column()
        {
            var table = new TestTable("t", 0, new[] { Samples.T0 });
            var read = TableBlockReader.Read(Samples.Write(new[] { table }));

            Samples.AssertSame(new[] { table }, read);
            Assert.Equal(read.Header.FirstTime, read.Header.LastTime);
        }

        [Fact]
        public void Repeated_timestamps_and_rows_out_of_time_order_keep_their_order()
        {
            var time = new[] { Samples.T0 + 5, Samples.T0 + 5, Samples.T0 + 1, Samples.T0 + 9, Samples.T0 + 1, Samples.T0 + 5, Samples.T0 };
            var value = Enumerable.Range(0, time.Length).Select(i => (double)i).ToArray();
            var table = new TestTable("unsorted", 3, time, value);

            var read = TableBlockReader.Read(Samples.Write(new[] { table }));

            Assert.Equal(time, read.Tables[0].Time);
            Assert.Equal(value, read.Tables[0].Columns[1].DoubleValues);
            Assert.Equal(Samples.T0, read.Header.FirstTime);
            Assert.Equal(Samples.T0 + 9, read.Header.LastTime);
        }

        [Fact]
        public void Tables_keep_the_order_they_were_added_and_ids_are_opaque_bytes()
        {
            var tables = new[]
            {
                new TestTable(new byte[] { 0 }, 1, new[] { TableColumnType.Time }, new object[] { new[] { Samples.T0 } }),
                new TestTable(new byte[] { 0xFF, 0x00, 0xFE }, 2, new[] { TableColumnType.Time }, new object[] { new[] { Samples.T0 } }),
                new TestTable(Enumerable.Repeat((byte)0xAB, TableBlockLimits.DefaultMaxTableIdLength).ToArray(), 3, new[] { TableColumnType.Time }, new object[] { new[] { Samples.T0 } }),
                new TestTable(Encoding.UTF8.GetBytes("Ünïcødé/路径"), 4, new[] { TableColumnType.Time }, new object[] { new[] { Samples.T0 } }),
            };

            Samples.AssertSame(tables, TableBlockReader.Read(Samples.Write(tables)));
        }

        [Fact]
        public void ReadTable_returns_one_table_or_null()
        {
            var tables = Enumerable.Range(0, 20).Select(i => Samples.Quotes($"T{i}", 10 + i, (ulong)i)).ToList();
            var block = Samples.Write(tables);

            Samples.AssertSame(tables[13], TableBlockReader.ReadTable(block, tables[13].Id));
            Samples.AssertSame(tables[0], TableBlockReader.ReadTable(block, tables[0].Id));
            Assert.Null(TableBlockReader.ReadTable(block, Encoding.UTF8.GetBytes("T20")));
            Assert.Null(TableBlockReader.ReadTable(block, Encoding.UTF8.GetBytes("T1x")));
        }

        [Fact]
        public void ReadHeader_returns_the_fields_without_decompressing()
        {
            var tables = new[] { Samples.Quotes("A", 30, 1), Samples.Quotes("B", 12, 2) };
            var block = Samples.Write(tables);

            var header = TableBlockReader.ReadHeader(block);

            Assert.Equal(TableBlockFormat.FormatVersion, header.FormatVersion);
            Assert.Equal(42, header.RowCount);
            Assert.Equal(2, header.TableCount);
            Assert.Equal(tables.SelectMany(t => (long[])t.Columns[0]).Min(), header.FirstTime);
            Assert.Equal(tables.SelectMany(t => (long[])t.Columns[0]).Max(), header.LastTime);
            Assert.Equal(block.Length - TableBlockFormat.HeaderSize, header.CompressedLength);
            Assert.Equal(RawBlock.Payload(block).Length, header.DecompressedLength);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(19)]
        [InlineData(-5)]
        public void Every_compression_level_reads_back(int level)
        {
            var tables = new[] { Samples.Quotes("A", 300, 9) };
            Samples.AssertSame(tables, TableBlockReader.Read(Samples.Write(tables, level)));
        }

        [Fact]
        public void A_frame_without_a_declared_content_size_is_accepted()
        {
            // A streaming zstd writer (for example in C++) may leave the content size out of the frame header.
            var tables = new[] { Samples.Quotes("A", 100, 3) };
            var block = Samples.Write(tables);
            var payload = RawBlock.Payload(block);

            using var compressor = new ZstdSharp.Compressor(3);
            compressor.SetParameter(ZstdSharp.Unsafe.ZSTD_cParameter.ZSTD_c_contentSizeFlag, 0);
            var compressed = compressor.Wrap(payload).ToArray();
            Assert.Equal(ZstdFrames.ContentSizeUnknown, ZstdFrames.ContentSize(compressed));

            Samples.AssertSame(tables, TableBlockReader.Read(RawBlock.Seal(RawBlock.FieldsOf(block), compressed, payload.Length)));
        }

        [Fact]
        public void The_checksum_is_standard_crc32c()
        {
            // The CRC-32C check value (RFC 3720, iSCSI): the same algorithm as the sealed history chunks.
            Assert.Equal(0xE3069283u, Crc32.HashToUInt32(Crc32ParameterSet.Crc32C, Encoding.ASCII.GetBytes("123456789")));
        }
    }
}
