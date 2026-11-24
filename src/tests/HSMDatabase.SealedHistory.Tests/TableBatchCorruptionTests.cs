using System.Buffers.Binary;
using HSMDatabase.SealedHistory;
using HSMDatabase.SealedHistory.Format;
using HSMTableBlocks;
using Xunit;
using static HSMDatabase.SealedHistory.Tests.TableBatchKit;

namespace HSMDatabase.SealedHistory.Tests
{
    /// <summary>
    /// A damaged batch is refused with a named error, never decoded into an empty or partial result, and its lengths are
    /// checked before anything sized from them is allocated.
    /// </summary>
    public class TableBatchCorruptionTests
    {
        private static readonly byte[] Valid = TableBatchCodec.Encode(Quotes(42, 700, nullPercent: 5));
        private static readonly byte[] Uncompressed = UncompressedBatch();


        private static TableBatchFormatError Refusal(byte[] batch) =>
            Assert.Throws<TableBatchFormatException>(() => TableBatchCodec.Decode(batch)).Error;

        // Rewrites the checksum after a change to the header or the stored bytes, so the check under test is reached.
        private static byte[] Resealed(byte[] batch)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(batch.AsSpan(44), TableBatchCodec.Checksum(batch));
            return batch;
        }

        private static byte[] Edited(byte[] source, Action<byte[]> edit, bool reseal = true)
        {
            var copy = (byte[])source.Clone();
            edit(copy);
            return reseal ? Resealed(copy) : copy;
        }

        private static byte[] UncompressedBatch()
        {
            var batch = Batch(TableBatchColumn.Time([Start, Start + 1]), TableBatchColumn.Double([1.5, 2.5]));
            var payload = TableBatchCodec.ExtractPayload(TableBatchCodec.Encode(batch));
            return TableBatchCodec.Assemble(payload, Schema, 2, 2, Start, Start + 1, allowCompression: false);
        }

        // A batch around a hand-made payload, uncompressed (the header's lengths and checksum are right).
        private static byte[] WithPayload(byte[] payload, int rows, int columns, long first, long last) =>
            TableBatchCodec.Assemble(payload, Schema, rows, columns, first, last, allowCompression: false);


        [Fact]
        public void The_valid_batches_decode()
        {
            Assert.Equal(TableBatchCodec.FlagZstd, Valid[4]);
            Assert.Equal(0, Uncompressed[4]);
            TableBatchCodec.Decode(Valid);
            TableBatchCodec.Decode(Uncompressed);
        }

        [Fact]
        public void A_bad_magic_is_not_a_batch()
        {
            Assert.Equal(TableBatchFormatError.NotABatch, Refusal(Edited(Valid, b => b[0] = (byte)'X', reseal: false)));
            Assert.Equal(TableBatchFormatError.NotABatch, Refusal(Edited(Valid, b => b[1] = (byte)'C', reseal: false)));
            // A sealed history chunk starts with HC.
            Assert.Equal(TableBatchFormatError.NotABatch, Refusal(Edited(Valid, b => b[1] = (byte)'C')));
        }

        [Fact]
        public void An_unknown_format_version_is_refused_and_the_encoder_revision_is_ignored()
        {
            Assert.Equal(TableBatchFormatError.UnknownVersion, Refusal(Edited(Valid, b => b[2] = 2)));
            Assert.Equal(TableBatchFormatError.UnknownVersion, Refusal(Edited(Valid, b => b[2] = 0)));

            var otherRevision = Edited(Valid, b => b[3] = 200);
            Assert.Equal(200, TableBatchCodec.ReadHeader(otherRevision).EncoderRevision);
            TableBatchCodec.Decode(otherRevision);
        }

        [Fact]
        public void A_changed_byte_anywhere_fails_the_checksum()
        {
            foreach (var position in new[] { 3, 8, 12, 20, 28, 36, 44, 48, Valid.Length / 2, Valid.Length - 1 })
            {
                var damaged = Edited(Valid, b => b[position] ^= 0x10, reseal: false);
                Assert.Equal(TableBatchFormatError.ChecksumMismatch, Refusal(damaged));
            }
        }

        [Fact]
        public void A_batch_cut_at_any_length_is_truncated()
        {
            for (int length = 0; length < Valid.Length; length++)
            {
                var error = Refusal(Valid.AsSpan(0, length).ToArray());
                Assert.True(error == TableBatchFormatError.Truncated, $"cut at {length}: {error}");
            }
        }

