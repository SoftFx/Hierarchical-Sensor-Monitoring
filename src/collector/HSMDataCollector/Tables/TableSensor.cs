using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects.SensorRequests;
using HSMTableBlocks;
using ApiColumnType = HSMSensorDataObjects.SensorRequests.TableColumnType;

namespace HSMDataCollector.Tables
{
    /// <summary>
    /// One table sensor: its schema, its state with the server, its counters and the rows added since the last tick.
    /// Writers append under the sensor's lock; a tick swaps the buffer for the spare one under the same lock, so a
    /// writer never waits for a block to be packed.
    /// </summary>
    internal sealed class TableSensor : ITableSensor
    {
        private readonly object _lock = new object();
        private readonly TablePipeline _pipeline;
        private readonly TableColumn[] _columns;
        private readonly HSMTableBlocks.TableColumnType[] _types;

        // The buffer writers append to, and the one the last tick gave back; swapped by TakeRows.
        private TableRowBuffer _active;
        private TableRowBuffer _spare;

        // Which value columns hold doubles; a new buffer needs it.
        private readonly bool[] _doubleColumns;

        private int _state;
        private string _stateReason;
        private long _rowsAccepted;
        private long _rowsDropped;

        // The run whose registration request carries this sensor; 0 when none does.
        private long _registering;


        internal TableSensor(TablePipeline pipeline, string path, IReadOnlyList<TableColumn> columns, TableSchema schema,
                             string description, TimeSpan? keepHistory)
        {
            _pipeline = pipeline;
            Path = path;
            PathBytes = Encoding.UTF8.GetBytes(path);
            _columns = new TableColumn[columns.Count];
            _types = new HSMTableBlocks.TableColumnType[columns.Count];

            for (var i = 0; i < _columns.Length; i++)
            {
                _columns[i] = columns[i];
                _types[i] = columns[i].Type == ApiColumnType.Int64 ? HSMTableBlocks.TableColumnType.Int64 : HSMTableBlocks.TableColumnType.Double;
            }

            Schema = schema;
            Description = description;
            KeepHistory = keepHistory;
            RowBytes = TableBlockFormatSizes.ValueSize * (1 + _columns.Length);

            var doubleColumns = _doubleColumns = _types.Select(type => type == HSMTableBlocks.TableColumnType.Double).ToArray();

            _active = new TableRowBuffer(doubleColumns);
            _spare = new TableRowBuffer(doubleColumns);
        }


        public string Path { get; }

        public IReadOnlyList<TableColumn> Columns => _columns;

        public TableSensorState State => (TableSensorState)Volatile.Read(ref _state);

        public string StateReason => Volatile.Read(ref _stateReason);

        public long RowsAccepted => Interlocked.Read(ref _rowsAccepted);

        public long RowsDropped => Interlocked.Read(ref _rowsDropped);


        internal byte[] PathBytes { get; }

        internal TableSchema Schema { get; }

        internal string Description { get; }

        internal TimeSpan? KeepHistory { get; }

        /// <summary>The bytes one buffered row counts against the collector's buffer bound.</summary>
        internal int RowBytes { get; }

        internal HSMTableBlocks.TableColumnType ColumnType(int column) => _types[column];

        /// <summary>For tests: the rows the arrays of both buffers can hold without growing.</summary>
        internal int RetainedRowCapacity
        {
            get
            {
                lock (_lock)
                    return (_active?.Capacity ?? 0) + (_spare?.Capacity ?? 0);
            }
        }


        public bool AddRow(DateTime time, TableCell first)
        {
            Span<TableCell> cells = stackalloc TableCell[1];
            cells[0] = first;
            return AddRow(time, (ReadOnlySpan<TableCell>)cells);
        }

        public bool AddRow(DateTime time, TableCell first, TableCell second)
        {
            Span<TableCell> cells = stackalloc TableCell[2];
            cells[0] = first;
            cells[1] = second;
            return AddRow(time, (ReadOnlySpan<TableCell>)cells);
        }

        public bool AddRow(DateTime time, TableCell first, TableCell second, TableCell third)
        {
            Span<TableCell> cells = stackalloc TableCell[3];
            cells[0] = first;
            cells[1] = second;
            cells[2] = third;
            return AddRow(time, (ReadOnlySpan<TableCell>)cells);
        }

