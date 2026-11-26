using System.Buffers.Binary;
using HSMTableBlocks;
using Xunit;
using ZstdSharp;
using T = HSMTableBlocks.TableColumnType;

namespace HSMTableBlocks.Tests
{
    /// <summary>
    /// Every malformed block is refused with <see cref="TableBlockFormatException"/> and the expected code, by the full
    /// read and by the single-table read. Blocks are built by hand (<see cref="RawBlock"/>, <see cref="PayloadBuilder"/>)
    /// with a correct checksum, so each case reaches the check it is about.
    /// </summary>
    public class MalformedBlockTests
    {
        private static readonly long T0 = Samples.T0;

        private static byte[] Valid() => Samples.Write(new[] { Samples.Quotes("A", 5, 1), Samples.Quotes("BB", 3, 2) });

        /// <summary>A block around a hand-made payload: one table "a" of <paramref name="rows"/> rows unless overridden.</summary>
        private static byte[] Block(PayloadBuilder payload, int rows = 2, int tables = 1, long first = 0, long last = 0) =>
            RawBlock.Seal(new RawBlock.Fields { RowCount = rows, TableCount = tables, FirstTime = first, LastTime = last }, payload.Build());

        private static PayloadBuilder TwoRows() => new PayloadBuilder().Entry("a", 2, T.Time, T.Double);


        [Fact]
        public void A_hand_made_block_is_valid_so_the_cases_below_test_one_fault_each()
        {
            var block = Block(TwoRows().Values(T0, T0 + 1, 0, 0), first: T0, last: T0 + 1);

            var read = TableBlockReader.Read(block);
            Assert.Equal(new[] { T0, T0 + 1 }, read.Tables[0].Time);
        }

        [Fact]
        public void Truncated_at_every_length()
        {
            var block = Valid();

            for (int length = 0; length < block.Length; length++)
                Samples.AssertRefused(TableBlockError.Truncated, block.Take(length).ToArray());
        }

        [Fact]
        public void Bytes_after_the_frame()
        {
            Samples.AssertRefused(TableBlockError.LengthMismatch, Valid().Concat(new byte[] { 0 }).ToArray());
        }

        [Fact]
        public void Wrong_magic_and_unknown_version()
        {
            var block = Valid();
            block[0] = (byte)'X';
            Samples.AssertRefused(TableBlockError.BadMagic, block);

            block = Valid();
            block[4] = 2;
            RawBlock.Reseal(block);
            Samples.AssertRefused(TableBlockError.UnknownVersion, block);

            block = Valid();
            block[4] = 0;
            Samples.AssertRefused(TableBlockError.UnknownVersion, block);
        }

        [Theory]
        [InlineData(0)]   // a header byte
        [InlineData(9)]   // the row count
        [InlineData(20)]  // the first time
        [InlineData(37)]  // the decompressed length
        [InlineData(40)]  // the checksum itself
        [InlineData(43)]
        [InlineData(44)]  // the first frame byte
        [InlineData(-1)]  // the last frame byte
        public void Damaged_checksum_or_damaged_bytes_under_it(int offset)
        {
            var block = Valid();
            if (offset == 0)
                offset = 6; // a reserved byte: the magic would fail first
            if (offset < 0)
                offset = block.Length + offset;

            block[offset] ^= 0x10;
            Samples.AssertRefused(TableBlockError.ChecksumMismatch, block);
        }

        [Theory]
        [InlineData(+1, TableBlockError.Truncated)]
        [InlineData(-1, TableBlockError.LengthMismatch)]
        public void A_compressed_length_that_does_not_match_the_bytes(int delta, TableBlockError error)
        {
            var block = Valid();
            int declared = BinaryPrimitives.ReadInt32LittleEndian(block.AsSpan(32));
            BinaryPrimitives.WriteInt32LittleEndian(block.AsSpan(32), declared + delta);
            RawBlock.Reseal(block);

            Samples.AssertRefused(error, block);
        }

        [Theory]
        [InlineData(0, TableBlockError.CorruptHeader)]
        [InlineData(-1, TableBlockError.CorruptHeader)]
        [InlineData(int.MinValue, TableBlockError.CorruptHeader)]
        public void A_compressed_length_that_is_not_positive(int declared, TableBlockError error)
        {
            var block = Valid();
            BinaryPrimitives.WriteInt32LittleEndian(block.AsSpan(32), declared);
            RawBlock.Reseal(block);

            Samples.AssertRefused(error, block);
        }

