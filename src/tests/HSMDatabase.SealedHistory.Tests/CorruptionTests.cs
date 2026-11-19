using HSMCommon.Model;
using HSMDatabase.SealedHistory;
using Xunit;

namespace HSMDatabase.SealedHistory.Tests
{
    public class CorruptionTests
    {
        private static byte[] SmallChunk(SensorType type = SensorType.DoubleBar) =>
            ChunkCodec.Encode(Samples.Ordinary(type, 40)).Chunks.Single();


        [Fact]
        public void Every_single_bit_flip_is_detected()
        {
            var chunk = SmallChunk();

            for (int bit = 0; bit < chunk.Length * 8; bit++)
            {
                var damaged = (byte[])chunk.Clone();
                damaged[bit / 8] ^= (byte)(1 << (bit % 8));

                AssertCleanFailure(Assert.Throws<ChunkFormatException>(() => ChunkCodec.Decode(damaged)));
                AssertCleanFailure(Assert.Throws<ChunkFormatException>(() => ChunkCodec.ReadHeader(damaged)));
            }
        }

        [Fact]
        public void Every_truncation_is_detected()
        {
            var chunk = SmallChunk();

            for (int length = 0; length < chunk.Length; length++)
            {
                var ex = Assert.Throws<ChunkFormatException>(() => ChunkCodec.Decode(chunk.AsSpan(0, length)));
                Assert.Equal(ChunkFormatError.Truncated, ex.Error);
            }
        }