        public bool AddRow(DateTime time, TableCell first, TableCell second, TableCell third, TableCell fourth)
        {
            Span<TableCell> cells = stackalloc TableCell[4];
            cells[0] = first;
            cells[1] = second;
            cells[2] = third;
            cells[3] = fourth;
            return AddRow(time, (ReadOnlySpan<TableCell>)cells);
        }

        public bool AddRow(DateTime time, TableCell[] cells)
        {
            if (cells == null)
                throw new ArgumentNullException(nameof(cells));

            return AddRow(time, new ReadOnlySpan<TableCell>(cells));
        }

        public bool AddRow(DateTime time, ReadOnlySpan<TableCell> cells)
        {
            CheckRow(cells, 0);

            var ticks = ToUtcTicks(time);

            if (!TryReserve(1))
                return false;

            BeforeAppendForTests?.Invoke();

            lock (_lock)
            {
                // Checked again under the lock that a refusal and a discard take: a row reserved while the
                // sensor was writable is not appended once it is not (#1846 review, items 2 and 3).
                if (!CanAppend(1))
                    return false;

                _active.Append(ticks, cells);
            }

            Interlocked.Increment(ref _rowsAccepted);
            return true;
        }

        public int AddRows(DateTime[] times, TableCell[] cells, int rowCount)
        {
            if (times == null)
                throw new ArgumentNullException(nameof(times));
            if (cells == null)
                throw new ArgumentNullException(nameof(cells));
            if (rowCount < 0 || rowCount > times.Length)
                throw new ArgumentOutOfRangeException(nameof(rowCount), rowCount, $"must be within 0..{times.Length} (the length of {nameof(times)})");

            return AddRows(new ReadOnlySpan<DateTime>(times, 0, rowCount), new ReadOnlySpan<TableCell>(cells));
        }

        public int AddRows(ReadOnlySpan<DateTime> times, ReadOnlySpan<TableCell> cells)
        {
            var rows = times.Length;

            if ((long)rows * _columns.Length > cells.Length)
                throw new ArgumentException($"{rows} row(s) of {_columns.Length} column(s) need {(long)rows * _columns.Length} cells, {cells.Length} were given.", nameof(cells));

            cells = cells.Slice(0, rows * _columns.Length);

            for (var row = 0; row < rows; row++)
                CheckRow(cells.Slice(row * _columns.Length, _columns.Length), row);

            if (rows == 0)
                return 0;

            if (!TryReserve(rows))
                return 0;

            BeforeAppendForTests?.Invoke();

            lock (_lock)
            {
                if (!CanAppend(rows))
                    return 0;

                _active.Append(times, cells, rows);
            }

            Interlocked.Add(ref _rowsAccepted, rows);
            return rows;
        }


        /// <summary>UTC ticks of a row's time: a local time is converted, a time of unspecified kind is taken as UTC.</summary>
        internal static long ToUtcTicks(DateTime time) =>
            time.Kind == DateTimeKind.Local ? time.ToUniversalTime().Ticks : time.Ticks;


        // A row of the wrong shape is a bug of the caller and says so; every other refusal is a dropped row.
        private void CheckRow(ReadOnlySpan<TableCell> cells, int row)
        {
            if (cells.Length != _columns.Length)
                throw new ArgumentException($"A row of {Path} has {_columns.Length} value column(s); {cells.Length} cell(s) were given.", nameof(cells));

            for (var column = 0; column < cells.Length; column++)
                if (cells[column].Kind == TableCell.KindDouble && _types[column] == HSMTableBlocks.TableColumnType.Int64)
                    throw new ArgumentException($"Column '{_columns[column].Name}' of {Path} holds 64-bit integers; row {row} gives it a double.", nameof(cells));
        }

        // Takes room in the collector's buffer for rows that are about to be appended; false (counted) when they are
        // not taken.
        private bool TryReserve(int rows)
        {
            var reason = _pipeline.WhyRowsAreRefused(this);

            if (reason == TableDropReason.None && _pipeline.TryReserveBuffer((long)rows * RowBytes))
                return true;

            if (reason == TableDropReason.None)
                reason = TableDropReason.BufferFull;

            Drop(rows, reason);
            return false;
        }


        /// <summary>For tests: runs between the reservation of a row and its append under the sensor's lock.</summary>
        internal Action BeforeAppendForTests { get; set; }

