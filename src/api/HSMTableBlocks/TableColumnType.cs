namespace HSMTableBlocks
{
    /// <summary>
    /// The type of a column in a table block. The numeric values are written into the block directory and are part of
    /// the format: never renumber them. Every type of format version 1 is 8 bytes wide.
    /// </summary>
    public enum TableColumnType : byte
    {
        /// <summary>UTC time as 64-bit ticks (100 ns since 0001-01-01), within <c>0..DateTime.MaxValue.Ticks</c>.</summary>
        Time = 1,

        /// <summary>Signed 64-bit integer.</summary>
        Int64 = 2,

        /// <summary>IEEE 754 binary64; stored bit for bit (NaN payloads, infinities and -0.0 survive).</summary>
        Double = 3,
    }
}
