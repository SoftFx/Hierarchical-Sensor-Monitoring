using System;

namespace HSMTableBlocks
{
    /// <summary>
    /// The bit order of a null map (#1859): which rows of one column hold no value. Row i is bit <c>i % 8</c> (least
    /// significant first) of byte <c>i / 8</c>; a set bit means the row is null. A map of a column of n rows is
    /// <see cref="ByteLength"/>(n) bytes, and its bits from n up (padding) are zero. The same order is used in a block,
    /// by the writer's input and by the reader's output.
    /// </summary>
    public static class TableNullMap
    {
        /// <summary>The length of the map of <paramref name="rowCount"/> rows: <c>ceil(rowCount / 8)</c>.</summary>
        public static int ByteLength(int rowCount)
        {
            if (rowCount < 0)
                throw new ArgumentOutOfRangeException(nameof(rowCount), rowCount, "a row count is not negative");

            return (int)(((long)rowCount + 7) / 8);
        }

        /// <summary>True when the bit of <paramref name="row"/> is set; false for an empty map (no nulls).</summary>
        public static bool IsNull(ReadOnlySpan<byte> map, int row)
        {
            if (map.IsEmpty)
                return false;

            return (map[row >> 3] & (1 << (row & 7))) != 0;
        }

        /// <summary>Sets the bit of <paramref name="row"/>: the row holds no value.</summary>
        public static void SetNull(Span<byte> map, int row) => map[row >> 3] |= (byte)(1 << (row & 7));
    }
}
