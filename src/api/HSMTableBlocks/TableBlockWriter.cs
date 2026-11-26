using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ZstdSharp;

namespace HSMTableBlocks
{
    /// <summary>
    /// Builds one table block: the rows of several tables, grouped by table, as raw little-endian columns under one zstd
    /// frame with a fixed header in front (ADR-0021). Add tables with <see cref="BeginTable"/> followed by the time
    /// column and then the other columns; <see cref="ToArray"/> returns the block. The rows are written in the order
    /// given: the writer neither sorts nor requires time order, and times may repeat. In the payload the columns are
    /// laid out column-major: column 0 of every table, then column 1 of every table that has one, and so on.
    /// <para>
    /// Every column but the first (the time) may have rows without a value: the overloads with a null map
    /// (<see cref="TableNullMap"/>). A column with at least one null carries its map after all columns; a block without
    /// nulls is byte for byte what it would be without the overloads.
    /// </para>
    /// <para>
    /// Not thread-safe: use one instance from one thread at a time. An instance is meant to be reused, one block per
    /// tick: <see cref="Reset"/> keeps its buffers. After any exception from a method of the writer, call
    /// <see cref="Reset"/> before using it again.
    /// </para>
    /// </summary>
    public sealed class TableBlockWriter : IDisposable
    {
        /// <summary>The zstd level used when none is given.</summary>
        public const int DefaultCompressionLevel = 3;

        private readonly TableBlockLimits _limits;
        private readonly Compressor _compressor;
        private readonly IdOrder _idOrder;

        private byte[] _directory = new byte[1024];
        private int _directoryLength;
        // Columns as added, table after table; ToArray lays them out column-major.
        private byte[] _data = new byte[16 * 1024];
        private int _dataLength;
        private TableInfo[] _tables = new TableInfo[64];

        // Null maps as added, and per column (in the order added, every table's columns in turn) the offset of its map in
        // _nullData or -1. Kept between blocks like the other buffers.
        private byte[] _nullData = new byte[256];
        private int _nullDataLength;
        private int[] _columnMaps = new int[256];
        private int _columnCount;

        // The payload before compression and the compressed frame, kept between blocks.
        private byte[] _payload;
        private byte[] _compressed;
        private int _maxColumns;

        private int _tableCount;
        private int _rowCount;
        private long _firstTime = long.MaxValue;
        private long _lastTime = long.MinValue;

        // The open table: the offset of its column count byte in the directory, its row and column counts.
        private int _columnCountOffset = -1;
        private int _openRows;
        private int _openColumns;
        private bool _disposed;


        /// <summary>The number of tables added so far.</summary>
        public int TableCount => _tableCount;

        /// <summary>The number of rows added so far, over all tables.</summary>
        public int RowCount => _rowCount;

        /// <summary>The length the payload would have now, before compression: the directory, every column and every null map added.</summary>
        public int DecompressedLength => _directoryLength + _dataLength + _nullDataLength;

        /// <summary>The zstd level of this writer.</summary>
        public int CompressionLevel { get; }

        /// <summary>The limits this writer keeps to.</summary>
        public TableBlockLimits Limits => _limits;

        /// <summary>Counts the identifier comparisons of the uniqueness check, for the tests; null in production.</summary>
        internal IdComparisonCounter IdComparisonCounter { get; set; }


        /// <summary>Creates a writer with zstd level <see cref="DefaultCompressionLevel"/> and the default limits.</summary>
        public TableBlockWriter() : this(DefaultCompressionLevel) { }

        /// <summary>Creates a writer with the given zstd level and limits (null: <see cref="TableBlockLimits.Default"/>).</summary>
        public TableBlockWriter(int compressionLevel, TableBlockLimits limits = null)
        {
            if (compressionLevel < Compressor.MinCompressionLevel || compressionLevel > Compressor.MaxCompressionLevel)
                throw new ArgumentOutOfRangeException(nameof(compressionLevel), compressionLevel,
                    $"must be within {Compressor.MinCompressionLevel}..{Compressor.MaxCompressionLevel}");

            CompressionLevel = compressionLevel;
            _limits = limits ?? TableBlockLimits.Default;
            _compressor = new Compressor(compressionLevel);
            _idOrder = new IdOrder(this);
        }