        [Theory]
        [InlineData(+1)]
        [InlineData(-1)]
        [InlineData(+1000)]
        public void A_decompressed_length_that_disagrees_with_the_frame(int delta)
        {
            var block = Valid();
            var payload = RawBlock.Payload(block);
            var fields = RawBlock.FieldsOf(block);
            fields.DecompressedLength = payload.Length + delta;

            Samples.AssertRefused(TableBlockError.LengthMismatch, RawBlock.Seal(fields, payload));
        }

        [Fact]
        public void A_decompressed_length_that_disagrees_with_a_frame_without_a_content_size()
        {
            var block = Valid();
            var payload = RawBlock.Payload(block);
            var fields = RawBlock.FieldsOf(block);

            using var compressor = new Compressor(3);
            compressor.SetParameter(ZstdSharp.Unsafe.ZSTD_cParameter.ZSTD_c_contentSizeFlag, 0);
            var compressed = compressor.Wrap(payload).ToArray();

            Samples.AssertRefused(TableBlockError.LengthMismatch, RawBlock.Seal(fields, compressed, payload.Length + 8));
            Samples.AssertRefused(TableBlockError.LengthMismatch, RawBlock.Seal(fields, compressed, payload.Length - 8));
        }

        [Fact]
        public void Header_fields_out_of_range()
        {
            var good = TwoRows().Values(T0, T0 + 1, 0, 0);

            var reserved = new RawBlock.Fields { RowCount = 2, TableCount = 1, FirstTime = T0, LastTime = T0 + 1, Reserved = new byte[] { 0, 0, 1 } };
            Samples.AssertRefused(TableBlockError.CorruptHeader, RawBlock.Seal(reserved, good.Build()));

            Samples.AssertRefused(TableBlockError.CorruptHeader, Block(good, rows: 2, tables: 0, first: T0, last: T0 + 1));
            Samples.AssertRefused(TableBlockError.CorruptHeader, Block(good, rows: 2, tables: -1, first: T0, last: T0 + 1));
            Samples.AssertRefused(TableBlockError.CorruptHeader, Block(good, rows: 0, tables: 1, first: T0, last: T0 + 1));
            Samples.AssertRefused(TableBlockError.CorruptHeader, Block(good, rows: -5, tables: 1, first: T0, last: T0 + 1));
            Samples.AssertRefused(TableBlockError.CorruptHeader, Block(good, first: T0 + 1, last: T0));
            Samples.AssertRefused(TableBlockError.CorruptHeader, Block(good, first: -1, last: T0));
            Samples.AssertRefused(TableBlockError.CorruptHeader, Block(good, first: T0, last: TableBlockFormat.MaxTimeTicks + 1));
        }

        [Fact]
        public void Header_counts_that_do_not_fit_the_declared_length()
        {
            // 1,000 rows need at least 8,000 bytes: refused from the header, before anything is decompressed.
            Samples.AssertRefused(TableBlockError.LengthMismatch, Block(TwoRows().Values(T0, T0, 0, 0), rows: 1000, first: T0, last: T0));
            Samples.AssertRefused(TableBlockError.LengthMismatch, Block(TwoRows().Values(T0, T0, 0, 0), rows: 1000, tables: 1000, first: T0, last: T0));

            var limits = new TableBlockLimits(maxRows: int.MaxValue, maxTables: int.MaxValue);
            Samples.AssertRefused(TableBlockError.LengthMismatch, Block(TwoRows().Values(T0, T0, 0, 0), rows: int.MaxValue, tables: int.MaxValue, first: T0, last: T0), limits);
        }

        [Fact]
        public void Header_and_content_that_disagree()
        {
            var values = TwoRows().Values(T0, T0 + 1, 0, 0);

            Samples.AssertRefused(TableBlockError.HeaderMismatch, Block(values, rows: 3, first: T0, last: T0 + 1));
            Samples.AssertRefused(TableBlockError.HeaderMismatch, Block(TwoRows().Values(T0, T0 + 1, 0, 0), first: T0 - 1, last: T0 + 1));
            Samples.AssertRefused(TableBlockError.HeaderMismatch, Block(TwoRows().Values(T0, T0 + 1, 0, 0), first: T0, last: T0 + 2));
        }

