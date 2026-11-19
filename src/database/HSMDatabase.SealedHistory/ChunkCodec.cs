using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using HSMCommon.Model;
using HSMDatabase.SealedHistory.Format;
using ZstdSharp;

namespace HSMDatabase.SealedHistory
{
    /// <summary>
    /// Encodes the history rows of one sensor into chunks and decodes them back (ADR-0020). Pure functions:
    /// no storage, no shared mutable state, safe to call concurrently. The byte layout is specified in
    /// <c>aicontext/features/storage/sealed-history/feature.md</c>. A change that a reader of the current version
    /// could not decode requires a new <see cref="FormatVersion"/>, and old versions must keep decoding; a change
    /// to the bytes written for the same input that the current reader still decodes requires a new
    /// <see cref="EncoderRevision"/>.
    /// </summary>
    public static class ChunkCodec
    {
        /// <summary>The format version this build writes. It reads this version only.</summary>
        public const byte FormatVersion = 1;

        /// <summary>
        /// The revision of the encoder in this build, written into header byte 5 of every chunk. Bump it whenever
        /// the bytes <see cref="Encode(IReadOnlyList{HistoryRow})"/> writes for the same input change while a reader
        /// of the same <see cref="FormatVersion"/> still decodes them (a better choice among the existing column
        /// encodings, other parameters, a compressor upgrade that changes its output), and add a golden set for it.
        /// Readers accept any value and never decode differently because of it.
        /// </summary>
        public const byte EncoderRevision = 1;

        /// <summary>The largest number of rows in one chunk; part of format version 1.</summary>
        public const int MaxRowsPerChunk = 4096;

        /// <summary>The largest uncompressed payload of one chunk; part of format version 1.</summary>
        public const int MaxPayloadBytes = 64 * 1024 * 1024;

        /// <summary>The step <c>ReceivingTime</c> is stored at, relative to <c>Time</c>: one millisecond.</summary>
        public const long ReceivingTimeStepTicks = TimeSpan.TicksPerMillisecond;

        public const int HeaderSize = 40;

        internal const byte Magic0 = (byte)'H';
        internal const byte Magic1 = (byte)'C';

        internal const byte CompressionNone = 0;
        internal const byte CompressionZstd = 1;

        internal const int ZstdLevel = 3;

        private const uint ZstdFrameMagic = 0xFD2FB528;

        private const int EncoderRevisionOffset = 5;

        private const int ChecksumOffset = 36;


        /// <summary>
        /// Encodes the rows of one sensor, in strictly ascending key order, into one or more chunks of at most
        /// <see cref="MaxRowsPerChunk"/> rows. Refuses the whole input (and never throws) when any row cannot be
        /// represented exactly; see <see cref="ChunkRefusalReason"/>.
        /// </summary>
        public static ChunkEncodeResult Encode(IReadOnlyList<HistoryRow> rows) => Encode(rows, MaxRowsPerChunk);

        /// <summary>Reads and verifies the header (magic, version, checksum, field ranges) without decompressing the payload. The encoder revision is returned as stored, whatever its value.</summary>
        public static ChunkHeader ReadHeader(ReadOnlySpan<byte> chunk) => ParseHeader(chunk, MaxRowsPerChunk).Header;

        /// <summary>
        /// Decodes a chunk into its rows in key order. Throws <see cref="ChunkFormatException"/> for an unknown
        /// version, a bad checksum, or a truncated or corrupt chunk; never returns an empty or partial list.
        /// </summary>
        public static List<HistoryRow> Decode(ReadOnlySpan<byte> chunk) => Decode(chunk, MaxRowsPerChunk);


        internal static ChunkEncodeResult Encode(IReadOnlyList<HistoryRow> rows, int maxRowsPerChunk)
        {
            ArgumentNullException.ThrowIfNull(rows);
            if (maxRowsPerChunk < 1)
                throw new ArgumentOutOfRangeException(nameof(maxRowsPerChunk));

            var refusal = RowValidator.Validate(rows, out var valueType);
            if (refusal is not null)
                return refusal;

            int chunkCount = (rows.Count + maxRowsPerChunk - 1) / maxRowsPerChunk;
            int baseSize = rows.Count / chunkCount, larger = rows.Count % chunkCount;

            var chunks = new List<byte[]>(chunkCount);
            var payload = new ByteSink(Math.Min(rows.Count, maxRowsPerChunk) * 16, MaxPayloadBytes);
            int start = 0;

            for (int c = 0; c < chunkCount; c++)
            {
                int count = baseSize + (c < larger ? 1 : 0);

                payload.Clear();

                try
                {
                    PayloadWriter.Write(payload, rows, start, count, valueType);
                }
                catch (PayloadTooLargeException ex)
                {
                    return ChunkEncodeResult.Refuse(ChunkRefusalReason.ChunkTooLarge, start,
                        $"rows {start}..{start + count - 1}: {ex.Message}");
                }

                chunks.Add(Assemble(payload.Written, valueType, count, rows[start].Key, rows[start + count - 1].Key));
                start += count;
            }

            return ChunkEncodeResult.Success(chunks);
        }

