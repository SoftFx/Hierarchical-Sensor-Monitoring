using System;
using System.Buffers.Binary;
using System.IO.Hashing;
using System.Runtime.InteropServices;

namespace HSMTableBlocks
{
    /// <summary>
    /// Constants of the table block format. The byte layout is specified in
    /// <c>aicontext/features/api/table-block/feature.md</c>. Any change that a reader of the current version could not
    /// decode requires a new <see cref="FormatVersion"/>, and every released version must keep decoding.
    /// </summary>
    public static class TableBlockFormat
    {
        /// <summary>The format version this build writes. It reads this version only.</summary>
        public const byte FormatVersion = 1;

        /// <summary>The size of the fixed header in front of the zstd frame.</summary>
        public const int HeaderSize = 44;

        /// <summary>The largest time value: <c>DateTime.MaxValue.Ticks</c>.</summary>
        public const long MaxTimeTicks = 3_155_378_975_999_999_999;

        /// <summary>The width of every column value in format version 1.</summary>
        public const int ValueSize = 8;

        // The four magic bytes "HTBK" (HSM table block).
        internal const byte Magic0 = (byte)'H', Magic1 = (byte)'T', Magic2 = (byte)'B', Magic3 = (byte)'K';

        internal const int VersionOffset = 4;
        internal const int ReservedOffset = 5;           // bytes 5..7, zero
        internal const int RowCountOffset = 8;
        internal const int TableCountOffset = 12;
        internal const int FirstTimeOffset = 16;
        internal const int LastTimeOffset = 24;
        internal const int CompressedLengthOffset = 32;
        internal const int DecompressedLengthOffset = 36;
        internal const int ChecksumOffset = 40;

        // A directory entry: id length (2), id, schema id (4), row count (4), column count (1), column types (1 each).
        internal const int EntryFixedSize = 2 + 4 + 4 + 1;

        // The smallest entry and its data: a 1-byte id, one column, one row.
        internal const int MinTableBytes = EntryFixedSize + 1 + 1 + ValueSize;

        internal const uint ZstdFrameMagic = 0xFD2FB528;


        /// <summary>CRC-32 (IEEE 802.3, as in zlib and gzip) over header bytes 0..39 and the compressed payload.</summary>
        internal static uint Checksum(ReadOnlySpan<byte> header, ReadOnlySpan<byte> compressed)
        {
            var crc = new Crc32();
            crc.Append(header.Slice(0, ChecksumOffset));
            crc.Append(compressed);
            return crc.GetCurrentHashAsUInt32();
        }

        internal static void WriteHeader(Span<byte> header, int rowCount, int tableCount, long firstTime, long lastTime,
            int compressedLength, int decompressedLength)
        {
            header[0] = Magic0;
            header[1] = Magic1;
            header[2] = Magic2;
            header[3] = Magic3;
            header[VersionOffset] = FormatVersion;
            header[ReservedOffset] = 0;
            header[ReservedOffset + 1] = 0;
            header[ReservedOffset + 2] = 0;
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(RowCountOffset), rowCount);
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(TableCountOffset), tableCount);
            BinaryPrimitives.WriteInt64LittleEndian(header.Slice(FirstTimeOffset), firstTime);
            BinaryPrimitives.WriteInt64LittleEndian(header.Slice(LastTimeOffset), lastTime);
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(CompressedLengthOffset), compressedLength);
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(DecompressedLengthOffset), decompressedLength);
        }

        internal static bool IsKnownType(byte type) =>
            type == (byte)TableColumnType.Time || type == (byte)TableColumnType.Int64 || type == (byte)TableColumnType.Double;


        /// <summary>Copies 64-bit values into little-endian bytes; <paramref name="destination"/> is <c>values.Length * 8</c> long.</summary>
        internal static void WriteValues(ReadOnlySpan<long> values, Span<byte> destination)
        {
            if (BitConverter.IsLittleEndian)
            {
                MemoryMarshal.AsBytes(values).CopyTo(destination);
                return;
            }

            for (int i = 0; i < values.Length; i++)
                BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(i * ValueSize), values[i]);
        }

        /// <summary>Copies little-endian bytes into 64-bit values; <paramref name="source"/> is <c>destination.Length * 8</c> long.</summary>
        internal static void ReadValues(ReadOnlySpan<byte> source, Span<long> destination)
        {
            if (BitConverter.IsLittleEndian)
            {
                source.CopyTo(MemoryMarshal.AsBytes(destination));
                return;
            }

            for (int i = 0; i < destination.Length; i++)
                destination[i] = BinaryPrimitives.ReadInt64LittleEndian(source.Slice(i * ValueSize));
        }
    }
}