        [Fact]
        public void A_directory_that_points_outside_the_payload()
        {
            // Five columns declared, two present.
            Samples.AssertRefused(TableBlockError.DirectoryOutOfBounds,
                Block(new PayloadBuilder().Entry("a", 2, T.Time, T.Double, T.Double, T.Double, T.Double).Values(T0, T0 + 1, 0, 0), first: T0, last: T0 + 1));

            // An identifier longer than the payload.
            Samples.AssertRefused(TableBlockError.DirectoryOutOfBounds,
                Block(new PayloadBuilder().Raw(0xFF, 0x03, (byte)'a').Values(T0, T0 + 1, 0, 0), first: T0, last: T0 + 1));

            // A column type list that runs past the payload: 60 types declared, 36 bytes left.
            Samples.AssertRefused(TableBlockError.DirectoryOutOfBounds,
                Block(new PayloadBuilder().Raw(1, 0, (byte)'a', 1, 0, 0, 0, 2, 0, 0, 0, 60).Values(0, 0, 0), first: T0, last: T0));

            // A second table whose columns lie past the payload: 38 rows of 255 columns.
            var types = new[] { (byte)T.Time }.Concat(Enumerable.Repeat((byte)T.Int64, 254)).ToArray();
            var second = new PayloadBuilder()
                .Entry(new byte[] { 1 }, 1, 2, (byte)T.Time)
                .Entry(new byte[] { 2 }, 1, 38, types)
                .Values(Enumerable.Repeat(T0, 40).ToArray());
            Samples.AssertRefused(TableBlockError.DirectoryOutOfBounds, Block(second, rows: 40, tables: 2, first: T0, last: T0), new TableBlockLimits(maxColumnsPerTable: 255));
        }

        [Fact]
        public void Row_counts_that_overflow()
        {
            // int.MaxValue rows of 255 columns: the byte count overflows 32 bits; the reader counts in 64 bits.
            var limits = new TableBlockLimits(maxColumnsPerTable: 255);
            var types = new[] { (byte)T.Time }.Concat(Enumerable.Repeat((byte)T.Int64, 254)).ToArray();
            var payload = new PayloadBuilder().Entry(new byte[] { 1 }, 1, int.MaxValue, types).Values(Enumerable.Repeat(T0, 300).ToArray());
            Samples.AssertRefused(TableBlockError.HeaderMismatch, Block(payload, rows: 2, first: T0, last: T0), limits);

            // 1,100,000 rows of 255 columns need 2,244,000,000 bytes: past int.MaxValue. The header is consistent (the
            // payload holds a time value for every row), so only the directory check in 64 bits refuses it.
            const int rows = 1_100_000;
            var wide = new PayloadBuilder().Entry(new byte[] { 1 }, 1, rows, types).Values(new long[rows]);
            Samples.AssertRefused(TableBlockError.DirectoryOutOfBounds, Block(wide, rows: rows, first: 0, last: 0), limits);

            Samples.AssertRefused(TableBlockError.CorruptDirectory,
                Block(new PayloadBuilder().Entry(new byte[] { 1 }, 1, -2, (byte)T.Time).Values(T0, T0), first: T0, last: T0));
        }

        [Fact]
        public void Invalid_directory_entries()
        {
            var two = new[] { T0, T0 + 1, 0L, 0L };

            Samples.AssertRefused(TableBlockError.CorruptDirectory, Block(new PayloadBuilder().Entry(new byte[0], 1, 2, (byte)T.Time, (byte)T.Double).Values(two), first: T0, last: T0 + 1));
            Samples.AssertRefused(TableBlockError.CorruptDirectory, Block(new PayloadBuilder().Entry("a", 0, T.Time, T.Double).Values(two), first: T0, last: T0 + 1));
            Samples.AssertRefused(TableBlockError.CorruptDirectory, Block(new PayloadBuilder().Entry("a", 2).Values(two), first: T0, last: T0 + 1));
            Samples.AssertRefused(TableBlockError.CorruptDirectory, Block(new PayloadBuilder().Entry("a", 2, T.Double, T.Time).Values(two), first: T0, last: T0 + 1));
            Samples.AssertRefused(TableBlockError.CorruptDirectory, Block(new PayloadBuilder().Entry(new byte[] { 1 }, 1, 2, (byte)T.Time, 0).Values(two), first: T0, last: T0 + 1));
            Samples.AssertRefused(TableBlockError.CorruptDirectory, Block(new PayloadBuilder().Entry(new byte[] { 1 }, 1, 2, (byte)T.Time, 4).Values(two), first: T0, last: T0 + 1));
        }