        /// <summary>
        /// Opens a new table of <paramref name="rowCount"/> rows and closes the previous one. The next column must be the
        /// time column (<see cref="AddTimeColumn"/>). The identifier is opaque bytes, unique within the block;
        /// <see cref="ToArray"/> checks that.
        /// </summary>
        public void BeginTable(ReadOnlySpan<byte> tableId, uint schemaId, int rowCount)
        {
            ThrowIfDisposed();
            CloseTable();

            if (tableId.Length < 1 || tableId.Length > _limits.MaxTableIdLength)
                throw new ArgumentOutOfRangeException(nameof(tableId), tableId.Length, $"the identifier length must be within 1..{_limits.MaxTableIdLength}");
            if (rowCount < 1)
                throw new ArgumentOutOfRangeException(nameof(rowCount), rowCount, "a table has at least one row");
            if (_tableCount + 1 > _limits.MaxTables)
                throw new ArgumentOutOfRangeException(nameof(tableId), $"the block would hold more than {_limits.MaxTables} tables");
            if ((long)_rowCount + rowCount > _limits.MaxRows)
                throw new ArgumentOutOfRangeException(nameof(rowCount), rowCount, $"the block would hold more than {_limits.MaxRows} rows");

            int entryLength = TableBlockFormat.EntryFixedSize + tableId.Length;
            CheckPayloadGrowth(entryLength + 1L + (long)rowCount * TableBlockFormat.ValueSize);

            int start = _directoryLength;
            var entry = Grow(ref _directory, ref _directoryLength, entryLength);
            BinaryPrimitives.WriteUInt16LittleEndian(entry, (ushort)tableId.Length);
            tableId.CopyTo(entry.Slice(2));
            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(2 + tableId.Length), schemaId);
            BinaryPrimitives.WriteInt32LittleEndian(entry.Slice(6 + tableId.Length), rowCount);
            entry[10 + tableId.Length] = 0;

            if (_tableCount == _tables.Length)
                Array.Resize(ref _tables, _tables.Length * 2);
            _tables[_tableCount] = new TableInfo(start + 2, tableId.Length, _dataLength, rowCount, _columnCount);

            _columnCountOffset = start + entryLength - 1;
            _openRows = rowCount;
            _openColumns = 0;
            _tableCount++;
            _rowCount += rowCount;
        }

        /// <summary>
        /// Adds a time column (UTC ticks within <c>0..DateTime.MaxValue.Ticks</c>). The first column of every table is
        /// a time column; its values set the block's first and last time.
        /// </summary>
        public void AddTimeColumn(ReadOnlySpan<long> ticks) => AddTimeColumn(ticks, ReadOnlySpan<byte>.Empty);

        /// <summary>
        /// Adds a time column whose rows marked in <paramref name="nulls"/> hold no value (see
        /// <see cref="AddInt64Column(ReadOnlySpan{long}, ReadOnlySpan{byte})"/>); the map is checked by the same rules. The
        /// time of a row is never null: for the first column a map that marks a row throws
        /// <see cref="InvalidOperationException"/>, while an empty map or one that marks no row is no map, as for every
        /// column. The value of a null row is not checked.
        /// </summary>
        public void AddTimeColumn(ReadOnlySpan<long> ticks, ReadOnlySpan<byte> nulls)
        {
            // The map is checked (length, padding) first, as for every column; only a marked row is refused here.
            bool first = _columnCountOffset >= 0 && _openColumns == 0;
            if (first && HasNulls(nulls, ticks.Length))
                throw new InvalidOperationException("the first column is the time of the row, which is never null: its map may mark no row");

            bool hasNulls = CheckColumn(ticks.Length, TableColumnType.Time, first ? ReadOnlySpan<byte>.Empty : nulls);

            long min = long.MaxValue, max = long.MinValue;
            for (int i = 0; i < ticks.Length; i++)
            {
                if (hasNulls && TableNullMap.IsNull(nulls, i))
                    continue;

                long t = ticks[i];
                if (t < 0 || t > TableBlockFormat.MaxTimeTicks)
                    throw new ArgumentOutOfRangeException(nameof(ticks), t, $"a time must be within 0..{TableBlockFormat.MaxTimeTicks}");
                if (t < min) min = t;
                if (t > max) max = t;
            }

            if (_openColumns == 0)
            {
                if (min < _firstTime) _firstTime = min;
                if (max > _lastTime) _lastTime = max;
            }

            AppendColumn(TableColumnType.Time, ticks, hasNulls ? nulls : ReadOnlySpan<byte>.Empty);
        }

