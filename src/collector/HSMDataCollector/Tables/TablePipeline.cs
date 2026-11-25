using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.Options;
using HSMDataCollector.Prototypes;
using HSMDataCollector.PublicInterface;
using HSMDataCollector.Threading;
using HSMTableBlocks;

namespace HSMDataCollector.Tables
{
    /// <summary>What the table pipeline needs from its collector: the data gate and the two report channels.</summary>
    internal interface ITableDiagnostics
    {
        /// <summary>True while the collector takes data (Starting, Running, Stopping).</summary>
        bool CanAcceptData { get; }

        /// <summary>The collector's deduplicated error channel (log and <c>Collector errors</c>).</summary>
        void AddError(string message);

        /// <summary>Lost rows, counted in the <c>Queue overflow</c> sensor under the queue "Table rows".</summary>
        void ReportDroppedRows(long rows);
    }


    /// <summary>
    /// The table sensors of one collector (ADR-0021 points 5–7): it registers them, takes the rows of all of them at
    /// every tick into blocks (one block per tick unless the limits split it), keeps the blocks in a bounded backlog and
    /// delivers them one at a time, in sequence order, through the sender's <see cref="ITableBlockChannel"/>.
    /// <para>
    /// Threads: rows are added by the application on any thread; the tick runs on the collector's scheduler and only
    /// packs; delivery runs in a loop of its own, so a slow server never delays packing. Nothing here throws into the
    /// host: every loop catches and reports.
    /// </para>
    /// </summary>
    internal sealed class TablePipeline : IDisposable
    {
        /// <summary>The queue name under which the <c>Queue overflow</c> sensor counts lost table rows.</summary>
        internal const string DroppedRowsQueueName = "Table rows";

        private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(5);
        private static readonly int ReasonCount = Enum.GetValues(typeof(TableDropReason)).Length;

        private readonly ConcurrentDictionary<string, TableSensor> _sensors = new ConcurrentDictionary<string, TableSensor>(StringComparer.Ordinal);
        private readonly object _createLock = new object();
        private readonly object _backlogLock = new object();
        private readonly LinkedList<PendingBlock> _backlog = new LinkedList<PendingBlock>();
        private readonly SemaphoreSlim _packGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _deliveryGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _deliverySignal = new SemaphoreSlim(0, int.MaxValue);
        private readonly long[] _unreportedDrops = new long[ReasonCount];
        private readonly object _runLock = new object();

        private readonly ITableBlockChannel _channel;
        private readonly string _unavailableReason;
        private readonly ITableDiagnostics _diagnostics;
        private readonly ICollectorScheduler _scheduler;
        private readonly ICollectorLogger _logger;
        private readonly TimeSpan _tickPeriod;
        private readonly long _maxBufferBytes;
        private readonly long _maxBacklogBytes;
        private readonly bool _manualTick;

        private TableBlockWriter _writer;
        private long _bufferedBytes;
        private long _backlogBytes;
        private long _droppedTotal;
        private PendingBlock _inFlight;
        private long _retryNotBeforeTicks;

        // One run per Start.
        private CancellationTokenSource _run;
        private ScheduledTask _tick;
        private Task _deliveryLoop;
        private Guid _producer;
        private long _nextSequence;
        private int _disposed;


        internal TablePipeline(CollectorOptions options, ITableBlockChannel channel, string unavailableReason,
                               ITableDiagnostics diagnostics, ICollectorScheduler scheduler, ICollectorLogger logger)
        {
            _channel = unavailableReason == null ? channel : null;
            _unavailableReason = unavailableReason ?? (channel == null ? "table sensors need the collector session transport" : channel.TablesUnavailableReason);

            if (_unavailableReason != null)
                _channel = null;

            _diagnostics = diagnostics;
            _scheduler = scheduler;
            _logger = logger;
            _tickPeriod = options.TableTickPeriod;
            _maxBufferBytes = options.MaxTableBufferBytes;
            _maxBacklogBytes = options.MaxTableBacklogBytes;
            _manualTick = options.ManualTableTick;
        }


        /// <summary>Why this collector cannot send tables, or null when it can.</summary>
        internal string UnavailableReason => _unavailableReason;

        internal IReadOnlyCollection<TableSensor> Sensors => _sensors.Values.ToList();

        internal long BufferedBytes => Interlocked.Read(ref _bufferedBytes);

        internal long BacklogBytes => Interlocked.Read(ref _backlogBytes);

        internal long DroppedRows => Interlocked.Read(ref _droppedTotal);

        internal int BacklogBlocks
        {
            get
            {
                lock (_backlogLock)
                    return _backlog.Count;
            }
        }

        /// <summary>The producer id of the current run; a new one at every Start.</summary>
        internal Guid Producer => _producer;


