using System;
using HSMDataCollector.PublicInterface;
using HSMTableBlocks;

namespace HSMDataCollector.Tables
{
    /// <summary>
    /// The rows of one table sensor between two ticks, column by column: the times, every value column as 64-bit
    /// words (a double as its bits), and a null map for a column only once a row of it has no value. Not thread-safe:
    /// its sensor appends under its own lock, and a tick reads a buffer it has taken out of the sensor.
    /// </summary>
    internal sealed class TableRowBuffer
    {
        private const int InitialCapacity = 64;

        private readonly int _columnCount;
        private readonly bool[] _doubleColumns;

        private long[] _times;
        private readonly long[][] _values;
        private readonly byte[][] _nulls;
        private readonly bool[] _hasNulls;


        internal TableRowBuffer(bool[] doubleColumns)
        {
            _doubleColumns = doubleColumns;
            _columnCount = doubleColumns.Length;
            _values = new long[_columnCount][];
            _nulls = new byte[_columnCount][];
            _hasNulls = new bool[_columnCount];
        }


        internal int Count { get; private set; }

        internal int ColumnCount => _columnCount;

        internal ReadOnlySpan<long> Times => new ReadOnlySpan<long>(_times, 0, Count);

        internal ReadOnlySpan<long> Values(int column) => new ReadOnlySpan<long>(_values[column], 0, Count);

        internal bool HasNulls(int column) => _hasNulls[column];

        internal bool IsNull(int column, int row) => _hasNulls[column] && TableNullMap.IsNull(_nulls[column], row);


        /// <summary>
        /// Appends one row. The cells are checked by the sensor before (count and types), so this cannot fail but for
        /// memory.
        /// </summary>
        internal void Append(long ticks, ReadOnlySpan<TableCell> cells)
        {
            EnsureCapacity(Count + 1);
            WriteRow(Count, ticks, cells);
            Count++;
        }

        /// <summary>Appends <paramref name="rows"/> rows; <paramref name="cells"/> is row-major.</summary>
        internal void Append(ReadOnlySpan<DateTime> times, ReadOnlySpan<TableCell> cells, int rows)
        {
            EnsureCapacity(Count + rows);

            for (var row = 0; row < rows; row++)
                WriteRow(Count + row, TableSensor.ToUtcTicks(times[row]), cells.Slice(row * _columnCount, _columnCount));

            Count += rows;
        }

        /// <summary>
        /// The capacity in rows a cleared buffer keeps. A buffer that grew past it gives its arrays up when it is
        /// cleared: the bytes of the rows left the collector-wide bound when they were packed, and arrays kept beyond
        /// that would be memory no bound covers (#1846 review, item 1). A sensor has two buffers, so a quiet sensor
        /// keeps at most 2 x 256 rows' worth of arrays: 4 KiB per column, the time included.
        /// </summary>
        internal const int RetainedCapacity = 256;

        /// <summary>The rows the arrays can take now without growing.</summary>
        internal int Capacity => _times?.Length ?? 0;

        /// <summary>
        /// Forgets the rows. A buffer within <see cref="RetainedCapacity"/> keeps its arrays, so a quiet sensor does not
        /// allocate every tick; a larger one gives them up.
        /// </summary>
        internal void Clear()
        {
            if (Capacity > RetainedCapacity)
            {
                _times = null;

                for (var column = 0; column < _columnCount; column++)
                {
                    _values[column] = null;
                    _nulls[column] = null;
                    _hasNulls[column] = false;
                }

                Count = 0;
                return;
            }

            for (var column = 0; column < _columnCount; column++)
                if (_hasNulls[column])
                {
                    Array.Clear(_nulls[column], 0, TableNullMap.ByteLength(Count));
                    _hasNulls[column] = false;
                }

            Count = 0;
        }

        /// <summary>
        /// Writes the null map of rows <c>[start, start + count)</c> of a column into <paramref name="map"/>, which has
        /// exactly <see cref="TableNullMap.ByteLength"/>(count) bytes, all zero.
        /// </summary>
        internal void CopyNullMap(int column, int start, int count, Span<byte> map)
        {
            var source = _nulls[column];

            for (var row = 0; row < count; row++)
                if (TableNullMap.IsNull(source, start + row))
                    TableNullMap.SetNull(map, row);
        }


        private void WriteRow(int row, long ticks, ReadOnlySpan<TableCell> cells)
        {
            _times[row] = ticks;

            for (var column = 0; column < _columnCount; column++)
            {
                var cell = cells[column];

                if (cell.Kind == TableCell.KindNone)
                {
                    _values[column][row] = 0;
                    MarkNull(column, row);
                }
                else
                {
                    // An integer cell in a double column is converted; the sensor refuses the other way round.
                    _values[column][row] = cell.Kind == TableCell.KindInt64 && _doubleColumns[column]
                        ? BitConverter.DoubleToInt64Bits(cell.Bits)
                        : cell.Bits;
                }
            }
        }

        private void MarkNull(int column, int row)
        {
            var map = _nulls[column];
            var needed = TableNullMap.ByteLength(_times.Length);

            if (map == null || map.Length < needed)
            {
                var grown = new byte[needed];

                if (map != null)
                    Buffer.BlockCopy(map, 0, grown, 0, map.Length);

                _nulls[column] = map = grown;
            }

            TableNullMap.SetNull(map, row);
            _hasNulls[column] = true;
        }

        private void EnsureCapacity(int needed)
        {
            if (_times != null && _times.Length >= needed)
                return;

            var capacity = Math.Max(InitialCapacity, _times == null ? 0 : _times.Length);

            while (capacity < needed)
                capacity = capacity > int.MaxValue / 2 ? needed : capacity * 2;

            Array.Resize(ref _times, capacity);

            for (var column = 0; column < _columnCount; column++)
            {
                Array.Resize(ref _values[column], capacity);

                if (_nulls[column] != null)
                    Array.Resize(ref _nulls[column], TableNullMap.ByteLength(capacity));
            }
        }
    }
}
