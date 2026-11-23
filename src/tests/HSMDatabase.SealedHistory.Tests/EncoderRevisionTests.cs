using HSMCommon.Model;
using HSMDatabase.SealedHistory;
using Xunit;

namespace HSMDatabase.SealedHistory.Tests
{
    /// <summary>
    /// Header byte 5 is the encoder revision: written by the encoder, informational for readers. Bytes 6 and 7
    /// stay reserved and must be zero.
    /// </summary>
    public class EncoderRevisionTests
    {
        private const int RevisionOffset = 5;

        private static readonly List<HistoryRow> Rows = Samples.Ordinary(SensorType.DoubleBar, 40);

        private static byte[] SmallChunk() => ChunkCodec.Encode(Rows).Chunks.Single();

        private static byte[] WithByte(byte[] chunk, int offset, byte value)
        {
            var copy = (byte[])chunk.Clone();
            copy[offset] = value;
            CorruptionTests.RecomputeChecksum(copy);
            return copy;
        }


        [Fact]
        public void The_encoder_writes_its_revision_into_byte_5()
        {
            foreach (var type in Samples.SealedTypes)
                foreach (var chunk in ChunkCodec.Encode(Samples.Ordinary(type, 5000)).Chunks)
                    Assert.Equal(ChunkCodec.EncoderRevision, chunk[RevisionOffset]);
        }

        [Fact]
        public void The_header_reader_returns_the_revision()
        {
            var header = ChunkCodec.ReadHeader(SmallChunk());

            Assert.Equal(ChunkCodec.EncoderRevision, header.EncoderRevision);
            Assert.Equal(ChunkCodec.FormatVersion, header.FormatVersion);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(255)]
        public void Any_revision_decodes_to_the_same_rows_and_passes_every_check(byte revision)
        {
            var original = SmallChunk();
            var chunk = WithByte(original, RevisionOffset, revision);

            var header = ChunkCodec.ReadHeader(chunk);
            Assert.Equal(revision, header.EncoderRevision);
            Assert.Equal(ChunkCodec.ReadHeader(original) with { EncoderRevision = revision }, header);

            Fidelity.AssertRowsEqual(Rows, ChunkCodec.Decode(chunk));
            Assert.True(ChunkCodec.ExtractPayload(chunk).AsSpan().SequenceEqual(ChunkCodec.ExtractPayload(original)));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(128)]
        [InlineData(255)]
        public void The_checksum_covers_the_revision_byte(byte delta)
        {
            var chunk = SmallChunk();
            chunk[RevisionOffset] = (byte)(ChunkCodec.EncoderRevision + delta); // another value, no checksum update

            Assert.Equal(ChunkFormatError.ChecksumMismatch, Assert.Throws<ChunkFormatException>(() => ChunkCodec.Decode(chunk)).Error);
            Assert.Equal(ChunkFormatError.ChecksumMismatch, Assert.Throws<ChunkFormatException>(() => ChunkCodec.ReadHeader(chunk)).Error);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        [InlineData(255)]
        public void A_bit_flip_elsewhere_is_still_detected_whatever_the_revision(byte revision)
        {
            var chunk = WithByte(SmallChunk(), RevisionOffset, revision);

            for (int bit = 0; bit < chunk.Length * 8; bit++)
            {
                if (bit / 8 == RevisionOffset)
                    continue;

                var damaged = (byte[])chunk.Clone();
                damaged[bit / 8] ^= (byte)(1 << (bit % 8));

                CorruptionTests.AssertCleanFailure(Assert.Throws<ChunkFormatException>(() => ChunkCodec.Decode(damaged)));
            }
        }

        [Theory]
        [InlineData(6, 1)]
        [InlineData(6, 255)]
        [InlineData(7, 1)]
        [InlineData(7, 255)]
        public void A_non_zero_reserved_byte_is_corrupt(int offset, byte value)
        {
            var chunk = WithByte(SmallChunk(), offset, value);

            var ex = Assert.Throws<ChunkFormatException>(() => ChunkCodec.ReadHeader(chunk));
            Assert.Equal(ChunkFormatError.Corrupt, ex.Error);
            Assert.Contains("reserved", ex.Message);
            Assert.Equal(ChunkFormatError.Corrupt, Assert.Throws<ChunkFormatException>(() => ChunkCodec.Decode(chunk)).Error);
        }

        [Fact]
        public void The_encoder_writes_zero_into_the_reserved_bytes()
        {
            var chunk = SmallChunk();

            Assert.Equal(0, chunk[6]);
            Assert.Equal(0, chunk[7]);
        }
    }
}