        // ---- Creation -------------------------------------------------------------------------------------------

        internal ITableSensor Create(string path, TableSensorOptions options, string computerName, string module, bool canRegister)
        {
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            var full = options.SensorLocation == SensorLocation.Product
                ? path
                : DefaultPrototype.BuildPath(computerName, module, path);

            var normalized = TablePath.Normalize(full);
            var limits = _channel?.BlockLimits ?? TableBlockLimits.Default;

            TablePath.Check(normalized, path, limits.MaxTableIdLength);

            var schema = BuildSchema(options.Columns, limits);

            lock (_createLock)
            {
                if (_sensors.TryGetValue(normalized, out var existing))
                {
                    if (!existing.Schema.SameAs(schema))
                        throw new ArgumentException($"The table sensor {normalized} was created with other columns ({string.Join(", ", existing.Columns)}); another shape needs another path.", nameof(options));

                    return existing;
                }

                var sensor = new TableSensor(this, normalized, options.Columns, schema, options.Description, options.KeepHistory);

                if (_unavailableReason != null)
                {
                    sensor.TryMove(TableSensorState.Pending, TableSensorState.Unavailable, _unavailableReason);
                    _diagnostics.AddError($"The table sensor {normalized} cannot send its rows: {_unavailableReason}. Its rows are dropped and counted.");
                }
                else if (!canRegister)
                {
                    const string stopping = "the collector is stopping or disposed";
                    sensor.TryMove(TableSensorState.Pending, TableSensorState.Unavailable, stopping);
                    _diagnostics.AddError($"The table sensor {normalized} was created while {stopping}; its rows are dropped and counted.");
                }

                _sensors[normalized] = sensor;

                if (sensor.State == TableSensorState.Pending && IsRunning && !_manualTick)
                    KickRegistration();

                return sensor;
            }
        }

        private static TableSchema BuildSchema(IReadOnlyList<TableColumn> columns, TableBlockLimits limits)
        {
            if (columns.Count == 0)
                throw new ArgumentException("A table sensor needs at least one value column besides the time.", "options");

            var schemaColumns = new List<TableSchemaColumn>(columns.Count + 1)
            {
                new TableSchemaColumn(TableColumn.TimeColumnName, HSMTableBlocks.TableColumnType.Time),
            };

            foreach (var column in columns)
            {
                if (string.Equals(column.Name, TableColumn.TimeColumnName, StringComparison.Ordinal))
                    throw new ArgumentException($"The column name '{TableColumn.TimeColumnName}' belongs to the time of the row, which every table sensor has first.", "options");

                var type = column.Type == HSMSensorDataObjects.SensorRequests.TableColumnType.Int64 ? HSMTableBlocks.TableColumnType.Int64
                         : column.Type == HSMSensorDataObjects.SensorRequests.TableColumnType.Double ? HSMTableBlocks.TableColumnType.Double
                         : throw new ArgumentException($"Column '{column.Name}' has the type {column.Type}; a value column is Double or Int64.", "options");

                schemaColumns.Add(new TableSchemaColumn(column.Name, type));
            }

            try
            {
                return new TableSchema(schemaColumns, limits);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"The columns of the table sensor are invalid: {ex.Message}.", "options", ex);
            }
        }


        // ---- Rows and the buffer bound ---------------------------------------------------------------------------

        internal TableDropReason WhyRowsAreRefused(TableSensor sensor)
        {
            switch (sensor.State)
            {
                case TableSensorState.Refused: return TableDropReason.SensorRefused;
                case TableSensorState.Unavailable: return TableDropReason.SensorUnavailable;
            }

            // Rows are taken from Start until the stop's final tick takes them, not while the collector is still
            // Stopping after it: such a row would be in no report, or go out in the next run (#1846 review, item 2).
            if (Volatile.Read(ref _acceptingRows) == 0)
                return TableDropReason.NotRunning;

            return _diagnostics.CanAcceptData ? TableDropReason.None : TableDropReason.NotRunning;
        }

        // 1 from Start until the stop or the rollback closes the run for rows.
        private int _acceptingRows;

        /// <summary>
        /// Takes <paramref name="bytes"/> of the collector-wide bound, or nothing. A compare-and-swap that never
        /// overshoots: a request that cannot fit is refused before it touches the counter, so it never makes a
        /// concurrent request that fits look like one that does not (#1846 review, item 10).
        /// </summary>
        internal bool TryReserveBuffer(long bytes)
        {
            if (bytes > _maxBufferBytes)
            {
                OnReservationForTests?.Invoke();
                return false;
            }

            while (true)
            {
                var current = Interlocked.Read(ref _bufferedBytes);

                if (current + bytes > _maxBufferBytes)
                {
                    OnReservationForTests?.Invoke();
                    return false;
                }

                if (Interlocked.CompareExchange(ref _bufferedBytes, current + bytes, current) == current)
                {
                    OnReservationForTests?.Invoke();
                    return true;
                }
            }
        }