        internal static List<HistoryRow> Decode(ReadOnlySpan<byte> chunk, int maxRowsPerChunk)
        {
            var (header, compression, payloadLength) = ParseHeader(chunk, maxRowsPerChunk);
            var stored = chunk.Slice(HeaderSize);

            byte[] rented = null;

            try
            {
                ReadOnlySpan<byte> payload;

                if (compression == CompressionNone)
                {
                    payload = stored;
                }
                else
                {
                    rented = ArrayPool<byte>.Shared.Rent(Math.Max(payloadLength, 1));
                    payload = Decompress(stored, rented.AsSpan(0, payloadLength));
                }

                var reader = new ByteReader(payload);
                var rows = PayloadReader.Read(ref reader, header.ValueType, header.RowCount);

                if (reader.Remaining != 0)
                    throw Corrupt.Error($"{reader.Remaining} unread payload bytes");

                if (rows[0].Key != header.FirstKey || rows[^1].Key != header.LastKey)
                    throw Corrupt.Error("first or last key does not match the header");

                return rows;
            }
            catch (Exception ex) when (ex is not ChunkFormatException and not OutOfMemoryException)
            {
                throw new ChunkFormatException(ChunkFormatError.Corrupt, ex.Message, ex);
            }
            finally
            {
                if (rented is not null)
                    ArrayPool<byte>.Shared.Return(rented);
            }
        }