        [Fact]
        public void Bytes_after_the_last_column()
        {
            Samples.AssertRefused(TableBlockError.LengthMismatch, Block(TwoRows().Values(T0, T0 + 1, 0, 0, 0), first: T0, last: T0 + 1));
        }

        [Fact]
        public void Duplicate_table_identifiers()
        {
            var payload = new PayloadBuilder()
                .Entry("a", 1, T.Time).Entry("b", 1, T.Time).Entry("a", 1, T.Time)
                .Values(T0, T0, T0);

            Samples.AssertRefused(TableBlockError.DuplicateTable, Block(payload, rows: 3, tables: 3, first: T0, last: T0));
        }

        [Theory]
        [InlineData(-1L)]
        [InlineData(long.MinValue)]
        [InlineData(TableBlockFormat.MaxTimeTicks + 1)]
        public void Times_out_of_range(long bad)
        {
            // In the first column, and in a later time column.
            Samples.AssertRefused(TableBlockError.InvalidTime, Block(new PayloadBuilder().Entry("a", 2, T.Time).Values(T0, bad), first: T0, last: T0));
            Samples.AssertRefused(TableBlockError.InvalidTime, Block(new PayloadBuilder().Entry("a", 2, T.Time, T.Time).Values(T0, T0, T0, bad), first: T0, last: T0));
        }

        [Fact]
        public void Limits_exceeded()
        {
            var block = Samples.Write(new[] { Samples.Quotes("table-A", 5, 1), Samples.Quotes("table-BB", 3, 2) });
            var payloadLength = TableBlockReader.ReadHeader(block).DecompressedLength;

            TableBlockReader.Read(block, new TableBlockLimits(maxDecompressedBytes: payloadLength, maxTables: 2, maxRows: 8, maxColumnsPerTable: 4, maxTableIdLength: 8));

            Samples.AssertRefused(TableBlockError.LimitExceeded, block, new TableBlockLimits(maxDecompressedBytes: payloadLength - 1));
            Samples.AssertRefused(TableBlockError.LimitExceeded, block, new TableBlockLimits(maxTables: 1));
            Samples.AssertRefused(TableBlockError.LimitExceeded, block, new TableBlockLimits(maxRows: 7));
            Samples.AssertRefused(TableBlockError.LimitExceeded, block, new TableBlockLimits(maxColumnsPerTable: 3));
            Samples.AssertRefused(TableBlockError.LimitExceeded, block, new TableBlockLimits(maxTableIdLength: 7));

            var ex = Assert.Throws<TableBlockFormatException>(() => TableBlockReader.ReadHeader(block, new TableBlockLimits(maxRows: 7)));
            Assert.Equal(TableBlockError.LimitExceeded, ex.Error);
        }

        [Fact]
        public void Damaged_zstd_frames()
        {
            var block = Valid();
            var payload = RawBlock.Payload(block);
            var fields = RawBlock.FieldsOf(block);
            var frame = RawBlock.Compress(payload);

            // Not a zstd frame at all.
            Samples.AssertRefused(TableBlockError.CorruptFrame, RawBlock.Seal(fields, payload, payload.Length));

            // A skippable frame in front.
            var skippable = new byte[] { 0x50, 0x2A, 0x4D, 0x18, 0, 0, 0, 0 }.Concat(frame).ToArray();
            Samples.AssertRefused(TableBlockError.CorruptFrame, RawBlock.Seal(fields, skippable, payload.Length));

            // Two frames.
            Samples.AssertRefused(TableBlockError.CorruptFrame, RawBlock.Seal(fields, frame.Concat(frame).ToArray(), payload.Length));

            // A frame cut short.
            Samples.AssertRefused(TableBlockError.CorruptFrame, RawBlock.Seal(fields, frame.Take(frame.Length - 3).ToArray(), payload.Length));

            // A reserved bit of the frame header descriptor set.
            var reserved = (byte[])frame.Clone();
            reserved[4] |= 0x08;
            Samples.AssertRefused(TableBlockError.CorruptFrame, RawBlock.Seal(fields, reserved, payload.Length));

            // A block header of the frame that declares a reserved block type (3).
            var badBlock = (byte[])frame.Clone();
            int blockHeader = FrameHeaderSize(frame);
            badBlock[blockHeader] |= 0x06;
            Samples.AssertRefused(TableBlockError.CorruptFrame, RawBlock.Seal(fields, badBlock, payload.Length));
        }