        /// <summary>For tests: runs once a reservation has decided, whatever it decided.</summary>
        internal Action OnReservationForTests { get; set; }

        internal void ReleaseBuffer(long bytes) => Interlocked.Add(ref _bufferedBytes, -bytes);

        internal void CountDropped(long rows, TableDropReason reason)
        {
            Interlocked.Add(ref _droppedTotal, rows);
            Interlocked.Add(ref _unreportedDrops[(int)reason], rows);
        }

        /// <summary>
        /// Reports the rows lost since the last report, one deduplicated line per reason and the count to the
        /// <c>Queue overflow</c> sensor. Run at every tick and at stop, so a hot path of dropped rows costs a counter only.
        /// </summary>
        internal void ReportDrops()
        {
            long total = 0;

            for (var reason = 1; reason < ReasonCount; reason++)
            {
                var rows = Interlocked.Exchange(ref _unreportedDrops[reason], 0);

                if (rows <= 0)
                    continue;

                total += rows;
                _diagnostics.AddError(DescribeDrop((TableDropReason)reason));
            }

            if (total > 0)
                _diagnostics.ReportDroppedRows(total);
        }

        private string DescribeDrop(TableDropReason reason)
        {
            switch (reason)
            {
                case TableDropReason.NotRunning: return "Table rows were dropped: they were added while the collector was not running.";
                case TableDropReason.BufferFull: return $"Table rows were dropped: the table row buffer of {_maxBufferBytes} bytes was full (CollectorOptions.MaxTableBufferBytes).";
                case TableDropReason.SensorRefused: return "Table rows were dropped: they were added to a table sensor the server refused.";
                case TableDropReason.SensorUnavailable: return "Table rows were dropped: they were added to a table sensor this collector cannot send (see its StateReason).";
                case TableDropReason.BacklogFull: return $"Table rows were dropped: the undelivered table blocks passed {_maxBacklogBytes} bytes (CollectorOptions.MaxTableBacklogBytes) and the oldest went.";
                case TableDropReason.RefusedByServer: return "Table rows were dropped: the server refused them in a table block.";
                case TableDropReason.NotDeliveredAtStop: return "Table rows were dropped: they were not delivered when the collector stopped.";
                case TableDropReason.RowTooLarge: return "Table rows were dropped: a row did not fit a frame of the table connection.";
                case TableDropReason.PackingFailed: return "Table rows were dropped: packing them into a block failed.";
                default: return $"Table rows were dropped ({reason}).";
            }
        }


        // ---- Lifecycle --------------------------------------------------------------------------------------------

        private bool IsRunning
        {
            get
            {
                lock (_runLock)
                    return _run != null;
            }
        }

        /// <summary>Starts a run: a new producer, every sensor registered again, the tick and the delivery loop.</summary>
        internal void Start()
        {
            lock (_runLock)
            {
                if (_run != null || Volatile.Read(ref _disposed) == 1)
                    return;

                _producer = Guid.NewGuid();
                _nextSequence = 0;
                Interlocked.Exchange(ref _retryNotBeforeTicks, 0);
                Interlocked.Increment(ref _runGeneration);
                Volatile.Write(ref _acceptingRows, 1);

                foreach (var sensor in _sensors.Values)
                    sensor.TryMove(TableSensorState.Registered, TableSensorState.Pending);

                if (_writer == null)
                    _writer = new TableBlockWriter();

                _run = new CancellationTokenSource();

                if (_manualTick)
                    return;

                _tick = _scheduler.Schedule((Func<Task>)TickAsync, _tickPeriod, _tickPeriod, ex => ReportFault("The table tick", ex));

                if (_channel != null)
                {
                    var token = _run.Token;
                    _deliveryLoop = Task.Run(() => DeliveryLoopAsync(token));
                }

                KickRegistration();
            }
        }

        /// <summary>
        /// The stop drain of the tables: the rows still buffered are packed and every block is offered to the server
        /// until <paramref name="drainBudget"/> runs out or a block is not delivered. What is left is dropped, counted
        /// and reported. Bounded by the budget; does not throw.
        /// </summary>
        internal async Task FlushAsync(CancellationToken drainBudget)
        {
            if (!await EndRunAsync().ConfigureAwait(false))
                return;

            try
            {
                // The final tick: what it takes is the last of this run. Rows added after it are refused and
                // counted until the next Start (#1846 review, item 2); a writer that reserved before is checked
                // again under its sensor's lock, and what lands after the pack is discarded and counted below.
                Volatile.Write(ref _acceptingRows, 0);

                // Observes the budget between passes: what is not packed in time is dropped and counted (item 4).
                await PackAsync(drainBudget, TableDropReason.NotDeliveredAtStop).ConfigureAwait(false);

                if (_channel != null && !drainBudget.IsCancellationRequested)
                    await DeliverAsync(drainBudget, atStop: true).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The budget is spent: what is left is counted below.
            }
            catch (Exception ex)
            {
                ReportFault("The table stop drain", ex);
            }

            DropEverything(TableDropReason.NotDeliveredAtStop, report: true);
        }

