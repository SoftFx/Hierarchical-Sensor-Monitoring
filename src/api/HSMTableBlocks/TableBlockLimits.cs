using System;

namespace HSMTableBlocks
{
    /// <summary>
    /// The limits a block must stay within. A reader checks the header against them before it allocates or
    /// decompresses anything, and the directory against them while it parses it; a writer refuses input that would
    /// exceed them, so a block written with the default limits is accepted by a reader with the default limits.
    /// Immutable and safe to share between threads.
    /// </summary>
    public sealed class TableBlockLimits
    {
        /// <summary>Default largest decompressed payload: 64 MiB.</summary>
        public const int DefaultMaxDecompressedBytes = 64 * 1024 * 1024;

        /// <summary>Default largest number of tables in one block.</summary>
        public const int DefaultMaxTables = 65_536;

        /// <summary>Default largest number of rows in one block, over all tables.</summary>
        public const int DefaultMaxRows = 8 * 1024 * 1024;

        /// <summary>Default largest number of columns of one table, the time column included.</summary>
        public const int DefaultMaxColumnsPerTable = 64;

        /// <summary>Default longest table identifier, in bytes.</summary>
        public const int DefaultMaxTableIdLength = 1_024;

        /// <summary>The largest decompressed payload any limits may allow: 1 GiB.</summary>
        public const int FormatMaxDecompressedBytes = 1 << 30;

        /// <summary>The largest number of columns the format can describe (one byte in the directory).</summary>
        public const int FormatMaxColumnsPerTable = byte.MaxValue;

        /// <summary>The longest identifier the format can describe (two bytes in the directory).</summary>
        public const int FormatMaxTableIdLength = ushort.MaxValue;


        /// <summary>The defaults: 64 MiB, 65,536 tables, 8,388,608 rows, 64 columns, 1,024-byte identifiers.</summary>
        public static TableBlockLimits Default { get; } = new TableBlockLimits();


        /// <summary>The largest decompressed payload, in bytes.</summary>
        public int MaxDecompressedBytes { get; }

        /// <summary>The largest number of tables.</summary>
        public int MaxTables { get; }

        /// <summary>The largest number of rows over all tables.</summary>
        public int MaxRows { get; }

        /// <summary>The largest number of columns of one table, the time column included.</summary>
        public int MaxColumnsPerTable { get; }

        /// <summary>The longest table identifier, in bytes.</summary>
        public int MaxTableIdLength { get; }


        /// <summary>Creates limits; every value must be at least 1 and within the format's maximum.</summary>
        public TableBlockLimits(
            int maxDecompressedBytes = DefaultMaxDecompressedBytes,
            int maxTables = DefaultMaxTables,
            int maxRows = DefaultMaxRows,
            int maxColumnsPerTable = DefaultMaxColumnsPerTable,
            int maxTableIdLength = DefaultMaxTableIdLength)
        {
            MaxDecompressedBytes = Check(maxDecompressedBytes, FormatMaxDecompressedBytes, nameof(maxDecompressedBytes));
            MaxTables = Check(maxTables, int.MaxValue, nameof(maxTables));
            MaxRows = Check(maxRows, int.MaxValue, nameof(maxRows));
            MaxColumnsPerTable = Check(maxColumnsPerTable, FormatMaxColumnsPerTable, nameof(maxColumnsPerTable));
            MaxTableIdLength = Check(maxTableIdLength, FormatMaxTableIdLength, nameof(maxTableIdLength));
        }


        private static int Check(int value, int max, string name)
        {
            if (value < 1 || value > max)
                throw new ArgumentOutOfRangeException(name, value, $"must be within 1..{max}");

            return value;
        }
    }
}
