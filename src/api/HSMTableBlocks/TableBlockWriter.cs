using System;
using System.Buffers;
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

        /// <summary>The length the payload would have now, before compression: the directory and every column added.</summary>
        public int DecompressedLength => _directoryLength + _dataLength;

        /// <summary>The zstd level of this writer.</summary>
        public int CompressionLevel { get; }

        /// <summary>The limits this writer keeps to.</summary>
        public TableBlockLimits Limits => _limits;


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
            _tables[_tableCount] = new TableInfo(start + 2, tableId.Length, _dataLength, rowCount);

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
        public void AddTimeColumn(ReadOnlySpan<long> ticks)
        {
            CheckColumn(ticks.Length, TableColumnType.Time);

            long min = long.MaxValue, max = long.MinValue;
            foreach (long t in ticks)
            {
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

            AppendColumn(TableColumnType.Time, ticks);
        }

        /// <summary>Adds a 64-bit integer column.</summary>
        public void AddInt64Column(ReadOnlySpan<long> values)
        {
            CheckColumn(values.Length, TableColumnType.Int64);
            AppendColumn(TableColumnType.Int64, values);
        }

        /// <summary>Adds a double column; every value is stored bit for bit.</summary>
        public void AddDoubleColumn(ReadOnlySpan<double> values)
        {
            CheckColumn(values.Length, TableColumnType.Double);
            AppendColumn(TableColumnType.Double, MemoryMarshal.Cast<double, long>(values));
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

            int payloadLength = DecompressedLength;
            var pool = ArrayPool<byte>.Shared;
            byte[] payload = pool.Rent(payloadLength);
            byte[] compressed = pool.Rent(Compressor.GetCompressBound(payloadLength));

            try
            {
                _directory.AsSpan(0, _directoryLength).CopyTo(payload);
                LayOutColumnMajor(payload.AsSpan(_directoryLength, _dataLength));

                int written = _compressor.Wrap(payload.AsSpan(0, payloadLength), compressed);

                var block = new byte[TableBlockFormat.HeaderSize + written];
                var header = block.AsSpan(0, TableBlockFormat.HeaderSize);
                TableBlockFormat.WriteHeader(header, _rowCount, _tableCount, _firstTime, _lastTime, written, payloadLength);
                compressed.AsSpan(0, written).CopyTo(block.AsSpan(TableBlockFormat.HeaderSize));
                BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(TableBlockFormat.ChecksumOffset),
                    TableBlockFormat.Checksum(header, block.AsSpan(TableBlockFormat.HeaderSize)));

                return block;
            }
            finally
            {
                pool.Return(payload);
                pool.Return(compressed);
            }
        }

        /// <summary>Forgets every table and keeps the buffers, ready for the next block.</summary>
        public void Reset()
        {
            ThrowIfDisposed();
            _directoryLength = 0;
            _dataLength = 0;
            _tableCount = 0;
            _rowCount = 0;
            _firstTime = long.MaxValue;
            _lastTime = long.MinValue;
            _columnCountOffset = -1;
            _openRows = 0;
            _openColumns = 0;
            _maxColumns = 0;
        }

        /// <summary>Releases the zstd context.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _compressor.Dispose();
        }


        private void CheckColumn(int length, TableColumnType type)
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

            // The table's column type byte and its values (the first column was counted by BeginTable).
            if (_openColumns > 0)
                CheckPayloadGrowth(1L + (long)length * TableBlockFormat.ValueSize);
        }

        private void AppendColumn(TableColumnType type, ReadOnlySpan<long> values)
        {
            Grow(ref _directory, ref _directoryLength, 1)[0] = (byte)type;
            TableBlockFormat.WriteValues(values, Grow(ref _data, ref _dataLength, values.Length * TableBlockFormat.ValueSize));
            _openColumns++;
            _directory[_columnCountOffset] = (byte)_openColumns;
            _tables[_tableCount - 1].Columns = _openColumns;
            if (_openColumns > _maxColumns)
                _maxColumns = _openColumns;
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
            public readonly int IdOffset, IdLength, DataOffset, Rows;
            public int Columns;

            public TableInfo(int idOffset, int idLength, int dataOffset, int rows)
            {
                IdOffset = idOffset;
                IdLength = idLength;
                DataOffset = dataOffset;
                Rows = rows;
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
                return _writer._directory.AsSpan(a.IdOffset, a.IdLength).SequenceCompareTo(_writer._directory.AsSpan(b.IdOffset, b.IdLength));
            }
        }
    }
}
