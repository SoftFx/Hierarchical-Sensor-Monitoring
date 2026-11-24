using System;
using System.Collections.Generic;

namespace HSMTableBlocks
{
    /// <summary>A decoded table block: its header and every table in directory order.</summary>
    public sealed class TableBlock
    {
        /// <summary>The verified header.</summary>
        public TableBlockHeader Header { get; }

        /// <summary>The tables in the order of the directory.</summary>
        public IReadOnlyList<BlockTable> Tables { get; }


        internal TableBlock(TableBlockHeader header, IReadOnlyList<BlockTable> tables)
        {
            Header = header;
            Tables = tables;
        }
    }


    /// <summary>
    /// One table of a block: its identifier, schema id and columns. The rows keep the order the writer was given;
    /// they are not sorted by time and times may repeat. The arrays belong to the caller.
    /// </summary>
    public sealed class BlockTable
    {
        /// <summary>The opaque table identifier, as written.</summary>
        public byte[] TableId { get; }

        /// <summary>The schema id, as written.</summary>
        public uint SchemaId { get; }

        /// <summary>The number of rows; every column holds this many values.</summary>
        public int RowCount { get; }

        /// <summary>The columns in order; the first is <see cref="TableColumnType.Time"/>.</summary>
        public IReadOnlyList<BlockColumn> Columns { get; }

        /// <summary>The time column (the first column), in UTC ticks.</summary>
        public long[] Time => Columns[0].Int64Values;


        internal BlockTable(byte[] tableId, uint schemaId, int rowCount, IReadOnlyList<BlockColumn> columns)
        {
            TableId = tableId;
            SchemaId = schemaId;
            RowCount = rowCount;
            Columns = columns;
        }
    }


    /// <summary>
    /// The directory of a verified block (<see cref="TableBlockReader.ReadDirectory"/>): its header and one entry per
    /// table, in directory order, without the column values.
    /// </summary>
    public sealed class TableBlockDirectory
    {
        /// <summary>The verified header.</summary>
        public TableBlockHeader Header { get; }

        /// <summary>The entries in the order of the directory; the index of an entry is its table's position in the block.</summary>
        public IReadOnlyList<TableDirectoryEntry> Entries { get; }


        internal TableBlockDirectory(TableBlockHeader header, IReadOnlyList<TableDirectoryEntry> entries)
        {
            Header = header;
            Entries = entries;
        }
    }


    /// <summary>One directory entry: a table's identifier, schema id, row count and column types.</summary>
    public sealed class TableDirectoryEntry
    {
        /// <summary>The opaque table identifier, as written.</summary>
        public byte[] TableId { get; }

        /// <summary>The schema id, as written.</summary>
        public uint SchemaId { get; }

        /// <summary>The number of rows of the table.</summary>
        public int RowCount { get; }

        /// <summary>
        /// The column types in order; the first is <see cref="TableColumnType.Time"/>. Plain types: whether a column has
        /// nulls in this block is <see cref="HasNulls"/>, so the types compare with a schema's as they are.
        /// </summary>
        public IReadOnlyList<TableColumnType> ColumnTypes { get; }

        private readonly bool[] _nulls;


        internal TableDirectoryEntry(byte[] tableId, uint schemaId, int rowCount, IReadOnlyList<TableColumnType> columnTypes, bool[] nulls = null)
        {
            TableId = tableId;
            SchemaId = schemaId;
            RowCount = rowCount;
            ColumnTypes = columnTypes;
            _nulls = nulls;
        }


        /// <summary>True when column <paramref name="column"/> carries a null map in this block: at least one of its rows is null.</summary>
        public bool HasNulls(int column)
        {
            if ((uint)column >= (uint)ColumnTypes.Count)
                throw new ArgumentOutOfRangeException(nameof(column), column, $"the table has {ColumnTypes.Count} columns");

            return _nulls != null && _nulls[column];
        }
    }


    /// <summary>One column of a <see cref="BlockTable"/>.</summary>
    public sealed class BlockColumn
    {
        /// <summary>The type of the column.</summary>
        public TableColumnType Type { get; }

        /// <summary>The values of a <see cref="TableColumnType.Time"/> or <see cref="TableColumnType.Int64"/> column; null for a double column.</summary>
        public long[] Int64Values { get; }

        /// <summary>The values of a <see cref="TableColumnType.Double"/> column; null otherwise.</summary>
        public double[] DoubleValues { get; }

        /// <summary>
        /// The rows of this column that hold no value, as the block carries them (<see cref="TableNullMap"/>: bit i set
        /// = row i is null); null when the column has no null in this block. A null row holds 0 in
        /// <see cref="Int64Values"/> or <see cref="DoubleValues"/>: a consumer that does not look here reads that 0.
        /// </summary>
        public byte[] NullMap { get; }

        /// <summary>True when at least one row of this column is null in this block.</summary>
        public bool HasNulls => NullMap != null;

        /// <summary>True when row <paramref name="row"/> holds no value.</summary>
        public bool IsNull(int row)
        {
            int rows = Int64Values?.Length ?? DoubleValues.Length;
            if ((uint)row >= (uint)rows)
                throw new ArgumentOutOfRangeException(nameof(row), row, $"the column has {rows} rows");

            return NullMap != null && TableNullMap.IsNull(NullMap, row);
        }


        internal BlockColumn(TableColumnType type, long[] int64Values, double[] doubleValues, byte[] nullMap = null)
        {
            Type = type;
            Int64Values = int64Values;
            DoubleValues = doubleValues;
            NullMap = nullMap;
        }
    }
}