        /// <summary>Wraps a payload into a chunk: header, zstd when it makes the payload smaller, checksum.</summary>
        internal static byte[] Assemble(ReadOnlySpan<byte> payload, SensorType valueType, int rowCount, long firstKey, long lastKey, bool allowCompression = true)
        {
            byte compression = CompressionNone;
            ReadOnlySpan<byte> stored = payload;
            byte[] compressed = null;

            if (allowCompression)
            {
                using var compressor = new Compressor(ZstdLevel);
                compressed = new byte[Compressor.GetCompressBound(payload.Length)];
                int written = compressor.Wrap(payload, compressed);

                if (written < payload.Length)
                {
                    compression = CompressionZstd;
                    stored = compressed.AsSpan(0, written);
                }
            }

            var chunk = new byte[HeaderSize + stored.Length];
            var span = chunk.AsSpan();

            span[0] = Magic0;
            span[1] = Magic1;
            span[2] = FormatVersion;
            span[3] = (byte)valueType;
            span[4] = compression;
            span[EncoderRevisionOffset] = EncoderRevision;
            // bytes 6..7 are reserved and stay zero
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(8), rowCount);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(12), payload.Length);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(16), stored.Length);
            BinaryPrimitives.WriteInt64LittleEndian(span.Slice(20), firstKey);
            BinaryPrimitives.WriteInt64LittleEndian(span.Slice(28), lastKey);
            stored.CopyTo(span.Slice(HeaderSize));
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(ChecksumOffset), Checksum(chunk));

            return chunk;
        }

        /// <summary>Returns the verified, decompressed payload of a chunk (the column streams).</summary>
        internal static byte[] ExtractPayload(ReadOnlySpan<byte> chunk)
        {
            var (_, compression, payloadLength) = ParseHeader(chunk, MaxRowsPerChunk);
            var stored = chunk.Slice(HeaderSize);

            if (compression == CompressionNone)
                return stored.ToArray();

            var payload = new byte[payloadLength];
            Decompress(stored, payload);
            return payload;
        }

        private static (ChunkHeader Header, byte Compression, int PayloadLength) ParseHeader(ReadOnlySpan<byte> chunk, int maxRowsPerChunk)
        {
            if (chunk.Length < 3)
                throw new ChunkFormatException(ChunkFormatError.Truncated, $"{chunk.Length} bytes is shorter than the version prefix");

            if (chunk[0] != Magic0 || chunk[1] != Magic1)
                throw new ChunkFormatException(ChunkFormatError.NotAChunk, "the magic bytes do not match");

            if (chunk[2] != FormatVersion)
                throw new ChunkFormatException(ChunkFormatError.UnknownVersion, $"format version {chunk[2]} is not supported (this build reads {FormatVersion})");

            if (chunk.Length < HeaderSize)
                throw new ChunkFormatException(ChunkFormatError.Truncated, $"{chunk.Length} bytes is shorter than the {HeaderSize}-byte header");

            int storedLength = BinaryPrimitives.ReadInt32LittleEndian(chunk.Slice(16));
            if (storedLength < 0)
                throw new ChunkFormatException(ChunkFormatError.Corrupt, $"negative stored length {storedLength}");
            if (chunk.Length - HeaderSize < storedLength)
                throw new ChunkFormatException(ChunkFormatError.Truncated, $"the header declares {storedLength} payload bytes, {chunk.Length - HeaderSize} present");
            if (chunk.Length - HeaderSize > storedLength)
                throw new ChunkFormatException(ChunkFormatError.Corrupt, $"{chunk.Length - HeaderSize - storedLength} bytes after the declared payload");

            uint expected = BinaryPrimitives.ReadUInt32LittleEndian(chunk.Slice(ChecksumOffset));
            if (Checksum(chunk) != expected)
                throw new ChunkFormatException(ChunkFormatError.ChecksumMismatch, "the CRC-32C over the header and payload does not match");

            var valueType = (SensorType)chunk[3];
            byte compression = chunk[4];
            int rowCount = BinaryPrimitives.ReadInt32LittleEndian(chunk.Slice(8));
            int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(chunk.Slice(12));
            long firstKey = BinaryPrimitives.ReadInt64LittleEndian(chunk.Slice(20));
            long lastKey = BinaryPrimitives.ReadInt64LittleEndian(chunk.Slice(28));

            if (!RowValidator.IsSealedType(valueType))
                throw Corrupt.Error($"value type {(byte)valueType} is not a sealed value type");
            if (compression != CompressionNone && compression != CompressionZstd)
                throw Corrupt.Error($"unknown compression {compression}");
            // byte 5, the encoder revision, may hold any value and does not affect decoding
            if (chunk[6] != 0 || chunk[7] != 0)
                throw Corrupt.Error("reserved header bytes are not zero");
            if (rowCount < 1 || rowCount > maxRowsPerChunk)
                throw Corrupt.Error($"row count {rowCount} outside 1..{maxRowsPerChunk}");
            if (payloadLength < 1 || payloadLength > MaxPayloadBytes)
                throw Corrupt.Error($"payload length {payloadLength} outside 1..{MaxPayloadBytes}");
            if (compression == CompressionNone && payloadLength != storedLength)
                throw Corrupt.Error("an uncompressed payload must be stored as is");
            if (firstKey > lastKey || (rowCount == 1 && firstKey != lastKey))
                throw Corrupt.Error("first and last key are inconsistent");
            if (compression == CompressionZstd)
                ValidateZstdFrame(chunk.Slice(HeaderSize), payloadLength);

            return (new ChunkHeader(FormatVersion, valueType, rowCount, firstKey, lastKey, chunk[EncoderRevisionOffset]), compression, payloadLength);
        }

        /// <summary>
        /// A zstd payload is exactly one zstd frame (not a skippable frame) that declares its content size, the
        /// content size equals the header's payload length, and the frame spans every stored byte. Checked before
        /// anything is allocated for decompression.
        /// </summary>
        private static unsafe void ValidateZstdFrame(ReadOnlySpan<byte> stored, int payloadLength)
        {
            if (stored.Length < 4 || BinaryPrimitives.ReadUInt32LittleEndian(stored) != ZstdFrameMagic)
                throw Corrupt.Error("the stored payload does not start with a zstd frame");

            fixed (byte* source = stored)
            {
                ulong contentSize = ZstdSharp.Unsafe.Methods.ZSTD_getFrameContentSize(source, (nuint)stored.Length);
                if (contentSize != (ulong)payloadLength)
                    throw Corrupt.Error($"the zstd frame does not declare a content size of {payloadLength} bytes");

                nuint frameSize = ZstdSharp.Unsafe.Methods.ZSTD_findFrameCompressedSize(source, (nuint)stored.Length);
                if (ZstdSharp.Unsafe.Methods.ZSTD_isError(frameSize) || frameSize != (nuint)stored.Length)
                    throw Corrupt.Error("the stored payload is not exactly one zstd frame");
            }
        }

        private static ReadOnlySpan<byte> Decompress(ReadOnlySpan<byte> stored, Span<byte> destination)
        {
            int written;

            try
            {
                using var decompressor = new Decompressor();
                written = decompressor.Unwrap(stored, destination);
            }
            catch (ZstdException ex)
            {
                throw new ChunkFormatException(ChunkFormatError.Corrupt, "zstd payload does not decompress: " + ex.Message, ex);
            }

            if (written != destination.Length)
                throw Corrupt.Error($"zstd payload decompressed to {written} bytes, the header declares {destination.Length}");

            return destination;
        }

        /// <summary>CRC-32C (Castagnoli) over the header bytes before the checksum field and the stored payload.</summary>
        internal static uint Checksum(ReadOnlySpan<byte> chunk) =>
            ~Crc32C(Crc32C(uint.MaxValue, chunk.Slice(0, ChecksumOffset)), chunk.Slice(HeaderSize));

        internal static uint Crc32C(uint crc, ReadOnlySpan<byte> data)
        {
            while (data.Length >= 8)
            {
                crc = BitOperations.Crc32C(crc, BinaryPrimitives.ReadUInt64LittleEndian(data));
                data = data.Slice(8);
            }

            foreach (var b in data)
                crc = BitOperations.Crc32C(crc, b);

            return crc;
        }
    }
}
