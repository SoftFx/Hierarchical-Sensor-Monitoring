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


        internal TableRowBuffer(bool[] doubleColumns, Func<Action> growthHook = null)
        {
            _growthHook = growthHook;
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
        /// Appends one row. The cells are checked by the sensor before (count and types). The only failure is memory
        /// while the buffer grows, and it leaves the buffer as it was: every array is allocated before any is swapped in
        /// and before any row is written.
        /// </summary>
        internal void Append(long ticks, ReadOnlySpan<TableCell> cells)
        {
            Prepare(1, cells);
            WriteRow(Count, ticks, cells);
            Count++;
        }

        /// <summary>Appends <paramref name="rows"/> rows; <paramref name="cells"/> is row-major. All or nothing, as the single row.</summary>
        internal void Append(ReadOnlySpan<DateTime> times, ReadOnlySpan<TableCell> cells, int rows)
        {
            Prepare(rows, cells);

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

        // The null map exists: Prepare made it before the first row was written.
        private void MarkNull(int column, int row)
        {
            TableNullMap.SetNull(_nulls[column], row);
            _hasNulls[column] = true;
        }

        // For tests: what runs before the arrays of a growth are allocated, so a test can make the allocation fail.
        private readonly Func<Action> _growthHook;

        // Makes room for `rows` more rows and a null map for every column a null cell of them needs. Every new array is
        // allocated first; only when all of them exist are they swapped in, so an allocation that fails (out of memory)
        // leaves the buffer exactly as it was.
        private void Prepare(int rows, ReadOnlySpan<TableCell> cells)
        {
            var needed = Count + rows;
            var grows = _times == null || _times.Length < needed;

            // At most 255 columns (the format): a small stack buffer.
            Span<bool> needsMap = stackalloc bool[_columnCount];
            var anyMap = false;

            for (var column = 0; column < _columnCount; column++)
            {
                if (_nulls[column] != null)
                    continue;

                for (var cell = column; cell < cells.Length; cell += _columnCount)
                    if (cells[cell].Kind == TableCell.KindNone)
                    {
                        needsMap[column] = true;
                        anyMap = true;
                        break;
                    }
            }

            if (!grows && !anyMap)
                return;

            var capacity = _times?.Length ?? 0;

            if (grows)
            {
                capacity = Math.Max(InitialCapacity, capacity);

                while (capacity < needed)
                    capacity = capacity > int.MaxValue / 2 ? needed : capacity * 2;
            }

            _growthHook?.Invoke()?.Invoke();

            var times = grows ? new long[capacity] : _times;
            var values = new long[_columnCount][];
            var nulls = new byte[_columnCount][];

            for (var column = 0; column < _columnCount; column++)
            {
                values[column] = grows ? new long[capacity] : _values[column];

                if (_nulls[column] != null)
                    nulls[column] = grows ? new byte[TableNullMap.ByteLength(capacity)] : _nulls[column];
                else if (needsMap[column])
                    nulls[column] = new byte[TableNullMap.ByteLength(capacity)];
            }

            // Nothing below allocates: copy and swap.
            if (grows && _times != null)
            {
                Array.Copy(_times, times, Count);

                for (var column = 0; column < _columnCount; column++)
                {
                    Array.Copy(_values[column], values[column], Count);

                    if (_nulls[column] != null)
                        Buffer.BlockCopy(_nulls[column], 0, nulls[column], 0, TableNullMap.ByteLength(Count));
                }
            }

            _times = times;

            for (var column = 0; column < _columnCount; column++)
            {
                _values[column] = values[column];
                _nulls[column] = nulls[column];
            }
        }
    }
}