        [Fact]
        public void A_zip_bomb_declared_bigger_than_the_limit_is_refused_from_the_header()
        {
            var fields = new RawBlock.Fields { RowCount = 1, TableCount = 1, FirstTime = T0, LastTime = T0, DecompressedLength = int.MaxValue };
            var block = RawBlock.Seal(fields, RawBlock.Compress(new byte[1024]), int.MaxValue);

            long allocated = Allocated(() => Samples.AssertRefused(TableBlockError.LimitExceeded, block));
            AssertAllocatedAtMost(allocated, 64 * 1024);
        }

        [Fact]
        public void A_zip_bomb_whose_frame_declares_its_true_size_is_refused_before_decompression()
        {
            const int declared = 1 << 20;
            var compressed = CompressZeros(100L << 20, declareContentSize: true);
            Assert.Equal(100UL << 20, ZstdFrames.ContentSize(compressed));

            var block = RawBlock.Seal(new RawBlock.Fields { RowCount = 1, TableCount = 1, FirstTime = T0, LastTime = T0 }, compressed, declared);

            long allocated = Allocated(() => Samples.AssertRefused(TableBlockError.LengthMismatch, block));
            AssertAllocatedAtMost(allocated, 64 * 1024);
        }

        [Fact]
        public void A_zip_bomb_without_a_declared_size_stops_at_the_declared_length()
        {
            // About 100 MB of zeros in a few kilobytes, the header declares 1 MiB: decompression stops at 1 MiB.
            const int declared = 1 << 20;
            var compressed = CompressZeros(100L << 20, declareContentSize: false);
            Assert.True(compressed.Length < 64 * 1024, $"{compressed.Length} compressed bytes");
            Assert.Equal(ZstdFrames.ContentSizeUnknown, ZstdFrames.ContentSize(compressed));

            var block = RawBlock.Seal(new RawBlock.Fields { RowCount = 1, TableCount = 1, FirstTime = T0, LastTime = T0 }, compressed, declared);

            long allocated = Allocated(() =>
            {
                var ex = Assert.Throws<TableBlockFormatException>(() => TableBlockReader.Read(block));
                Assert.Equal(TableBlockError.LengthMismatch, ex.Error);
            });
            AssertAllocatedAtMost(allocated, declared + 64 * 1024);
        }


        private static byte[] CompressZeros(long length, bool declareContentSize)
        {
            if (declareContentSize)
            {
                using var compressor = new Compressor(1);
                return compressor.Wrap(new byte[length]).ToArray();
            }

            using var output = new MemoryStream();
            using (var stream = new CompressionStream(output, 1))
            {
                var chunk = new byte[1 << 20];
                for (long written = 0; written < length; written += chunk.Length)
                    stream.Write(chunk, 0, chunk.Length);
            }

            return output.ToArray();
        }

        /// <summary>The size of a zstd frame header: magic, descriptor, optional window byte, dictionary id and content size.</summary>
        private static int FrameHeaderSize(byte[] frame)
        {
            byte descriptor = frame[4];
            int fcsFlag = descriptor >> 6;
            bool singleSegment = (descriptor & 0x20) != 0;
            int dictFlag = descriptor & 0x03;
            int fcsSize = fcsFlag == 0 ? (singleSegment ? 1 : 0) : fcsFlag == 1 ? 2 : fcsFlag == 2 ? 4 : 8;
            int dictSize = dictFlag == 0 ? 0 : dictFlag == 1 ? 1 : dictFlag == 2 ? 2 : 4;
            return 4 + 1 + (singleSegment ? 0 : 1) + dictSize + fcsSize;
        }

        /// <summary>Managed bytes allocated on this thread by <paramref name="action"/>; -1 where the runtime cannot tell.</summary>
        internal static long Allocated(Action action)
        {
#if NET
            long before = GC.GetAllocatedBytesForCurrentThread();
            action();
            return GC.GetAllocatedBytesForCurrentThread() - before;
#else
            action();
            return -1;
#endif
        }

        internal static void AssertAllocatedAtMost(long allocated, long bound)
        {
            if (allocated >= 0)
                Assert.True(allocated <= bound, $"{allocated:N0} bytes allocated, bound {bound:N0}");
        }
    }
}