        [Fact]
        public void Bytes_after_the_stored_payload_are_a_length_mismatch()
        {
            Assert.Equal(TableBatchFormatError.LengthMismatch, Refusal([.. Valid, 0]));
        }

        [Fact]
        public void Lengths_that_disagree_are_refused()
        {
            // A stored length of zero or below.
            Assert.Equal(TableBatchFormatError.LengthMismatch, Refusal(Edited(Valid, b => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(24), 0))));
            // An uncompressed payload whose declared length is not the stored length.
            Assert.Equal(TableBatchFormatError.LengthMismatch, Refusal(Edited(Uncompressed, b => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(20), BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(20)) - 1))));
            // A compressed payload declared one byte longer than its frame holds.
            Assert.Equal(TableBatchFormatError.CorruptFrame, Refusal(Edited(Valid, b => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(20), BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(20)) + 1))));
        }

        [Fact]
        public void A_payload_length_beyond_what_the_rows_and_columns_allow_is_refused_before_anything_is_allocated()
        {
            var huge = Edited(Valid, b => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(20), int.MaxValue));

            long before = GC.GetAllocatedBytesForCurrentThread();
            Assert.Equal(TableBatchFormatError.LengthMismatch, Refusal(huge));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.True(allocated < 64 * 1024, $"{allocated} bytes allocated to refuse a declared payload of 2 GB");
        }

        [Fact]
        public void Header_fields_out_of_range_are_corrupt()
        {
            Assert.Equal(TableBatchFormatError.CorruptHeader, Refusal(Edited(Valid, b => b[4] = 0x03)));
            Assert.Equal(TableBatchFormatError.CorruptHeader, Refusal(Edited(Valid, b => b[6] = 1)));
            Assert.Equal(TableBatchFormatError.CorruptHeader, Refusal(Edited(Valid, b => b[18] = 1)));
            Assert.Equal(TableBatchFormatError.CorruptHeader, Refusal(Edited(Valid, b => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(12), 0))));
            Assert.Equal(TableBatchFormatError.CorruptHeader, Refusal(Edited(Valid, b => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(12), TableBatchCodec.MaxRows + 1))));
            Assert.Equal(TableBatchFormatError.CorruptHeader, Refusal(Edited(Valid, b => b[16] = 0)));
            Assert.Equal(TableBatchFormatError.CorruptHeader, Refusal(Edited(Valid, b => BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(28), long.MaxValue))));
        }

        [Fact]
        public void A_stored_payload_that_is_not_one_zstd_frame_is_corrupt()
        {
            // The frame magic damaged.
            Assert.Equal(TableBatchFormatError.CorruptFrame, Refusal(Edited(Valid, b => b[48] ^= 0xFF)));
            // The zstd flag on a payload that is not compressed.
            Assert.Equal(TableBatchFormatError.CorruptFrame, Refusal(Edited(Uncompressed, b => b[4] = TableBatchCodec.FlagZstd)));
            // A frame whose content does not decompress (a byte inside the compressed blocks).
            var error = Refusal(Edited(Valid, b => b[b.Length - 5] ^= 0x5A));
            Assert.Contains(error, new[] { TableBatchFormatError.CorruptFrame, TableBatchFormatError.CorruptStream, TableBatchFormatError.LengthMismatch,
                                           TableBatchFormatError.CountMismatch, TableBatchFormatError.HeaderMismatch });
        }

        // What a payload read with the wrong count ends in, depending on where the bytes happen to fall.
        private static readonly TableBatchFormatError[] Mismatches =
        [
            TableBatchFormatError.CorruptStream, TableBatchFormatError.LengthMismatch, TableBatchFormatError.CountMismatch,
            TableBatchFormatError.HeaderMismatch, TableBatchFormatError.CorruptHeader,
        ];

        [Fact]
        public void A_count_of_columns_that_does_not_match_the_payload_is_refused()
        {
            // One column more than the payload holds: the bytes are read as one more column than they are.
            Assert.Contains(Refusal(Edited(Uncompressed, b => b[16] = 3)), Mismatches);
            // One column less: the bytes of the last column are left over (and the second type byte is read as a stream).
            Assert.Equal(TableBatchFormatError.CorruptStream, Refusal(Edited(Uncompressed, b => b[16] = 1)));
        }

        [Fact]
        public void A_count_of_rows_that_does_not_match_the_payload_is_refused()
        {
            Assert.Contains(Refusal(Edited(Uncompressed, b => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(12), 3))), Mismatches);
            Assert.Contains(Refusal(Edited(Uncompressed, b => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(12), 1))), Mismatches);
        }

        [Fact]
        public void A_stream_cut_short_is_corrupt()
        {
            // time column: raw encoding announcing two values, one present.
            var payload = new byte[] { 1, EncodingRaw }.Concat(BitConverter.GetBytes(Start)).ToArray();
            Assert.Equal(TableBatchFormatError.CorruptStream, Refusal(WithPayload(payload, 2, 1, Start, Start)));

            // time column: an IntColumn varint stream that ends inside its last varint.
            var sink = new ByteSink();
            sink.WriteByte(1);
            sink.WriteByte(TableBatchCodec.EncodingInt);
            IntColumn.WriteVarint(sink, [Start, Start + 1_000_000_000], 1);
            var cut = sink.Written[..^1].ToArray();
            Assert.Equal(TableBatchFormatError.CorruptStream, Refusal(WithPayload(cut, 2, 1, Start, Start + 1_000_000_000)));
        }

        [Fact]
        public void Unknown_types_and_encodings_are_corrupt()
        {
            Assert.Equal(TableBatchFormatError.CorruptStream, Refusal(WithPayload([4, 0], 1, 1, 0, 0)));      // type 4
            Assert.Equal(TableBatchFormatError.CorruptStream, Refusal(WithPayload([3, 0], 1, 1, 0, 0)));      // the first column is not the time
            Assert.Equal(TableBatchFormatError.CorruptStream, Refusal(WithPayload([1, 9, 0], 1, 1, 0, 0)));   // encoding 9
            Assert.Equal(TableBatchFormatError.CorruptStream, Refusal(WithPayload([1, EncodingDouble, 0], 1, 1, 0, 0))); // a double stream on the time
        }

        private const byte EncodingRaw = TableBatchCodec.EncodingRaw;
        private const byte EncodingDouble = TableBatchCodec.EncodingDouble;

        // time (one constant row) + a double column: presence all, then the given exception list and stream.
        private static byte[] DoubleColumnBatch(int rows, Action<ByteSink> exceptionsAndStream)
        {
            var sink = new ByteSink();
            sink.WriteByte(1);
            sink.WriteByte(3);
            sink.WriteByte(TableBatchCodec.EncodingInt);
            IntColumn.Write(sink, Enumerable.Repeat(Start, rows).ToArray());
            sink.WriteByte(Presence.ModeAll);
            exceptionsAndStream(sink);
            return WithPayload(sink.Written.ToArray(), rows, 2, Start, Start);
        }

        [Fact]
        public void Exception_lists_that_do_not_agree_with_their_column_are_a_count_mismatch()
        {
            // Two exceptions in a column of 100 values: at most one.
            Assert.Equal(TableBatchFormatError.CountMismatch, Refusal(DoubleColumnBatch(100, s => s.WriteVarUInt(2))));
            // One exception in a column of one value: at least one value must be coded.
            Assert.Equal(TableBatchFormatError.CountMismatch, Refusal(DoubleColumnBatch(1, s => s.WriteVarUInt(1))));
            // An exception beyond the column.
            Assert.Equal(TableBatchFormatError.CountMismatch, Refusal(DoubleColumnBatch(100, s => { s.WriteVarUInt(1); s.WriteVarUInt(100); s.WriteUInt64(0); })));
            // Two exceptions at the same index.
            Assert.Equal(TableBatchFormatError.CountMismatch, Refusal(DoubleColumnBatch(1000, s => { s.WriteVarUInt(2); s.WriteVarUInt(5); s.WriteUInt64(0); s.WriteVarUInt(0); s.WriteUInt64(0); })));
            // An exception beside a raw stream.
            Assert.Equal(TableBatchFormatError.CountMismatch, Refusal(DoubleColumnBatch(100, s =>
            {
                s.WriteVarUInt(1); s.WriteVarUInt(3); s.WriteUInt64(0);
                s.WriteByte(EncodingRaw);
                for (int i = 0; i < 100; i++) s.WriteUInt64(0);
            })));
        }

        [Fact]
        public void A_presence_map_with_padding_bits_is_corrupt()
        {
            var sink = new ByteSink();
            sink.WriteByte(1);
            sink.WriteByte(3);
            sink.WriteByte(TableBatchCodec.EncodingInt);
            IntColumn.Write(sink, Enumerable.Repeat(Start, 3).ToArray());
            sink.WriteByte(Presence.ModeBitmap);
            sink.WriteByte(0b1111_1011);
            Assert.Equal(TableBatchFormatError.CorruptStream, Refusal(WithPayload(sink.Written.ToArray(), 3, 2, Start, Start)));
        }

        [Fact]
        public void Bytes_after_the_last_column_are_a_length_mismatch()
        {
            var payload = TableBatchCodec.ExtractPayload(Uncompressed).Append((byte)0).ToArray();
            Assert.Equal(TableBatchFormatError.LengthMismatch, Refusal(WithPayload(payload, 2, 2, Start, Start + 1)));
        }

        [Fact]
        public void A_time_column_without_the_header_first_and_last_time_is_a_header_mismatch()
        {
            var payload = TableBatchCodec.ExtractPayload(Uncompressed);
            Assert.Equal(TableBatchFormatError.HeaderMismatch, Refusal(WithPayload(payload, 2, 2, Start, Start + 2)));
            Assert.Equal(TableBatchFormatError.HeaderMismatch, Refusal(WithPayload(payload, 2, 2, Start - 1, Start + 1)));
        }

        /// <summary>
        /// Every single-byte change of a valid batch, with the checksum rewritten so the payload checks are reached:
        /// refused with a named error or decoded into a batch of the header's shape; never another exception.
        /// </summary>
        [Fact]
        public void Every_single_byte_change_behind_a_valid_checksum_is_refused_or_decodes_whole()
        {
            var g = new SeededRows(7);

            foreach (var source in new[] { Valid, Uncompressed })
            {
                for (int position = 0; position < source.Length; position++)
                {
                    if (position is >= 44 and < 48)
                        continue;

                    var damaged = Edited(source, b => b[position] ^= (byte)(1 + g.Below(255)));

                    try
                    {
                        var batch = TableBatchCodec.Decode(damaged);
                        var header = TableBatchCodec.ReadHeader(damaged);
                        Assert.Equal(header.RowCount, batch.RowCount);
                        Assert.Equal(header.ColumnCount, batch.Columns.Count);
                    }
                    catch (TableBatchFormatException)
                    {
                    }
                }
            }
        }

        [Fact]
        public void The_encoder_refuses_input_outside_the_rules()
        {
            long[] two = [Start, Start + 1];

            Assert.Throws<ArgumentException>(() => TableBatchCodec.Encode(Batch()));
            Assert.Throws<ArgumentException>(() => TableBatchCodec.Encode(Batch(TableBatchColumn.Time([]))));
            Assert.Throws<ArgumentException>(() => TableBatchCodec.Encode(Batch(TableBatchColumn.Int64(two))));
            Assert.Throws<ArgumentException>(() => TableBatchCodec.Encode(Batch(TableBatchColumn.Time(two), TableBatchColumn.Double([1.0]))));
            Assert.Throws<ArgumentException>(() => TableBatchCodec.Encode(Batch(TableBatchColumn.Time(two), TableBatchColumn.Int64(two, [0, 0]))));
            Assert.Throws<ArgumentException>(() => TableBatchCodec.Encode(Batch(TableBatchColumn.Time(two), TableBatchColumn.Int64(two, [0b100]))));
            Assert.Throws<ArgumentException>(() => TableBatchCodec.Encode(Batch(new TableBatchColumn(TableColumnType.Time, two, null, [0b01]))));
            Assert.Throws<ArgumentException>(() => TableBatchCodec.Encode(Batch(TableBatchColumn.Time(two), new TableBatchColumn((TableColumnType)7, two, null))));
            Assert.Throws<ArgumentException>(() => TableBatchCodec.Encode(new TableBatch(Schema,
                Enumerable.Range(0, TableBatchCodec.MaxColumns + 1).Select(_ => TableBatchColumn.Time(two)).ToArray())));
        }
    }
}
