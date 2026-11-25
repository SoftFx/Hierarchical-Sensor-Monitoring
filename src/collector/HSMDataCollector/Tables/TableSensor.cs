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

            _active = new TableRowBuffer(doubleColumns, () => BeforeBufferGrowthForTests);
            _spare = new TableRowBuffer(doubleColumns, () => BeforeBufferGrowthForTests);
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

            return Append(ToUtcTicks(time), default, cells, 1) == 1;
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

            return Append(0, times, cells, rows);
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

        // One call of AddRow or AddRows, done against the one run it read first: the reservation, the checks and the
        // append all belong to that run, so a reservation never crosses runs. Returns the rows taken: all or none.
        // Never throws: a buffer that cannot grow leaves the rows dropped and counted.
        private int Append(long ticks, ReadOnlySpan<DateTime> times, ReadOnlySpan<TableCell> cells, int rows)
        {
            var run = Volatile.Read(ref _run);
            var reason = WhyRefused(run);

            if (reason != TableDropReason.None)
            {
                Drop(rows, reason, run);
                return 0;
            }

            var bytes = (long)rows * RowBytes;

            if (!run.TryReserve(bytes))
            {
                Drop(rows, TableDropReason.BufferFull, run);
                return 0;
            }

            BeforeAppendForTests?.Invoke();

            lock (_lock)
            {
                // Checked again under the lock that a refusal, a discard, a take and a new run take: a row reserved
                // while the sensor was writable is not appended once it is not, nor into a run that is not its own.
                reason = ReferenceEquals(_run, run) ? WhyRefused(run) : TableDropReason.NotRunning;

                if (reason == TableDropReason.None)
                {
                    try
                    {
                        if (times.IsEmpty)
                            _active.Append(ticks, cells);
                        else
                            _active.Append(times, cells, rows);
                    }
                    catch (Exception)
                    {
                        // Out of memory while the buffer grew: the buffer is as it was before the call (it builds
                        // every new array before it swaps them in), so the rows are dropped and counted.
                        reason = TableDropReason.BufferGrowthFailed;
                    }
                }
            }

            if (reason != TableDropReason.None)
            {
                run.Release(bytes);
                Drop(rows, reason, run);
                return 0;
            }

            Interlocked.Add(ref _rowsAccepted, rows);
            OnAcceptedForTests?.Invoke(run);
            return rows;
        }

        // Why rows are not taken now into the run given, or None.
        private TableDropReason WhyRefused(TableRun run)
        {
            switch (State)
            {
                case TableSensorState.Refused: return TableDropReason.SensorRefused;
                case TableSensorState.Unavailable: return TableDropReason.SensorUnavailable;
            }

            if (run == null || !run.AcceptsRows || !_pipeline.CanAcceptData)
                return TableDropReason.NotRunning;

            return TableDropReason.None;
        }


        /// <summary>For tests: runs on the writer's thread once a call was accepted, with the run that accepted it.</summary>
        internal Action<TableRun> OnAcceptedForTests { get; set; }

        /// <summary>For tests: runs between the reservation of a row and its append under the sensor's lock.</summary>
        internal Action BeforeAppendForTests { get; set; }

        /// <summary>For tests: runs before a buffer of this sensor allocates the arrays of a growth (a test makes it throw).</summary>
        internal Action BeforeBufferGrowthForTests { get; set; }

        /// <summary>The run this sensor belongs to now; null before the first Start.</summary>
        internal TableRun Run => Volatile.Read(ref _run);

        // The current run; written under _lock only.
        private TableRun _run;

        /// <summary>
        /// Makes <paramref name="run"/> the sensor's run: from now on its rows are that run's. A sensor that was
        /// registered in the previous run is registered again. Rows of the previous run still buffered (none, after a
        /// stop) are dropped and counted in that run.
        /// </summary>
        internal void Attach(TableRun run)
        {
            TableRun previous;
            var leftover = 0;

            lock (_lock)
            {
                previous = _run;
                _run = run;

                if (_state == (int)TableSensorState.Registered)
                {
                    Volatile.Write(ref _stateReason, null);
                    Volatile.Write(ref _state, (int)TableSensorState.Pending);
                }

                if (_active.Count > 0)
                {
                    leftover = _active.Count;
                    _active.Clear();
                }
            }

            if (leftover > 0 && previous != null)
            {
                previous.Release((long)leftover * RowBytes);
                Drop(leftover, TableDropReason.NotDeliveredAtStop, previous);
            }
        }

        /// <summary>
        /// Counts lost rows on the sensor and in the ledger of <paramref name="run"/>; with no run (rows added before the
        /// first Start) or after the run's stop line was written, on the sensor only.
        /// </summary>
        internal void Drop(long rows, TableDropReason reason, TableRun run)
        {
            if (rows <= 0)
                return;

            Interlocked.Add(ref _rowsDropped, rows);
            run?.Ledger.Add(rows, reason);
        }

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
        /// <see cref="TryMove(TableSensorState, TableSensorState, string)"/> for an outcome of <paramref name="run"/> (a
        /// registration answer, an answer to a block): false, and nothing changes, when the sensor belongs to another run.
        /// </summary>
        internal bool TryMove(TableRun run, TableSensorState from, TableSensorState to, string reason = null)
        {
            lock (_lock)
            {
                if (!ReferenceEquals(_run, run) || _state != (int)from)
                    return false;

                Volatile.Write(ref _stateReason, reason);
                Volatile.Write(ref _state, (int)to);
                return true;
            }
        }

        /// <summary>
        /// Takes the rows added since the last take in <paramref name="run"/>; the caller packs them and gives the buffer
        /// back with <see cref="ReturnRows"/>. Null when there are none, or when the sensor belongs to another run: a tick
        /// of an old run never takes rows of a new one.
        /// </summary>
        internal TableRowBuffer TakeRows(TableRun run)
        {
            lock (_lock)
            {
                if (!ReferenceEquals(_run, run) || _active.Count == 0)
                    return null;

                var taken = _active;
                // The spare is away while a pack holds it (a discard can come from the delivery loop meanwhile).
                _active = _spare ?? new TableRowBuffer(_doubleColumns, () => BeforeBufferGrowthForTests);
                _spare = null;

                return taken;
            }
        }

        /// <summary>An empty buffer of this sensor's shape, for rows packed again from a block.</summary>
        internal TableRowBuffer NewBuffer() => new TableRowBuffer(_doubleColumns, () => BeforeBufferGrowthForTests);

        /// <summary>For tests: <see cref="TakeRows(TableRun)"/> in the sensor's current run.</summary>
        internal TableRowBuffer TakeRows() => TakeRows(Run);

        /// <summary>Gives a buffer taken with <see cref="TakeRows(TableRun)"/> back, empty, to be the next spare.</summary>
        internal void ReturnRows(TableRowBuffer buffer)
        {
            buffer.Clear();

            lock (_lock)
                if (_spare == null)
                    _spare = buffer;
        }

        /// <summary>
        /// Drops every buffered row of <paramref name="run"/>, counted under <paramref name="reason"/>, and returns how
        /// many there were. Their room in the run's buffer bound goes back.
        /// </summary>
        internal int DiscardRows(TableRun run, TableDropReason reason)
        {
            var taken = TakeRows(run);

            if (taken == null)
                return 0;

            var rows = taken.Count;

            ReturnRows(taken);
            run.Release((long)rows * RowBytes);
            Drop(rows, reason, run);

            return rows;
        }

        /// <summary>For tests: <see cref="DiscardRows(TableRun, TableDropReason)"/> in the current run.</summary>
        internal int DiscardRows(TableDropReason reason)
        {
            var run = Run;
            return run == null ? 0 : DiscardRows(run, reason);
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
        BufferGrowthFailed,
    }


    internal static class TableBlockFormatSizes
    {
        /// <summary>Every column type of block format version 1 is 8 bytes wide.</summary>
        internal const int ValueSize = 8;

        /// <summary>The frame header of the table connection in front of a block (wire contract, "Table connection").</summary>
        internal const int FrameHeaderSize = 40;
    }
}