        /// <summary>Adds a 64-bit integer column.</summary>
        public void AddInt64Column(ReadOnlySpan<long> values) => AddInt64Column(values, ReadOnlySpan<byte>.Empty);

        /// <summary>
        /// Adds a 64-bit integer column whose rows marked in <paramref name="nulls"/> hold no value. <paramref name="nulls"/>
        /// is empty (no nulls: the same as the overload without it) or a map of exactly
        /// <see cref="TableNullMap.ByteLength"/>(row count) bytes in the order of <see cref="TableNullMap"/>, its padding
        /// bits zero (<see cref="ArgumentException"/> otherwise). The values of null rows are ignored and written as 0. A
        /// map with no row set is the same as no map: the column is written exactly as without nulls.
        /// </summary>
        public void AddInt64Column(ReadOnlySpan<long> values, ReadOnlySpan<byte> nulls)
        {
            bool hasNulls = CheckColumn(values.Length, TableColumnType.Int64, nulls);
            AppendColumn(TableColumnType.Int64, values, hasNulls ? nulls : ReadOnlySpan<byte>.Empty);
        }

        /// <summary>Adds a double column; every value is stored bit for bit.</summary>
        public void AddDoubleColumn(ReadOnlySpan<double> values) => AddDoubleColumn(values, ReadOnlySpan<byte>.Empty);

        /// <summary>
        /// Adds a double column whose rows marked in <paramref name="nulls"/> hold no value; the rules of
        /// <see cref="AddInt64Column(ReadOnlySpan{long}, ReadOnlySpan{byte})"/> apply. Every other value is stored bit for bit.
        /// </summary>
        public void AddDoubleColumn(ReadOnlySpan<double> values, ReadOnlySpan<byte> nulls)
        {
            bool hasNulls = CheckColumn(values.Length, TableColumnType.Double, nulls);
            AppendColumn(TableColumnType.Double, MemoryMarshal.Cast<double, long>(values), hasNulls ? nulls : ReadOnlySpan<byte>.Empty);
        }

        /// <summary>
        /// Closes the open table and returns the block: header and one zstd frame. The writer keeps its content; call
        /// <see cref="Reset"/> to start the next block. A block holds at least one table, and no two tables share an
        /// identifier (<see cref="ArgumentException"/> otherwise).
        /// </summary>
        public byte[] ToArray()
        {
            ThrowIfDisposed();
            CloseTable();

            if (_tableCount == 0)
                throw new InvalidOperationException("a block holds at least one table");

            int duplicate = _idOrder.FindDuplicate(_tableCount);
            if (duplicate >= 0)
                throw new ArgumentException($"table {duplicate} has the identifier of another table: a table appears once in a block");

            // The payload and compressed buffers are the writer's own and kept between blocks: the shared array pool
            // of netstandard2.0 consumers (net472) does not keep arrays above about 1 MB, so renting them would
            // allocate and drop two large arrays every tick.
            int payloadLength = DecompressedLength;
            var payload = EnsureSize(ref _payload, payloadLength);
            var compressed = EnsureSize(ref _compressed, Compressor.GetCompressBound(payloadLength));

            _directory.AsSpan(0, _directoryLength).CopyTo(payload);
            LayOutColumnMajor(payload.AsSpan(_directoryLength, _dataLength));
            LayOutNullMaps(payload.AsSpan(_directoryLength + _dataLength, _nullDataLength));

            int written = _compressor.Wrap(payload.AsSpan(0, payloadLength), compressed);

            var block = new byte[TableBlockFormat.HeaderSize + written];
            var header = block.AsSpan(0, TableBlockFormat.HeaderSize);
            TableBlockFormat.WriteHeader(header, _rowCount, _tableCount, _firstTime, _lastTime, written, payloadLength);
            compressed.AsSpan(0, written).CopyTo(block.AsSpan(TableBlockFormat.HeaderSize));
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(TableBlockFormat.ChecksumOffset),
                TableBlockFormat.Checksum(header, block.AsSpan(TableBlockFormat.HeaderSize)));