        /// <summary>A stop that does not drain (the rollback of a failed start): everything held is dropped and counted.</summary>
        internal async Task StopWithoutFlushAsync()
        {
            Volatile.Write(ref _acceptingRows, 0);

            if (!await EndRunAsync().ConfigureAwait(false))
                return;

            DropEverything(TableDropReason.NotDeliveredAtStop, report: true);
        }

        // Ends the run: the tick, the delivery loop and the registrations in flight. False when no run was going.
        private async Task<bool> EndRunAsync()
        {
            CancellationTokenSource run;
            ScheduledTask tick;
            Task loop;

            lock (_runLock)
            {
                run = _run;
                tick = _tick;
                loop = _deliveryLoop;

                _run = null;
                _tick = null;
                _deliveryLoop = null;
            }

            if (run == null)
                return false;

            try { run.Cancel(); }
            catch (ObjectDisposedException) { }

            try
            {
                if (tick != null)
                    await tick.StopAsync(waitForCurrentRun: true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ReportFault("Stopping the table tick", ex);
            }

            if (loop != null)
                await Task.WhenAny(loop, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);

            // A tick of this run that is packing (a scheduled one, or one a test drives) finishes before the stop goes
            // on: it observes the cancelled run between passes, so this is short, and a block it packs can never be
            // enqueued after the stop cleared the backlog, to go out under the next run's producer (item 5).
            OnStopWaitsForPackForTests?.Invoke();

            await _packGate.WaitAsync().ConfigureAwait(false);
            _packGate.Release();

            run.Dispose();
            return true;
        }

        private void DropEverything(TableDropReason reason, bool report)
        {
            long rows = 0;
            var blocks = 0;

            lock (_backlogLock)
            {
                foreach (var block in _backlog)
                {
                    rows += block.Rows;
                    blocks++;
                    block.DropRows(reason);
                }

                _backlog.Clear();
                _inFlight = null;
                Interlocked.Exchange(ref _backlogBytes, 0);
            }

            foreach (var sensor in _sensors.Values)
                rows += sensor.DiscardRows(reason);

            if (report)
            {
                if (rows > 0)
                    _logger?.Error($"Collector stop: {rows} table row(s){(blocks > 0 ? $" ({blocks} block(s) among them)" : string.Empty)} were not delivered and are dropped.");

                ReportDrops();
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            try { EndRunAsync().ConfigureAwait(false).GetAwaiter().GetResult(); }
            catch (Exception) { }

            DropEverything(TableDropReason.NotDeliveredAtStop, report: false);

            _writer?.Dispose();
        }


        // ---- Registration ------------------------------------------------------------------------------------------

        private void KickRegistration()
        {
            var channel = _channel;
            CancellationToken token;
            long run;

            lock (_runLock)
            {
                if (channel == null || _run == null)
                    return;

                token = _run.Token;
                run = Interlocked.Read(ref _runGeneration);
            }

            Observe(Task.Run(async () =>
            {
                await channel.WaitUntilReadyAsync(token).ConfigureAwait(false);
                await RegisterPendingAsync(token, run).ConfigureAwait(false);
            }));
        }

        // The number of the current run, rising at every Start: an answer of an earlier run changes nothing.
        private long _runGeneration;

        /// <summary>For tests: runs after a registration request is answered, before the answer is applied.</summary>
        internal Func<Task> OnRegistrationAnsweredForTests { get; set; }

        /// <summary>
        /// Registers every Pending sensor not already in a request, in one request of table registrations. Accepted
        /// sensors are Registered, refused ones Refused for good; the rest stay Pending and go again on the next tick.
        /// </summary>
        internal async Task RegisterPendingAsync(CancellationToken token, long run)
        {
            var channel = _channel;

            if (channel == null)
                return;

            // In path order, so the frames of a large request are cut the same way every time.
            var batch = _sensors.Values.Where(s => s.State == TableSensorState.Pending && s.TryBeginRegistration(run))
                                       .OrderBy(s => s.Path, StringComparer.Ordinal)
                                       .ToList();

            if (batch.Count == 0)
                return;

            try
            {
                TableRegistrationResult result;

                try
                {
                    result = await channel.RegisterTablesAsync(batch.Select(s => s.BuildRegistration()).ToList(), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    result = TableRegistrationResult.NotSent(ex.Message);
                }

                var answered = OnRegistrationAnsweredForTests;

                if (answered != null)
                    await answered().ConfigureAwait(false);

                // An answer to a request of an earlier run: the run that is going now registers its sensors itself
                // (#1846 review, item 9).
                if (run != Interlocked.Read(ref _runGeneration))
                    return;

                var unsettled = 0;

                foreach (var sensor in batch)
                {
                    if (result.Accepted.Contains(sensor.Path))
                    {
                        sensor.TryMove(TableSensorState.Pending, TableSensorState.Registered);
                    }
                    else if (result.Refused.TryGetValue(sensor.Path, out var reason))
                    {
                        if (sensor.TryMove(TableSensorState.Pending, TableSensorState.Refused, reason))
                        {
                            _diagnostics.AddError($"The server refused the table sensor {sensor.Path}: {reason}. It is not registered again; its rows are dropped and counted. Another shape or another place needs another sensor path.");
                            sensor.DiscardRows(TableDropReason.SensorRefused);
                        }
                    }
                    else
                    {
                        unsettled++;
                    }
                }

                if (unsettled > 0)
                    _diagnostics.AddError($"Table sensor registrations were not delivered ({result.UnsettledReason ?? "no answer for them"}); they are sent again on the next tick and their rows are kept.");
            }
            finally
            {
                foreach (var sensor in batch)
                    sensor.EndRegistration(run);
            }
        }


        // ---- The tick and packing ----------------------------------------------------------------------------------

        /// <summary>
        /// One tick: reports lost rows, registers the sensors that wait for it, packs the rows of every registered sensor
        /// into blocks and hands them to delivery. With <see cref="CollectorOptions.ManualTableTick"/> (tests) the
        /// registration and the delivery are awaited here, so the tick is deterministic.
        /// </summary>
        internal async Task TickAsync()
        {
            CancellationToken token;
            long run;

            lock (_runLock)
            {
                if (_run == null)
                    return;

                token = _run.Token;
                run = Interlocked.Read(ref _runGeneration);
            }

            try
            {
                ReportDrops();

                var registration = RegisterPendingAsync(token, run);

                if (_manualTick)
                    await registration.ConfigureAwait(false);
                else
                    Observe(registration);

                await PackAsync(token, TableDropReason.NotDeliveredAtStop).ConfigureAwait(false);

                if (_manualTick)
                    await DeliverAsync(token, atStop: false).ConfigureAwait(false);
                else
                    _deliverySignal.Release();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
        }

        private readonly struct Piece
        {
            internal Piece(TableSensor sensor, TableRowBuffer buffer, int start, int count)
            {
                Sensor = sensor;
                Buffer = buffer;
                Start = start;
                Count = count;
            }

            internal TableSensor Sensor { get; }

            internal TableRowBuffer Buffer { get; }

            internal int Start { get; }

            internal int Count { get; }
        }

        /// <summary>
        /// Takes the rows of every registered sensor and packs them into blocks at the end of the backlog. Observes
        /// <paramref name="token"/> before every compression pass: the rows not packed when it fires are dropped under
        /// <paramref name="cancelledReason"/> and counted (#1846 review, items 4 and 5).
        /// </summary>
        internal async Task PackAsync(CancellationToken token, TableDropReason cancelledReason)
        {
            await _packGate.WaitAsync().ConfigureAwait(false);

            var taken = new List<KeyValuePair<TableSensor, TableRowBuffer>>();

            try
            {
                foreach (var sensor in _sensors.Values)
                {
                    if (sensor.State != TableSensorState.Registered)
                        continue;

                    var rows = sensor.TakeRows();

                    if (rows != null)
                        taken.Add(new KeyValuePair<TableSensor, TableRowBuffer>(sensor, rows));
                }

                if (taken.Count == 0 || _writer == null)
                    return;

                var limits = _channel?.BlockLimits ?? TableBlockLimits.Default;
                var maxBlock = _channel?.MaxBlockBytes ?? int.MaxValue;
                var pieces = new List<Piece>();

                foreach (var entry in taken)
                    Cut(entry.Key, entry.Value, limits, pieces);

                var abandoned = 0L;

                foreach (var group in Group(pieces, limits))
                    abandoned += EncodeAndQueue(group, maxBlock, token, cancelledReason);

                if (abandoned > 0)
                    _logger?.Error($"Packing table rows was cut short ({abandoned} row(s) not packed in time); they are dropped and counted.");
            }
            catch (Exception ex)
            {
                // Packing has no reason to fail; if it does, the rows it took are lost and said so (below).
                ReportFault("Packing table rows", ex);

                foreach (var entry in taken)
                    if (entry.Value.Count > 0)
                        entry.Key.Drop(entry.Value.Count, TableDropReason.PackingFailed);
            }
            finally
            {
                foreach (var entry in taken)
                {
                    ReleaseBuffer((long)entry.Value.Count * entry.Key.RowBytes);
                    entry.Key.ReturnRows(entry.Value);
                }

                _packGate.Release();
            }
        }

        // The rows of one sensor in pieces that each fit the block limits on their own.
        private static void Cut(TableSensor sensor, TableRowBuffer rows, TableBlockLimits limits, List<Piece> pieces)
        {
            var maxRows = (int)Math.Max(1, Math.Min(limits.MaxRows, (limits.MaxDecompressedBytes - TableOverhead(sensor)) / RowEstimate(sensor)));

            for (var start = 0; start < rows.Count; start += maxRows)
                pieces.Add(new Piece(sensor, rows, start, Math.Min(maxRows, rows.Count - start)));
        }

        // Upper bounds of the bytes a table takes in a block before compression: its directory entry, and per row its
        // values and its share of the null maps.
        private static long TableOverhead(TableSensor sensor) => 32L + sensor.PathBytes.Length + sensor.Columns.Count + 1;

        private static long RowEstimate(TableSensor sensor) => (long)TableBlockFormatSizes.ValueSize * (sensor.Columns.Count + 1) + sensor.Columns.Count + 1;

        // Pieces in groups that keep to the limits: tables, rows, decompressed bytes, and a table once per block.
        private static IEnumerable<List<Piece>> Group(List<Piece> pieces, TableBlockLimits limits)
        {
            var group = new List<Piece>();
            long rows = 0, bytes = 0;

            foreach (var piece in pieces)
            {
                var estimate = TableOverhead(piece.Sensor) + piece.Count * RowEstimate(piece.Sensor);

                if (group.Count > 0
                    && (group.Count + 1 > limits.MaxTables
                        || rows + piece.Count > limits.MaxRows
                        || bytes + estimate > limits.MaxDecompressedBytes
                        || ReferenceEquals(group[group.Count - 1].Sensor, piece.Sensor)))
                {
                    yield return group;

                    group = new List<Piece>();
                    rows = 0;
                    bytes = 0;
                }

                group.Add(piece);
                rows += piece.Count;
                bytes += estimate;
            }

            if (group.Count > 0)
                yield return group;
        }

        // One block per group; a block larger than a frame allows is split, by tables first and then by rows.
        // Returns the rows left unpacked because the token fired (dropped and counted here).
        private long EncodeAndQueue(List<Piece> group, int maxBlock, CancellationToken token, TableDropReason cancelledReason)
        {
            if (token.IsCancellationRequested)
            {
                long rows = 0;

                foreach (var cut in group)
                {
                    cut.Sensor.Drop(cut.Count, cancelledReason);
                    rows += cut.Count;
                }

                return rows;
            }

            OnPackPassForTests?.Invoke();

            var block = Encode(group);

            if (block.Length <= maxBlock)
            {
                Enqueue(block, group);
                return 0;
            }

            if (group.Count > 1)
            {
                var half = group.Count / 2;
                return EncodeAndQueue(group.GetRange(0, half), maxBlock, token, cancelledReason)
                     + EncodeAndQueue(group.GetRange(half, group.Count - half), maxBlock, token, cancelledReason);
            }

            var piece = group[0];

            if (piece.Count > 1)
            {
                var first = piece.Count / 2;
                return EncodeAndQueue(new List<Piece> { new Piece(piece.Sensor, piece.Buffer, piece.Start, first) }, maxBlock, token, cancelledReason)
                     + EncodeAndQueue(new List<Piece> { new Piece(piece.Sensor, piece.Buffer, piece.Start + first, piece.Count - first) }, maxBlock, token, cancelledReason);
            }

            piece.Sensor.Drop(1, TableDropReason.RowTooLarge);
            _diagnostics.AddError($"A row of the table sensor {piece.Sensor.Path} does not fit a frame of the table connection ({maxBlock} bytes) and is dropped.");
            return 0;
        }

        /// <summary>For tests: runs when a stop is about to wait for a pack of its run to finish.</summary>
        internal Action OnStopWaitsForPackForTests { get; set; }

        /// <summary>For tests: runs after a block got its answer and before the answer is applied.</summary>
        internal Action OnAckForTests { get; set; }

        /// <summary>For tests: runs before every compression pass of a pack.</summary>
        internal Action OnPackPassForTests { get; set; }

        private byte[] Encode(List<Piece> group)
        {
            var writer = _writer;

            writer.Reset();

            try
            {
                foreach (var piece in group)
                {
                    var sensor = piece.Sensor;
                    var rows = piece.Buffer;

                    writer.BeginTable(sensor.PathBytes, sensor.Schema.Id, piece.Count);
                    writer.AddTimeColumn(rows.Times.Slice(piece.Start, piece.Count));

                    for (var column = 0; column < rows.ColumnCount; column++)
                    {
                        var values = rows.Values(column).Slice(piece.Start, piece.Count);
                        var nulls = NullMap(rows, column, piece.Start, piece.Count);

                        if (sensor.ColumnType(column) == HSMTableBlocks.TableColumnType.Int64)
                            writer.AddInt64Column(values, nulls);
                        else
                            writer.AddDoubleColumn(MemoryMarshal.Cast<long, double>(values), nulls);
                    }
                }

                return writer.ToArray();
            }
            finally
            {
                writer.Reset();
            }
        }

        private static byte[] NullMap(TableRowBuffer rows, int column, int start, int count)
        {
            if (!rows.HasNulls(column))
                return Array.Empty<byte>();

            var map = new byte[TableNullMap.ByteLength(count)];
            rows.CopyNullMap(column, start, count, map);

            return map;
        }

        private void Enqueue(byte[] bytes, List<Piece> group)
        {
            var tables = new PendingTable[group.Count];
            var rows = 0;

            for (var i = 0; i < group.Count; i++)
            {
                tables[i] = new PendingTable(group[i].Sensor, group[i].Count);
                rows += group[i].Count;
            }

            lock (_backlogLock)
            {
                _backlog.AddLast(new PendingBlock(_nextSequence++, bytes, tables, rows));
                Interlocked.Add(ref _backlogBytes, bytes.Length);

                // The oldest blocks go first, never the one being sent.
                var node = _backlog.First;

                while (Interlocked.Read(ref _backlogBytes) > _maxBacklogBytes && node != null)
                {
                    var next = node.Next;

                    if (!ReferenceEquals(node.Value, _inFlight))
                    {
                        _backlog.Remove(node);
                        Interlocked.Add(ref _backlogBytes, -node.Value.Bytes.Length);
                        node.Value.DropRows(TableDropReason.BacklogFull);
                    }

                    node = next;
                }
            }
        }


        // ---- Delivery ----------------------------------------------------------------------------------------------

        private async Task DeliveryLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await _deliverySignal.WaitAsync(token).ConfigureAwait(false);
                    await DeliverAsync(token, atStop: false).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    ReportFault("The table delivery loop", ex);
                }
            }
        }

        /// <summary>
        /// Sends the backlog from its head, one block at a time, each after the answer to the one before (stop and
        /// wait), until it is empty or a block is not delivered. A block not delivered stays at the head and goes again
        /// later with the same producer and sequence.
        /// </summary>
        internal async Task DeliverAsync(CancellationToken token, bool atStop)
        {
            var channel = _channel;

            if (channel == null)
                return;

            await _deliveryGate.WaitAsync(token).ConfigureAwait(false);

            try
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();

                    if (!atStop && DateTime.UtcNow.Ticks < Interlocked.Read(ref _retryNotBeforeTicks))
                        return;

                    PendingBlock head;

                    lock (_backlogLock)
                    {
                        head = _backlog.First?.Value;
                        _inFlight = head;
                    }

                    if (head == null)
                        return;

                    TableBlockAck ack;

                    try
                    {
                        ack = await channel.PutBlockAsync(_producer, head.Sequence, head.Bytes, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        ack = TableBlockAck.NotDelivered(ex.Message);
                    }
                    finally
                    {
                        // A cancelled put leaves the block where it is, no longer in flight.
                        if (token.IsCancellationRequested)
                            lock (_backlogLock)
                                _inFlight = null;
                    }

                    OnAckForTests?.Invoke();

                    if (ack.Outcome == TableBlockOutcome.NotDelivered)
                    {
                        lock (_backlogLock)
                            _inFlight = null;

                        if (ack.RetryAfter.HasValue)
                        {
                            var wait = ack.RetryAfter.Value < MaxRetryAfter ? ack.RetryAfter.Value : MaxRetryAfter;
                            Interlocked.Exchange(ref _retryNotBeforeTicks, DateTime.UtcNow.Add(wait).Ticks);
                        }

                        _diagnostics.AddError($"Table blocks are not delivered: {ack.Error}. They are kept and sent again.");
                        return;
                    }

                    Settle(head, ack);
                }
            }
            finally
            {
                _deliveryGate.Release();
            }
        }