        [Fact]
        public void Trailing_bytes_are_detected()
        {
            var chunk = SmallChunk();
            var longer = chunk.Concat(new byte[] { 0 }).ToArray();

            var ex = Assert.Throws<ChunkFormatException>(() => ChunkCodec.Decode(longer));
            Assert.Equal(ChunkFormatError.Corrupt, ex.Error);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        [InlineData(255)]
        public void An_unknown_version_is_reported_as_such(byte version)
        {
            var chunk = SmallChunk();
            chunk[2] = version;

            var ex = Assert.Throws<ChunkFormatException>(() => ChunkCodec.Decode(chunk));
            Assert.Equal(ChunkFormatError.UnknownVersion, ex.Error);
            Assert.Equal(ChunkFormatError.UnknownVersion, Assert.Throws<ChunkFormatException>(() => ChunkCodec.ReadHeader(chunk)).Error);
        }

        [Fact]
        public void A_value_that_is_not_a_chunk_is_reported_as_such()
        {
            var memoryPackRow = Fidelity.Bytes(new DoubleValue { Value = 1 });

            var ex = Assert.Throws<ChunkFormatException>(() => ChunkCodec.Decode(memoryPackRow));
            Assert.Equal(ChunkFormatError.NotAChunk, ex.Error);
        }

        [Fact]
        public void A_bad_checksum_is_reported_as_such()
        {
            var chunk = SmallChunk();
            chunk[^1] ^= 0x40;

            Assert.Equal(ChunkFormatError.ChecksumMismatch, Assert.Throws<ChunkFormatException>(() => ChunkCodec.Decode(chunk)).Error);
        }

        [Fact]
        public void File_type_in_a_header_with_a_valid_checksum_is_corrupt()
        {
            var good = SmallChunk(SensorType.Integer);
            var payload = ChunkCodec.ExtractPayload(good);
            var header = ChunkCodec.ReadHeader(good);

            var chunk = ChunkCodec.Assemble(payload, SensorType.File, header.RowCount, header.FirstKey, header.LastKey);

            Assert.Equal(ChunkFormatError.Corrupt, Assert.Throws<ChunkFormatException>(() => ChunkCodec.Decode(chunk)).Error);
        }

        [Fact]
        public void A_header_that_disagrees_with_the_payload_is_corrupt()
        {
            var good = SmallChunk(SensorType.Integer);
            var payload = ChunkCodec.ExtractPayload(good);
            var header = ChunkCodec.ReadHeader(good);

            byte[][] wrong =
            [
                ChunkCodec.Assemble(payload, header.ValueType, header.RowCount, header.FirstKey + 1, header.LastKey),
                ChunkCodec.Assemble(payload, header.ValueType, header.RowCount, header.FirstKey, header.LastKey + 1),
                ChunkCodec.Assemble(payload, header.ValueType, header.RowCount - 1, header.FirstKey, header.LastKey),
                ChunkCodec.Assemble(payload, header.ValueType, header.RowCount + 1, header.FirstKey, header.LastKey),
                ChunkCodec.Assemble(payload, SensorType.Double, header.RowCount, header.FirstKey, header.LastKey),
                ChunkCodec.Assemble(payload, header.ValueType, ChunkCodec.MaxRowsPerChunk + 1, header.FirstKey, header.LastKey),
                ChunkCodec.Assemble(payload.AsSpan(0, payload.Length - 1), header.ValueType, header.RowCount, header.FirstKey, header.LastKey),
            ];

            foreach (var chunk in wrong)
                Assert.Equal(ChunkFormatError.Corrupt, Assert.Throws<ChunkFormatException>(() => ChunkCodec.Decode(chunk)).Error);
        }

        /// <summary>
        /// Payload damage behind a valid checksum (what a codec defect, not a disk, would produce): the decoder
        /// either returns every row or throws <see cref="ChunkFormatException"/>; no other exception, no partial list.
        /// </summary>
        [Theory]
        [InlineData(SensorType.DoubleBar)]
        [InlineData(SensorType.IntegerBar)]
        [InlineData(SensorType.Double)]
        [InlineData(SensorType.Rate)]
        [InlineData(SensorType.String)]
        [InlineData(SensorType.Version)]
        [InlineData(SensorType.Boolean)]
        [InlineData(SensorType.Integer)]
        [InlineData(SensorType.Enum)]
        [InlineData(SensorType.TimeSpan)]
        public void Fuzzed_payloads_behind_a_valid_checksum_fail_cleanly(SensorType type)
        {
            var random = new Random(1802 + (int)type);
            var sources = new[] { Samples.Ordinary(type, 50), Samples.Random(type, 50, new Random(7)) };

            foreach (var rows in sources)
            {
                var good = ChunkCodec.Encode(rows).Chunks.Single();
                var payload = ChunkCodec.ExtractPayload(good);
                var header = ChunkCodec.ReadHeader(good);

                for (int iteration = 0; iteration < 3000; iteration++)
                {
                    var damaged = Mutate(payload, random);
                    if (damaged.Length == 0)
                        continue;

                    var chunk = ChunkCodec.Assemble(damaged, header.ValueType, header.RowCount, header.FirstKey, header.LastKey, allowCompression: iteration % 4 == 0);

                    try
                    {
                        var decoded = ChunkCodec.Decode(chunk);
                        Assert.Equal(header.RowCount, decoded.Count);
                        Assert.All(decoded, r => Assert.NotNull(r.Value));
                    }
                    catch (ChunkFormatException ex)
                    {
                        AssertCleanFailure(ex);
                    }
                }
            }
        }

        [Fact]
        public void Corrupt_zstd_frames_behind_a_valid_checksum_fail_cleanly()
        {
            var random = new Random(1802);
            var good = ChunkCodec.Encode(Samples.Ordinary(SensorType.DoubleBar, 300)).Chunks.Single();
            var header = ChunkCodec.ReadHeader(good);
            Assert.Equal(ChunkCodec.CompressionZstd, good[4]);

            for (int iteration = 0; iteration < 3000; iteration++)
            {
                var damaged = (byte[])good.Clone();
                int flips = random.Next(1, 4);
                for (int f = 0; f < flips; f++)
                    damaged[ChunkCodec.HeaderSize + random.Next(damaged.Length - ChunkCodec.HeaderSize)] ^= (byte)random.Next(1, 256);

                RecomputeChecksum(damaged);

                try
                {
                    Assert.Equal(header.RowCount, ChunkCodec.Decode(damaged).Count);
                }
                catch (ChunkFormatException ex)
                {
                    AssertCleanFailure(ex);
                }
            }
        }


        [Fact]
        public void A_payload_length_the_zstd_frame_does_not_declare_is_corrupt()
        {
            var good = ChunkCodec.Encode(Samples.Ordinary(SensorType.DoubleBar, 300)).Chunks.Single();
            Assert.Equal(ChunkCodec.CompressionZstd, good[4]);

            var damaged = (byte[])good.Clone();
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(damaged.AsSpan(12), ChunkCodec.MaxPayloadBytes);
            RecomputeChecksum(damaged);

            var ex = Assert.Throws<ChunkFormatException>(() => ChunkCodec.ReadHeader(damaged));
            Assert.Equal(ChunkFormatError.Corrupt, ex.Error);
            Assert.Contains("content size", ex.Message);
            AssertCleanFailure(Assert.Throws<ChunkFormatException>(() => ChunkCodec.Decode(damaged)));
        }

        [Fact]
        public void A_zstd_payload_must_be_exactly_one_frame_that_declares_its_size()
        {
            var good = ChunkCodec.Encode(Samples.Ordinary(SensorType.DoubleBar, 300)).Chunks.Single();
            var header = ChunkCodec.ReadHeader(good);
            var payload = ChunkCodec.ExtractPayload(good);
            var frame = good.AsSpan(ChunkCodec.HeaderSize).ToArray();
            int half = payload.Length / 2;

            byte[] skippable = [0x50, 0x2A, 0x4D, 0x18, 4, 0, 0, 0, 1, 2, 3, 4];

            byte[][] stored =
            [
                [.. Zstd(payload.AsSpan(0, half), contentSize: true), .. Zstd(payload.AsSpan(half), contentSize: true)], // two frames
                [.. frame, .. skippable],                                                                                 // frame + skippable frame
                [.. skippable, .. frame],                                                                                 // skippable frame first
                Zstd(payload, contentSize: false),                                                                        // no declared content size
                [.. frame, 0],                                                                                            // a trailing byte
            ];

            foreach (var bytes in stored)
            {
                var chunk = Chunk(bytes, header, payload.Length);
                var ex = Assert.Throws<ChunkFormatException>(() => ChunkCodec.ReadHeader(chunk));
                Assert.Equal(ChunkFormatError.Corrupt, ex.Error);
                AssertCleanFailure(Assert.Throws<ChunkFormatException>(() => ChunkCodec.Decode(chunk)));
            }

            // the same construction with the encoder's own frame decodes
            Assert.Equal(header.RowCount, ChunkCodec.Decode(Chunk(Zstd(payload, contentSize: true), header, payload.Length)).Count);
        }


        /// <summary>
        /// Decode turns every exception into ChunkFormatException(Corrupt), so the tests look inside: a failure must
        /// come from the reader's own checks, from zstd or from UTF-8 decoding, never from a reader bug such as an
        /// index out of range.
        /// </summary>
        internal static void AssertCleanFailure(ChunkFormatException ex)
        {
            Assert.True(ex.InnerException is null or ZstdSharp.ZstdException or System.Text.DecoderFallbackException,
                $"unexpected inner exception: {ex.InnerException}");
        }

        private static byte[] Zstd(ReadOnlySpan<byte> data, bool contentSize)
        {
            using var compressor = new ZstdSharp.Compressor(ChunkCodec.ZstdLevel);
            compressor.SetParameter(ZstdSharp.Unsafe.ZSTD_cParameter.ZSTD_c_contentSizeFlag, contentSize ? 1 : 0);
            return compressor.Wrap(data).ToArray();
        }

        private static byte[] Chunk(byte[] stored, ChunkHeader header, int payloadLength)
        {
            var chunk = new byte[ChunkCodec.HeaderSize + stored.Length];
            chunk[0] = ChunkCodec.Magic0;
            chunk[1] = ChunkCodec.Magic1;
            chunk[2] = ChunkCodec.FormatVersion;
            chunk[3] = (byte)header.ValueType;
            chunk[4] = ChunkCodec.CompressionZstd;
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(8), header.RowCount);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(12), payloadLength);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(16), stored.Length);
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(chunk.AsSpan(20), header.FirstKey);
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(chunk.AsSpan(28), header.LastKey);
            stored.CopyTo(chunk.AsSpan(ChunkCodec.HeaderSize));
            RecomputeChecksum(chunk);
            return chunk;
        }

        internal static void RecomputeChecksum(byte[] chunk)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(36), ChunkCodec.Checksum(chunk));
        }

        private static byte[] Mutate(byte[] payload, Random random)
        {
            var bytes = new List<byte>(payload);

            switch (random.Next(5))
            {
                case 0:
                    bytes[random.Next(bytes.Count)] ^= (byte)(1 << random.Next(8));
                    break;
                case 1:
                    bytes[random.Next(bytes.Count)] = (byte)random.Next(256);
                    break;
                case 2:
                    bytes.RemoveAt(random.Next(bytes.Count));
                    break;
                case 3:
                    bytes.Insert(random.Next(bytes.Count + 1), (byte)random.Next(256));
                    break;
                default:
                    bytes.RemoveRange(random.Next(bytes.Count), 1);
                    bytes[random.Next(bytes.Count)] = 0xFF;
                    break;
            }

            return bytes.ToArray();
        }
    }
}