        // Under _lock: whether reserved rows may still be appended. When not, the reservation goes back and the rows
        // are counted as dropped.
        private bool CanAppend(int rows)
        {
            var reason = _pipeline.WhyRowsAreRefused(this);

            if (reason == TableDropReason.None)
                return true;

            _pipeline.ReleaseBuffer((long)rows * RowBytes);
            Drop(rows, reason);

            return false;
        }

        internal void Drop(long rows, TableDropReason reason)
        {
            if (rows <= 0)
                return;

            Interlocked.Add(ref _rowsDropped, rows);
            _pipeline.CountDropped(rows, reason);
        }

        /// <summary>
        /// Marks the sensor as carried by a registration request of run <paramref name="run"/> (a positive number).
        /// False when a request of the same run carries it already; a request of an earlier run does not hold a new
        /// run back (#1846 review, item 9).
        /// </summary>
        internal bool TryBeginRegistration(long run)
        {
            while (true)
            {
                var current = Interlocked.Read(ref _registering);

                if (current == run)
                    return false;

                if (Interlocked.CompareExchange(ref _registering, run, current) == current)
                    return true;
            }
        }

        /// <summary>Ends the mark of run <paramref name="run"/>; a mark of a later run stays.</summary>
        internal void EndRegistration(long run) => Interlocked.CompareExchange(ref _registering, 0, run);

        /// <summary>Moves the state from <paramref name="from"/> to <paramref name="to"/>; false when it was not <paramref name="from"/>.</summary>
        internal bool TryMove(TableSensorState from, TableSensorState to, string reason = null)
        {
            lock (_lock)
            {
                if (_state != (int)from)
                    return false;

                Volatile.Write(ref _stateReason, reason);
                Volatile.Write(ref _state, (int)to);
                return true;
            }
        }

        /// <summary>
        /// Takes the rows added since the last take; the caller packs them and gives the buffer back with
        /// <see cref="ReturnRows"/>. Null when there are none.
        /// </summary>
        internal TableRowBuffer TakeRows()
        {
            lock (_lock)
            {
                if (_active.Count == 0)
                    return null;

                var taken = _active;
                // The spare is away while a pack holds it (a discard can come from the delivery loop meanwhile).
                _active = _spare ?? new TableRowBuffer(_doubleColumns);
                _spare = null;

                return taken;
            }
        }

        /// <summary>Gives a buffer taken with <see cref="TakeRows"/> back, empty, to be the next spare.</summary>
        internal void ReturnRows(TableRowBuffer buffer)
        {
            buffer.Clear();

            lock (_lock)
                if (_spare == null)
                    _spare = buffer;
        }

        /// <summary>
        /// Drops every buffered row, counted under <paramref name="reason"/>, and returns how many there were. Their
        /// room in the collector's buffer goes back.
        /// </summary>
        internal int DiscardRows(TableDropReason reason)
        {
            var taken = TakeRows();

            if (taken == null)
                return 0;

            var rows = taken.Count;

            ReturnRows(taken);
            _pipeline.ReleaseBuffer((long)rows * RowBytes);
            Drop(rows, reason);

            return rows;
        }

        internal AddOrUpdateSensorRequest BuildRegistration()
        {
            var columns = new List<TableColumnRequest>(_columns.Length + 1)
            {
                new TableColumnRequest { Name = TableColumn.TimeColumnName, Type = ApiColumnType.Time },
            };

            foreach (var column in _columns)
                columns.Add(new TableColumnRequest { Name = column.Name, Type = column.Type });

            return new AddOrUpdateSensorRequest
            {
                Path = Path,
                SensorType = HSMSensorDataObjects.SensorType.TableSensor,
                Description = Description,
                KeepHistory = KeepHistory?.Ticks,
                TableColumns = columns,
            };
        }
    }


    /// <summary>Why rows were lost; each reason is reported in words of its own.</summary>
    internal enum TableDropReason
    {
        None = 0,
        NotRunning,
        BufferFull,
        SensorRefused,
        SensorUnavailable,
        BacklogFull,
        RefusedByServer,
        NotDeliveredAtStop,
        RowTooLarge,
        PackingFailed,
    }


    internal static class TableBlockFormatSizes
    {
        /// <summary>Every column type of block format version 1 is 8 bytes wide.</summary>
        internal const int ValueSize = 8;

        /// <summary>The frame header of the table connection in front of a block (wire contract, "Table connection").</summary>
        internal const int FrameHeaderSize = 40;
    }
}
