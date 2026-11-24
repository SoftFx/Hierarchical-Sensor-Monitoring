using System;
using System.Collections.Generic;
using System.IO;
using HSMTableBlocks;

namespace HSMDatabase.SealedHistory
{
    /// <summary>
    /// One column of a <see cref="TableBatch"/>: its type and its values, one per row. A time or int64 column holds
    /// <see cref="Int64Values"/>, a double column <see cref="DoubleValues"/>. <see cref="NullMap"/> marks the rows without a
    /// value in the bit order of <see cref="TableNullMap"/> (null: no null row); the value of a null row is ignored by the
    /// encoder and is 0 in what the decoder returns. The first column of a batch is the time and has no null row.
    /// </summary>
    public sealed class TableBatchColumn
    {
        public TableBatchColumn(TableColumnType type, long[] int64Values, double[] doubleValues, byte[] nullMap = null)
        {
            Type = type;
            Int64Values = int64Values;
            DoubleValues = doubleValues;
            NullMap = nullMap;
        }


        public TableColumnType Type { get; }

        public long[] Int64Values { get; }

        public double[] DoubleValues { get; }

        public byte[] NullMap { get; }

        public int Length => Type == TableColumnType.Double ? DoubleValues?.Length ?? 0 : Int64Values?.Length ?? 0;

        public bool IsNull(int row) => NullMap is not null && TableNullMap.IsNull(NullMap, row);


        public static TableBatchColumn Time(long[] ticks) => new(TableColumnType.Time, ticks, null);

        public static TableBatchColumn Int64(long[] values, byte[] nullMap = null) => new(TableColumnType.Int64, values, null, nullMap);

        public static TableBatchColumn Double(double[] values, byte[] nullMap = null) => new(TableColumnType.Double, null, values, nullMap);
    }


    /// <summary>
    /// The rows of one table sensor that one stored batch holds (#1850): 1 to <see cref="TableBatchCodec.MaxRows"/> rows
    /// of the sensor's columns, the first of which is the time. The codec takes the rows in the order given; the merge
    /// sorts them by time before.
    /// </summary>
    public sealed class TableBatch
    {
        public TableBatch(uint schemaId, IReadOnlyList<TableBatchColumn> columns)
        {
            SchemaId = schemaId;
            Columns = columns ?? throw new ArgumentNullException(nameof(columns));
        }


        public uint SchemaId { get; }

        public IReadOnlyList<TableBatchColumn> Columns { get; }

        public int RowCount => Columns.Count == 0 ? 0 : Columns[0].Length;

        public long[] Time => Columns[0].Int64Values;
    }


    /// <summary>The 48-byte header of a stored batch, as read and verified by <see cref="TableBatchCodec.ReadHeader"/>.</summary>
    public readonly record struct TableBatchHeader(
        byte FormatVersion,
        byte EncoderRevision,
        uint SchemaId,
        int RowCount,
        int ColumnCount,
        long FirstTime,
        long LastTime,
        bool Compressed,
        int PayloadLength,
        int StoredLength);


    /// <summary>
    /// What the encoder chose, counted over the batches it was given (for the merge's self-monitoring and for
    /// measurements). Not thread-safe: one instance per encoding thread.
    /// </summary>
    public sealed class TableBatchEncodeStats
    {
        /// <summary>Column streams written coded, by the kind of column (time = the first column).</summary>
        public long TimeCoded, Int64Coded, DoubleCoded;

        /// <summary>Column streams written as raw 8-byte values because that was smaller after zstd 1.</summary>
        public long TimeRaw, Int64Raw, DoubleRaw;

        /// <summary>Double values set aside as exceptions so that their column kept the decimal mode.</summary>
        public long ExceptionCells;

        /// <summary>Double columns (with at least two distinct values) that kept the decimal mode only thanks to exceptions.</summary>
        public long DecimalKeptByExceptions;

        /// <summary>Double columns (with at least two distinct values) that have no decimal mode, even with exceptions.</summary>
        public long DecimalGivenUp;

        /// <summary>Null cells of value columns, left out of the coded streams.</summary>
        public long NullCells;

        public long Batches;

        public long Rows;

        public long StoredBytes;
    }


    public enum TableBatchFormatError
    {
        /// <summary>The bytes do not start with the batch magic <c>HT</c>.</summary>
        NotABatch,
        /// <summary>The batch was written in a format version this build cannot read.</summary>
        UnknownVersion,
        /// <summary>The bytes end before the header or before the stored length the header declares.</summary>
        Truncated,
        /// <summary>The checksum over the header and the stored payload does not match.</summary>
        ChecksumMismatch,
        /// <summary>
        /// A length does not agree: bytes after the stored payload, a payload length outside what the row and column
        /// counts allow, a decompressed payload of another length, bytes after the last column.
        /// </summary>
        LengthMismatch,
        /// <summary>A header field is out of range: reserved bytes, flags, row or column count, first and last time.</summary>
        CorruptHeader,
        /// <summary>The stored payload is not exactly one zstd frame of the declared size, or it does not decompress.</summary>
        CorruptFrame,
        /// <summary>The column types or a column stream cannot be decoded: an unknown type or encoding, a stream cut short, a bad mode or varint.</summary>
        CorruptStream,
        /// <summary>A count inside the payload does not agree: an exception list too long, out of order or outside the column, exceptions beside a raw stream.</summary>
        CountMismatch,
        /// <summary>The decoded time column does not have the first and last time of the header.</summary>
        HeaderMismatch,
    }


    /// <summary>
    /// A stored batch cannot be read. Never an empty or partial result: the caller gets this exception instead.
    /// </summary>
    public sealed class TableBatchFormatException : IOException
    {
        public TableBatchFormatException(TableBatchFormatError error, string message, Exception inner = null)
            : base($"Table batch: {error}: {message}", inner)
        {
            Error = error;
        }


        public TableBatchFormatError Error { get; }
    }
}
