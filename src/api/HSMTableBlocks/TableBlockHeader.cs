using System;

namespace HSMTableBlocks
{
    /// <summary>The fixed-size header of a table block, read and verified without decompressing the payload.</summary>
    public readonly struct TableBlockHeader
    {
        /// <summary>The format version of the block.</summary>
        public byte FormatVersion { get; }

        /// <summary>The number of rows over all tables.</summary>
        public int RowCount { get; }

        /// <summary>The number of tables (directory entries).</summary>
        public int TableCount { get; }

        /// <summary>The smallest time of the block (first column of every table), in UTC ticks.</summary>
        public long FirstTime { get; }

        /// <summary>The largest time of the block (first column of every table), in UTC ticks.</summary>
        public long LastTime { get; }

        /// <summary>The length of the zstd frame that follows the header.</summary>
        public int CompressedLength { get; }

        /// <summary>The length of the payload after decompression.</summary>
        public int DecompressedLength { get; }

        /// <summary>The CRC-32 stored in the header.</summary>
        public uint Checksum { get; }

        /// <summary><see cref="FirstTime"/> as a UTC <see cref="DateTime"/>.</summary>
        public DateTime FirstTimeUtc => new DateTime(FirstTime, DateTimeKind.Utc);

        /// <summary><see cref="LastTime"/> as a UTC <see cref="DateTime"/>.</summary>
        public DateTime LastTimeUtc => new DateTime(LastTime, DateTimeKind.Utc);

        /// <summary>The length of the whole block: header and compressed payload.</summary>
        public int BlockLength => TableBlockFormat.HeaderSize + CompressedLength;


        internal TableBlockHeader(byte formatVersion, int rowCount, int tableCount, long firstTime, long lastTime,
            int compressedLength, int decompressedLength, uint checksum)
        {
            FormatVersion = formatVersion;
            RowCount = rowCount;
            TableCount = tableCount;
            FirstTime = firstTime;
            LastTime = lastTime;
            CompressedLength = compressedLength;
            DecompressedLength = decompressedLength;
            Checksum = checksum;
        }
    }
}