        // A block with a final answer leaves the backlog; its refused tables are counted and acted on.
        // The block stays in flight, and so out of reach of the backlog bound, until this step removes it: the removal,
        // the end of in-flight and the accounting are one step under the backlog's lock (#1846 review, item 6). A
        // block the bound took anyway (only a stop's DropEverything does) was counted there and is not counted again.
        private void Settle(PendingBlock block, TableBlockAck ack)
        {
            bool removed;

            lock (_backlogLock)
            {
                removed = _backlog.Remove(block);

                if (removed)
                    Interlocked.Add(ref _backlogBytes, -block.Bytes.Length);

                if (ReferenceEquals(_inFlight, block))
                    _inFlight = null;
            }

            if (!removed)
                return;

            switch (ack.Outcome)
            {
                case TableBlockOutcome.Stored:
                case TableBlockOutcome.Duplicate:
                    ApplyRefusals(block, ack, countRows: true);
                    break;

                case TableBlockOutcome.AllRefused:
                    ApplyRefusals(block, ack, countRows: false);
                    block.DropRows(TableDropReason.RefusedByServer);
                    break;

                case TableBlockOutcome.Invalid:
                    block.DropRows(TableDropReason.RefusedByServer);
                    _diagnostics.AddError($"The server refused a table block as invalid ({ack.Error}); its {block.Rows} row(s) are lost and it is not sent again.");
                    break;
            }
        }

