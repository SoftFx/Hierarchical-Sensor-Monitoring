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


    /// <summary>One column of a <see cref="BlockTable"/>.</summary>
    public sealed class BlockColumn
    {
        /// <summary>The type of the column.</summary>
        public TableColumnType Type { get; }

        /// <summary>The values of a <see cref="TableColumnType.Time"/> or <see cref="TableColumnType.Int64"/> column; null for a double column.</summary>
        public long[] Int64Values { get; }

        /// <summary>The values of a <see cref="TableColumnType.Double"/> column; null otherwise.</summary>
        public double[] DoubleValues { get; }


        internal BlockColumn(TableColumnType type, long[] int64Values, double[] doubleValues)
        {
            Type = type;
            Int64Values = int64Values;
            DoubleValues = doubleValues;
        }
    }
}