            return block;
        }

        private static byte[] EnsureSize(ref byte[] buffer, int size)
        {
            if (buffer == null || buffer.Length < size)
                buffer = new byte[Math.Max(size, buffer == null ? 0 : (int)Math.Min(int.MaxValue, buffer.Length * 2L))];

            return buffer;
        }

        /// <summary>Forgets every table and keeps the buffers, ready for the next block.</summary>
        public void Reset()
        {
            ThrowIfDisposed();
            _directoryLength = 0;
            _dataLength = 0;
            _nullDataLength = 0;
            _columnCount = 0;
            _tableCount = 0;
            _rowCount = 0;
            _firstTime = long.MaxValue;
            _lastTime = long.MinValue;
            _columnCountOffset = -1;
            _openRows = 0;
            _openColumns = 0;
            _maxColumns = 0;
        }

        /// <summary>Releases the zstd context and the buffers.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _compressor.Dispose();
            _payload = null;
            _compressed = null;
        }


        /// <summary>Checks a column before anything is copied; true when <paramref name="nulls"/> marks at least one row.</summary>
        private bool CheckColumn(int length, TableColumnType type, ReadOnlySpan<byte> nulls)
        {
            ThrowIfDisposed();

            if (_columnCountOffset < 0)
                throw new InvalidOperationException("call BeginTable before adding columns");
            if (_openColumns == 0 && type != TableColumnType.Time)
                throw new InvalidOperationException("the first column of a table is the time column");
            if (_openColumns + 1 > _limits.MaxColumnsPerTable)
                throw new ArgumentOutOfRangeException(nameof(type), $"a table would have more than {_limits.MaxColumnsPerTable} columns");
            if (length != _openRows)
                throw new ArgumentException($"the column has {length} values, the table {_openRows} rows");

            bool hasNulls = HasNulls(nulls, length);

            // The table's column type byte, its values (the first column was counted by BeginTable) and its null map.
            long growth = (_openColumns > 0 ? 1L + (long)length * TableBlockFormat.ValueSize : 0) + (hasNulls ? TableNullMap.ByteLength(length) : 0);
            if (growth > 0)
                CheckPayloadGrowth(growth);

            return hasNulls;
        }

        /// <summary>Checks a null map given for a column of <paramref name="rows"/> rows; true when it marks at least one row.</summary>
        private static bool HasNulls(ReadOnlySpan<byte> nulls, int rows)
        {
            if (nulls.IsEmpty)
                return false;

            if (nulls.Length != TableNullMap.ByteLength(rows))
                throw new ArgumentException($"the null map has {nulls.Length} bytes, a column of {rows} rows needs {TableNullMap.ByteLength(rows)}", nameof(nulls));

            int tail = rows & 7;
            if (tail != 0 && (nulls[nulls.Length - 1] >> tail) != 0)
                throw new ArgumentException($"the null map has bits set past row {rows - 1}", nameof(nulls));

            foreach (byte b in nulls)
                if (b != 0)
                    return true;

            return false;
        }

        /// <summary>Appends a column and, when <paramref name="nulls"/> is not empty (it then marks a row), its null map.</summary>
        private void AppendColumn(TableColumnType type, ReadOnlySpan<long> values, ReadOnlySpan<byte> nulls)
        {
            bool hasNulls = !nulls.IsEmpty;
            Grow(ref _directory, ref _directoryLength, 1)[0] = (byte)((byte)type | (hasNulls ? TableBlockFormat.NullFlag : 0));

            var destination = Grow(ref _data, ref _dataLength, values.Length * TableBlockFormat.ValueSize);
            TableBlockFormat.WriteValues(values, destination);

            if (_columnCount == _columnMaps.Length)
                Array.Resize(ref _columnMaps, _columnMaps.Length * 2);

            if (hasNulls)
            {
                // A null cell holds eight zero bytes, whatever the caller left in it.
                for (int b = 0; b < nulls.Length; b++)
                    for (int bit = 0; nulls[b] >> bit != 0; bit++)
                        if ((nulls[b] & (1 << bit)) != 0)
                            destination.Slice((b * 8 + bit) * TableBlockFormat.ValueSize, TableBlockFormat.ValueSize).Clear();

                _columnMaps[_columnCount] = _nullDataLength;
                nulls.CopyTo(Grow(ref _nullData, ref _nullDataLength, nulls.Length));
            }
            else
            {
                _columnMaps[_columnCount] = -1;
            }

            _columnCount++;
            _openColumns++;
            _directory[_columnCountOffset] = (byte)_openColumns;
            _tables[_tableCount - 1].Columns = _openColumns;
            if (_openColumns > _maxColumns)
                _maxColumns = _openColumns;
        }

        /// <summary>Copies the null maps after the columns, in the order of the columns: pass by pass, table by table.</summary>
        private void LayOutNullMaps(Span<byte> destination)
        {
            if (_nullDataLength == 0)
                return;

            int at = 0;
            for (int c = 0; c < _maxColumns; c++)
            {
                for (int t = 0; t < _tableCount; t++)
                {
                    var table = _tables[t];
                    if (c >= table.Columns)
                        continue;

                    int offset = _columnMaps[table.FirstColumn + c];
                    if (offset < 0)
                        continue;

                    int size = TableNullMap.ByteLength(table.Rows);
                    _nullData.AsSpan(offset, size).CopyTo(destination.Slice(at));
                    at += size;
                }
            }
        }

        /// <summary>Copies the columns, stored table after table, into the payload column-major.</summary>
        private void LayOutColumnMajor(Span<byte> destination)
        {
            int at = 0;
            for (int c = 0; c < _maxColumns; c++)
            {
                for (int t = 0; t < _tableCount; t++)
                {
                    var table = _tables[t];
                    if (c >= table.Columns)
                        continue;

                    int size = table.Rows * TableBlockFormat.ValueSize;
                    _data.AsSpan(table.DataOffset + c * size, size).CopyTo(destination.Slice(at));
                    at += size;
                }
            }
        }

        private void CloseTable()
        {
            if (_columnCountOffset >= 0 && _openColumns == 0)
                throw new InvalidOperationException("the open table has no columns: add its time column first");

            _columnCountOffset = -1;
        }

        private void CheckPayloadGrowth(long bytes)
        {
            if (DecompressedLength + bytes > _limits.MaxDecompressedBytes)
                throw new ArgumentOutOfRangeException(nameof(bytes),
                    $"the payload would exceed {_limits.MaxDecompressedBytes} bytes; send the block and start a new one");
        }

        private static Span<byte> Grow(ref byte[] buffer, ref int length, int count)
        {
            int needed = length + count;
            if (needed > buffer.Length)
            {
                int size = Math.Max(needed, (int)Math.Min(int.MaxValue, buffer.Length * 2L));
                Array.Resize(ref buffer, size);
            }

            var span = buffer.AsSpan(length, count);
            length = needed;
            return span;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(TableBlockWriter));
        }


        private struct TableInfo
        {
            public readonly int IdOffset, IdLength, DataOffset, Rows, FirstColumn;
            public int Columns;

            public TableInfo(int idOffset, int idLength, int dataOffset, int rows, int firstColumn)
            {
                IdOffset = idOffset;
                IdLength = idLength;
                DataOffset = dataOffset;
                Rows = rows;
                FirstColumn = firstColumn;
                Columns = 0;
            }
        }

        /// <summary>
        /// Finds a repeated identifier by sorting the table indexes by identifier bytes (ordinal) and comparing
        /// neighbours: O(n log n) comparisons whatever the identifiers are. The index buffer is kept between blocks.
        /// </summary>
        private sealed class IdOrder : IComparer<int>
        {
            private readonly TableBlockWriter _writer;
            private int[] _order = new int[64];

            public IdOrder(TableBlockWriter writer) => _writer = writer;

            /// <summary>The smaller index of two tables with the same identifier, or -1.</summary>
            public int FindDuplicate(int count)
            {
                if (count < 2)
                    return -1;

                if (_order.Length < count)
                    _order = new int[Math.Max(count, _order.Length * 2)];
                for (int i = 0; i < count; i++)
                    _order[i] = i;

                Array.Sort(_order, 0, count, this);

                for (int i = 1; i < count; i++)
                    if (Compare(_order[i - 1], _order[i]) == 0)
                        return Math.Min(_order[i - 1], _order[i]);

                return -1;
            }

            public int Compare(int x, int y)
            {
                var a = _writer._tables[x];
                var b = _writer._tables[y];
                var counter = _writer.IdComparisonCounter;
                if (counter != null)
                    counter.Count++;
                return _writer._directory.AsSpan(a.IdOffset, a.IdLength).SequenceCompareTo(_writer._directory.AsSpan(b.IdOffset, b.IdLength));
            }
        }
    }
}