        private void ApplyRefusals(PendingBlock block, TableBlockAck ack, bool countRows)
        {
            foreach (var refusal in ack.Refused)
            {
                if (refusal.Index < 0 || refusal.Index >= block.Tables.Length)
                {
                    _logger?.Error($"The server refused table {refusal.Index} ({refusal.Table}) of a block of {block.Tables.Length} table(s): {refusal.Reason}.");
                    continue;
                }

                var table = block.Tables[refusal.Index];
                var sensor = table.Sensor;

                if (countRows)
                    sensor.Drop(table.Rows, TableDropReason.RefusedByServer);

                switch (refusal.Reason)
                {
                    case "UnknownSensor":
                        // An operator may have removed the sensor: it is registered again; the rows of this block are lost.
                        if (sensor.TryMove(TableSensorState.Registered, TableSensorState.Pending))
                        {
                            _diagnostics.AddError($"The server does not know the table sensor {sensor.Path} any more; it is registered again. The rows of the refused block are lost.");

                            if (!_manualTick)
                                KickRegistration();
                        }
                        break;

                    case "SensorBlocked":
                    case "SensorCreatedAfterBlock":
                        _diagnostics.AddError($"The server refused the rows of the table sensor {sensor.Path} in a block: {refusal.Reason}.");
                        break;

                    case "NotATableSensor":
                    case "SchemaIdMismatch":
                    case "ColumnTypesMismatch":
                    case "InvalidIdentifier":
                        if (sensor.TryMove(TableSensorState.Registered, TableSensorState.Refused, $"the server refused its rows: {refusal.Reason}"))
                        {
                            _diagnostics.AddError($"The server refused the rows of the table sensor {sensor.Path}: {refusal.Reason}. Its rows are dropped and counted from now on.");
                            sensor.DiscardRows(TableDropReason.SensorRefused);
                        }
                        break;

                    default:
                        _diagnostics.AddError($"The server refused the rows of the table sensor {sensor.Path} for a reason this collector does not know: {refusal.Reason}.");
                        break;
                }
            }

            if (ack.RefusedCount > ack.Refused.Count)
                _diagnostics.AddError($"The server refused {ack.RefusedCount} table(s) of a block and listed {ack.Refused.Count}; the rows of the others are lost too.");
        }


        private void ReportFault(string what, Exception ex)
        {
            try
            {
                _diagnostics.AddError($"{what} failed: {ex.Message}");
                _logger?.Error(ex);
            }
            catch (Exception)
            {
                // Reporting must not throw into a loop.
            }
        }

        private void Observe(Task task) =>
            task.ContinueWith(t =>
                {
                    if (t.Exception != null && !(t.Exception.InnerException is OperationCanceledException))
                        ReportFault("A table sensor registration", t.Exception.InnerException ?? t.Exception);
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);


        private readonly struct PendingTable
        {
            internal PendingTable(TableSensor sensor, int rows)
            {
                Sensor = sensor;
                Rows = rows;
            }

            internal TableSensor Sensor { get; }

            internal int Rows { get; }
        }

        /// <summary>A packed block waiting for its final answer; its tables in directory order.</summary>
        private sealed class PendingBlock
        {
            internal PendingBlock(long sequence, byte[] bytes, PendingTable[] tables, int rows)
            {
                Sequence = sequence;
                Bytes = bytes;
                Tables = tables;
                Rows = rows;
            }

            internal long Sequence { get; }

            internal byte[] Bytes { get; }

            internal PendingTable[] Tables { get; }

            internal int Rows { get; }

            internal void DropRows(TableDropReason reason)
            {
                foreach (var table in Tables)
                    table.Sensor.Drop(table.Rows, reason);
            }
        }
    }
}
