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

        /// <summary>
        /// Hands lost rows to the <c>Queue overflow</c> sensor under the queue "Table rows". False when no such sensor
        /// takes values now (none registered, or it has published its last bar of the run): the rows stay in the ledger.
        /// </summary>
        bool TryReportDroppedRows(long rows);
    }


    /// <summary>
    /// The table sensors of one collector (ADR-0021 points 5–7): it registers them, takes the rows of all of them at
    /// every tick into blocks (one block per tick unless the limits split it), keeps the blocks in a bounded backlog and
    /// delivers them one at a time, in sequence order, through the sender's <see cref="ITableBlockChannel"/>.
    /// <para>
    /// It exists only once the first table sensor is created: a collector without table sensors has none of it.
    /// Everything a run owns is in its <see cref="TableRun"/>; a run that ended refuses everything, so nothing of it
    /// reaches the next one.
    /// </para>
    /// <para>
    /// Threads: rows are added by the application on any thread; the tick runs on the collector's scheduler and only
    /// packs; delivery runs in a loop of its own, so a slow server never delays packing. Nothing here throws into the
    /// host, and nothing is reported while a gate or a lock of the pipeline is held: reports are queued and written
    /// by <see cref="FlushReports"/> once the gates are released.
    /// </para>
    /// </summary>
    internal sealed class TablePipeline : IDisposable
    {
        /// <summary>The queue name under which the <c>Queue overflow</c> sensor counts lost table rows.</summary>
        internal const string DroppedRowsQueueName = "Table rows";

        private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(5);

        private readonly ConcurrentDictionary<string, TableSensor> _sensors = new ConcurrentDictionary<string, TableSensor>(StringComparer.Ordinal);
        private readonly ConcurrentQueue<Report> _reports = new ConcurrentQueue<Report>();
        private readonly object _createLock = new object();
        private readonly object _runLock = new object();
        private readonly object _writerLock = new object();

        private readonly ITableBlockChannel _channel;
        private readonly string _unavailableReason;
        private readonly ITableDiagnostics _diagnostics;
        private readonly ICollectorScheduler _scheduler;
        private readonly ICollectorLogger _logger;
        private readonly TimeSpan _tickPeriod;
        private readonly long _maxBufferBytes;
        private readonly long _maxBacklogBytes;
        private readonly bool _manualTick;
        private readonly Func<TableBlockWriter> _compressorFactory;

        // The compressor, made with the first table sensor; null while it could not be made.
        private TableBlockWriter _writer;

        // The run going now (null between runs), and the last one that went: its ledger holds the losses until the stop
        // line of the collector's stop is written.
        private TableRun _run;
        private TableRun _lastRun;
        private long _runCount;
        private int _disposed;


        internal TablePipeline(CollectorOptions options, ITableBlockChannel channel, string unavailableReason,
                               ITableDiagnostics diagnostics, ICollectorScheduler scheduler, ICollectorLogger logger)
        {
            _unavailableReason = unavailableReason ?? (channel == null ? "table sensors need the collector session transport" : channel.TablesUnavailableReason);
            _channel = _unavailableReason == null ? channel : null;

            _diagnostics = diagnostics;
            _scheduler = scheduler;
            _logger = logger;
            _tickPeriod = options.TableTickPeriod;
            _maxBufferBytes = options.MaxTableBufferBytes;
            _maxBacklogBytes = options.MaxTableBacklogBytes;
            _manualTick = options.ManualTableTick;
            _compressorFactory = options.TableCompressorFactory;
        }


        /// <summary>Why this collector cannot send tables, or null when it can.</summary>
        internal string UnavailableReason => _unavailableReason;

        internal IReadOnlyCollection<TableSensor> Sensors => _sensors.Values.ToList();

        internal bool CanAcceptData => _diagnostics.CanAcceptData;

        // The run the counters below describe: the one going now, else the last one.
        private TableRun ObservedRun
        {
            get
            {
                lock (_runLock)
                    return _run ?? _lastRun;
            }
        }

        /// <summary>The run going now; null between runs.</summary>
        internal TableRun CurrentRun
        {
            get
            {
                lock (_runLock)
                    return _run;
            }
        }

        /// <summary>For tests: the number of runs started.</summary>
        internal long RunCount => Interlocked.Read(ref _runCount);

        /// <summary>True once the compressor exists.</summary>
        internal bool HasCompressor
        {
            get
            {
                lock (_writerLock)
                    return _writer != null;
            }
        }

        internal long BufferedBytes => ObservedRun?.BufferedBytes ?? 0;

        internal long BacklogBytes => Interlocked.Read(ref ObservedRunOrEmpty.BacklogBytes);

        internal int BacklogBlocks
        {
            get
            {
                var run = ObservedRun;

                if (run == null)
                    return 0;

                lock (run.BacklogLock)
                    return run.Backlog.Count;
            }
        }

        /// <summary>Every row the sensors of this pipeline counted as lost.</summary>
        internal long DroppedRows => _sensors.Values.Sum(s => s.RowsDropped);

        /// <summary>The producer id of the current (or last) run; a new one at every Start.</summary>
        internal Guid Producer => ObservedRun?.Producer ?? Guid.Empty;

        private static readonly TableRun NoRun = new TableRun(0, 0);

        private TableRun ObservedRunOrEmpty => ObservedRun ?? NoRun;


        // ---- Reports, written outside every gate ---------------------------------------------------------------------

        private readonly struct Report
        {
            internal Report(string error, string logLine, Exception fault)
            {
                Error = error;
                LogLine = logLine;
                Fault = fault;
            }

            internal string Error { get; }

            internal string LogLine { get; }

            internal Exception Fault { get; }
        }

        // Queued, never written here: the caller may hold a gate.
        private void Note(string error) => _reports.Enqueue(new Report(error, null, null));

        private void NoteLog(string line) => _reports.Enqueue(new Report(null, line, null));

        private void NoteFault(string what, Exception ex) => _reports.Enqueue(new Report($"{what} failed: {ex.Message}", null, ex));

        /// <summary>
        /// Writes the queued reports. Called where no gate or lock of the pipeline is held: a host logger that calls back
        /// into the collector (even <c>Dispose</c>) cannot deadlock on the pipeline.
        /// </summary>
        internal void FlushReports()
        {
            while (_reports.TryDequeue(out var report))
            {
                try
                {
                    if (report.Error != null)
                        _diagnostics.AddError(report.Error);

                    if (report.LogLine != null)
                        _logger?.Error(report.LogLine);

                    if (report.Fault != null)
                        _logger?.Error(report.Fault);
                }
                catch (Exception)
                {
                    // Reporting must not throw into a loop.
                }
            }
        }

        /// <summary>For tests: whether reports wait to be written.</summary>
        internal bool HasQueuedReports => !_reports.IsEmpty;


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
                var unavailable = _unavailableReason ?? (canRegister ? null : "the collector is stopping or disposed");

                // The compressor is made with the first sensor that can use it: a collector without table sensors
                // never pays for it. When it cannot be made, the sensor says why and its rows are dropped and counted.
                if (unavailable == null)
                    unavailable = EnsureCompressor();

                if (unavailable != null)
                {
                    sensor.TryMove(TableSensorState.Pending, TableSensorState.Unavailable, unavailable);
                    Note($"The table sensor {normalized} cannot send its rows: {unavailable}. Its rows are dropped and counted.");
                }

                lock (_runLock)
                {
                    _sensors[normalized] = sensor;

                    // A sensor created while a run goes belongs to it at once, also one that cannot send: the rows it
                    // refuses are counted in the run's ledger and reported with every other loss.
                    if (_run != null)
                    {
                        sensor.Attach(_run);

                        if (sensor.State == TableSensorState.Pending && !_manualTick)
                            KickRegistration(_run);
                    }
                }

                return sensor;
            }
        }

        // Null when the compressor exists; otherwise why it cannot be made.
        private string EnsureCompressor()
        {
            lock (_writerLock)
            {
                if (_writer != null)
                    return null;

                try
                {
                    _writer = _compressorFactory != null ? _compressorFactory() : new TableBlockWriter();
                    return null;
                }
                catch (Exception ex)
                {
                    return $"the table block compressor cannot be created ({ex.GetType().Name}: {ex.Message})";
                }
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


        // ---- Losses -----------------------------------------------------------------------------------------------

        /// <summary>For tests: runs once a reservation has decided, whatever it decided.</summary>
        internal Action OnReservationForTests { get; set; }

        /// <summary>
        /// Reports what the ledger of <paramref name="run"/> gathered since the last report: one deduplicated line per
        /// reason, and the rows to the <c>Queue overflow</c> sensor while it takes them (they stay in the ledger when it
        /// does not). Called at every tick and at the end of a stop, under no gate.
        /// </summary>
        private void ReportLosses(TableRun run)
        {
            if (run == null)
                return;

            foreach (var reason in run.Ledger.TakeReasons())
                Note(DescribeDrop(reason));

            var rows = run.Ledger.TakeUnreported();

            if (rows > 0)
            {
                bool taken;

                try
                {
                    taken = _diagnostics.TryReportDroppedRows(rows);
                }
                catch (Exception)
                {
                    taken = false;
                }

                if (!taken)
                    run.Ledger.GiveBack(rows);
            }

            FlushReports();
        }

        /// <summary>
        /// The one stop line of the tables, written at the very end of a collector stop (after the value drain, after the
        /// sensors stopped, after the table drain): the rows the last run lost that are in no published bar of the
        /// <c>Queue overflow</c> sensor. It closes that run's ledger: a row refused after it is counted on its sensor only.
        /// </summary>
        internal void ReportStopLine()
        {
            var run = ObservedRun;

            if (run == null || ReferenceEquals(run, CurrentRun))
                return;

            var rows = run.Ledger.Finish();

            FlushReports();

            if (rows > 0)
                _logger?.Error($"Collector stop: {rows} table row(s) were lost during this run and are not in the Queue overflow sensor (not registered, or reported after it published its last bar).");
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
                case TableDropReason.BufferGrowthFailed: return "Table rows were dropped: the table row buffer could not grow (out of memory).";
                default: return $"Table rows were dropped ({reason}).";
            }
        }


        // ---- Lifecycle --------------------------------------------------------------------------------------------

        /// <summary>
        /// Starts a run: a new producer, every sensor registered again, the tick and the delivery loop. Does nothing when a
        /// run goes already or no table sensor exists. A pipeline whose sensors cannot send (no table transport, no
        /// compressor) has a run all the same, with its tick and its ledger and no delivery loop: the rows such a sensor
        /// refuses are lost rows and are reported like every other loss. Never throws.
        /// </summary>
        internal void Start()
        {
            try
            {
                lock (_runLock)
                {
                    if (_run != null || Volatile.Read(ref _disposed) == 1 || _sensors.IsEmpty)
                        return;

                    var run = new TableRun(Interlocked.Increment(ref _runCount), _maxBufferBytes, () => OnReservationForTests);

                    foreach (var sensor in _sensors.Values)
                        sensor.Attach(run);

                    _run = run;
                    _lastRun = run;

                    if (_manualTick)
                        return;

                    run.Tick = _scheduler.Schedule((Func<Task>)(() => TickAsync(run)), _tickPeriod, _tickPeriod, ex => { NoteFault("The table tick", ex); FlushReports(); });

                    if (_channel != null)
                        run.DeliveryLoop = Task.Run(() => DeliveryLoopAsync(run));

                    KickRegistration(run);
                }
            }
            catch (Exception ex)
            {
                NoteFault("Starting the table sensors", ex);
            }
        }

        /// <summary>
        /// The stop drain of the tables: the rows still buffered are packed and every block is offered to the server
        /// until <paramref name="drainBudget"/> runs out or a block is not delivered. What is left is dropped and counted
        /// in the run's ledger. Bounded by the budget; does not throw.
        /// </summary>
        internal async Task FlushAsync(CancellationToken drainBudget)
        {
            var run = await EndRunAsync().ConfigureAwait(false);

            if (run == null)
                return;

            try
            {
                // The final pack: what it takes is the last of this run. Rows added after the drain began were refused
                // and counted; it observes the budget between passes, and what is not packed in time is counted.
                await PackAsync(run, drainBudget, TableDropReason.NotDeliveredAtStop, final: true).ConfigureAwait(false);

                if (_channel != null && !drainBudget.IsCancellationRequested)
                    await DeliverAsync(run, drainBudget, atStop: true).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The budget is spent: what is left is counted below.
            }
            catch (Exception ex)
            {
                NoteFault("The table stop drain", ex);
            }

            // The table connection closes inside the drain budget, before the transport (the session) does.
            await CloseChannelAsync(drainBudget).ConfigureAwait(false);
            await CloseRunAsync(run).ConfigureAwait(false);
        }

        /// <summary>A stop that does not drain (the rollback of a failed start): everything held is dropped and counted.</summary>
        internal async Task StopWithoutFlushAsync()
        {
            var run = await EndRunAsync().ConfigureAwait(false);

            if (run == null)
                return;

            using (var noLinger = new CancellationTokenSource())
            {
                noLinger.Cancel();
                await CloseChannelAsync(noLinger.Token).ConfigureAwait(false);
            }

            await CloseRunAsync(run).ConfigureAwait(false);
        }

        private async Task CloseChannelAsync(CancellationToken budget)
        {
            var channel = _channel;

            if (channel == null)
                return;

            try
            {
                await channel.CloseAsync(budget).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                NoteFault("Closing the table connection", ex);
            }
        }

        // Takes the run going now out of the pipeline and ends its tick, its loop and its registrations; the run refuses
        // rows from now on. Null when no run was going.
        private async Task<TableRun> EndRunAsync()
        {
            TableRun run;

            lock (_runLock)
            {
                run = _run;
                _run = null;
            }

            if (run == null)
                return null;

            run.BeginDrain();

            try { run.Cancellation.Cancel(); }
            catch (ObjectDisposedException) { }

            try
            {
                if (run.Tick != null)
                    await run.Tick.StopAsync(waitForCurrentRun: true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                NoteFault("Stopping the table tick", ex);
            }

            if (run.DeliveryLoop != null)
                await Task.WhenAny(run.DeliveryLoop, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);

            OnStopWaitsForPackForTests?.Invoke();

            return run;
        }

        // Ends the run for good: after a pack of it that is under way, nothing of it is packed or enqueued again; what it
        // still holds is dropped and counted in its ledger, and the losses are reported (outside every gate).
        private async Task CloseRunAsync(TableRun run)
        {
            await run.PackGate.WaitAsync().ConfigureAwait(false);

            try
            {
                run.Close();
            }
            finally
            {
                run.PackGate.Release();
            }

            DropEverything(run);
            ReportLosses(run);
        }

        private void DropEverything(TableRun run)
        {
            lock (run.BacklogLock)
            {
                foreach (var block in run.Backlog)
                    block.DropRows(TableDropReason.NotDeliveredAtStop);

                run.Backlog.Clear();
                run.InFlight = null;
                Interlocked.Exchange(ref run.BacklogBytes, 0);
            }

            foreach (var sensor in _sensors.Values)
                sensor.DiscardRows(run, TableDropReason.NotDeliveredAtStop);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            try { StopWithoutFlushAsync().ConfigureAwait(false).GetAwaiter().GetResult(); }
            catch (Exception) { }

            // A dispose writes no stop line: the collector's stop wrote it, or there was no stop.
            ObservedRun?.Ledger.Finish();

            lock (_writerLock)
            {
                _writer?.Dispose();
                _writer = null;
            }

            FlushReports();
        }


        // ---- Registration ------------------------------------------------------------------------------------------

        private void KickRegistration(TableRun run)
        {
            var channel = _channel;

            if (channel == null || run.IsClosed)
                return;

            var token = run.Token;

            Observe(Task.Run(async () =>
            {
                await channel.WaitUntilReadyAsync(token).ConfigureAwait(false);
                await RegisterPendingAsync(run).ConfigureAwait(false);
            }));
        }

        /// <summary>For tests: runs after a registration request is answered, before the answer is applied.</summary>
        internal Func<Task> OnRegistrationAnsweredForTests { get; set; }

        /// <summary>For tests: runs before the answer is applied to each sensor of a registration request.</summary>
        internal Func<TableSensor, Task> OnRegistrationApplyForTests { get; set; }

        /// <summary>
        /// Registers every Pending sensor of <paramref name="run"/> not already in a request, in one request of table
        /// registrations. Accepted sensors are Registered, refused ones Refused for good; the rest stay Pending and go
        /// again on the next tick. An answer applies only to sensors that still belong to that run.
        /// </summary>
        internal async Task RegisterPendingAsync(TableRun run)
        {
            var channel = _channel;

            if (channel == null || run.IsClosed)
                return;

            // In path order, so the frames of a large request are cut the same way every time.
            var batch = _sensors.Values.Where(s => ReferenceEquals(s.Run, run) && s.State == TableSensorState.Pending && run.Registering.TryAdd(s, 0))
                                       .OrderBy(s => s.Path, StringComparer.Ordinal)
                                       .ToList();

            if (batch.Count == 0)
                return;

            try
            {
                TableRegistrationResult result;

                try
                {
                    result = await channel.RegisterTablesAsync(batch.Select(s => s.BuildRegistration()).ToList(), run.Token).ConfigureAwait(false);
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

                var unsettled = 0;

                foreach (var sensor in batch)
                {
                    var applying = OnRegistrationApplyForTests;

                    if (applying != null)
                        await applying(sensor).ConfigureAwait(false);

                    // Each move checks, under the sensor's lock, that the sensor still belongs to this run: an answer
                    // that comes after the run ended changes nothing of the next run.
                    if (result.Accepted.Contains(sensor.Path))
                    {
                        sensor.TryMove(run, TableSensorState.Pending, TableSensorState.Registered);
                    }
                    else if (result.Refused.TryGetValue(sensor.Path, out var reason))
                    {
                        if (sensor.TryMove(run, TableSensorState.Pending, TableSensorState.Refused, reason))
                        {
                            Note($"The server refused the table sensor {sensor.Path}: {reason}. It is not registered again; its rows are dropped and counted. Another shape or another place needs another sensor path.");
                            sensor.DiscardRows(run, TableDropReason.SensorRefused);
                        }
                    }
                    else if (ReferenceEquals(sensor.Run, run))
                    {
                        unsettled++;
                    }
                }

                if (unsettled > 0)
                    Note($"Table sensor registrations were not delivered ({result.UnsettledReason ?? "no answer for them"}); they are sent again on the next tick and their rows are kept.");
            }
            finally
            {
                foreach (var sensor in batch)
                    run.Registering.TryRemove(sensor, out _);

                FlushReports();
            }
        }


        // ---- The tick and packing ----------------------------------------------------------------------------------

        /// <summary>
        /// One tick of the run going now. With <see cref="CollectorOptions.ManualTableTick"/> (tests) the registration
        /// and the delivery are awaited here, so the tick is deterministic.
        /// </summary>
        internal Task TickAsync()
        {
            var run = CurrentRun;
            return run == null ? Task.CompletedTask : TickAsync(run);
        }

        /// <summary>
        /// One tick of <paramref name="run"/>: reports its losses, registers the sensors that wait for it, packs the rows
        /// of every registered sensor of the run into blocks and hands them to delivery. A tick of a run that ended does
        /// nothing.
        /// </summary>
        internal async Task TickAsync(TableRun run)
        {
            if (!run.AcceptsRows)
                return;

            var token = run.Token;

            try
            {
                ReportLosses(run);

                var registration = RegisterPendingAsync(run);

                if (_manualTick)
                    await registration.ConfigureAwait(false);
                else
                    Observe(registration);

                var beforePack = OnTickBeforePackForTests;

                if (beforePack != null)
                    await beforePack().ConfigureAwait(false);

                await PackAsync(run, token, TableDropReason.NotDeliveredAtStop, final: false).ConfigureAwait(false);

                if (_manualTick)
                    await DeliverAsync(run, token, atStop: false).ConfigureAwait(false);
                else
                    run.DeliverySignal.Release();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (run.IsClosed)
            {
            }
            finally
            {
                FlushReports();
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

        /// <summary>For tests: a pack of the run going now.</summary>
        internal Task PackAsync(CancellationToken token, TableDropReason cancelledReason)
        {
            var run = CurrentRun;
            return run == null ? Task.CompletedTask : PackAsync(run, token, cancelledReason, final: false);
        }

        /// <summary>
        /// Takes the rows of every registered sensor of <paramref name="run"/> and packs them into blocks at the end of
        /// its backlog. A tick's pack (<paramref name="final"/> false) does nothing once the run's drain began; the stop's
        /// final pack does nothing once the run is closed. Observes <paramref name="token"/> before every compression
        /// pass: the rows not packed when it fires are dropped under <paramref name="cancelledReason"/> and counted.
        /// Reports nothing while it holds the gate.
        /// </summary>
        internal async Task PackAsync(TableRun run, CancellationToken token, TableDropReason cancelledReason, bool final)
        {
            await run.PackGate.WaitAsync().ConfigureAwait(false);

            var taken = new List<KeyValuePair<TableSensor, TableRowBuffer>>();
            long abandoned = 0;

            try
            {
                if (run.IsClosed || (!final && !run.AcceptsRows))
                    return;

                foreach (var sensor in _sensors.Values)
                {
                    if (sensor.State != TableSensorState.Registered)
                        continue;

                    var rows = sensor.TakeRows(run);

                    if (rows != null)
                        taken.Add(new KeyValuePair<TableSensor, TableRowBuffer>(sensor, rows));
                }

                if (taken.Count == 0)
                    return;

                var limits = _channel?.BlockLimits ?? TableBlockLimits.Default;
                var maxBlock = _channel?.MaxBlockBytes ?? int.MaxValue;
                var pieces = new List<Piece>();

                foreach (var entry in taken)
                    Cut(entry.Key, entry.Value, limits, pieces);

                foreach (var group in Group(pieces, limits))
                    abandoned += EncodeAndQueue(run, group, maxBlock, limits, token, cancelledReason, (bytes, packed) => Enqueue(run, bytes, packed));
            }
            catch (Exception ex)
            {
                // Packing has no reason to fail; if it does, the rows it took are lost and said so.
                NoteFault("Packing table rows", ex);

                foreach (var entry in taken)
                    if (entry.Value.Count > 0)
                        entry.Key.Drop(entry.Value.Count, TableDropReason.PackingFailed, run);
            }
            finally
            {
                foreach (var entry in taken)
                {
                    run.Release((long)entry.Value.Count * entry.Key.RowBytes);
                    entry.Key.ReturnRows(entry.Value);
                }

                run.PackGate.Release();
            }

            if (abandoned > 0)
                NoteLog($"Packing table rows was cut short ({abandoned} row(s) not packed in time); they are dropped and counted.");

            FlushReports();
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

        // One block per group; a block larger than a frame allows, or one that breaks a block limit once written (the
        // pieces keep to the limits by estimate; the written block is what the table connection checks), is split, by
        // tables first and then by rows. A single row that still breaks one is dropped and counted as too large: every
        // limit a block can break on its own is judged here, so no block that leaves this method is too large for the
        // limits it was packed for. Returns the rows left unpacked because the token fired (dropped and counted here).
        private long EncodeAndQueue(TableRun run, List<Piece> group, int maxBlock, TableBlockLimits limits, CancellationToken token,
                                    TableDropReason cancelledReason, Action<byte[], List<Piece>> sink)
        {
            if (token.IsCancellationRequested)
            {
                long rows = 0;

                foreach (var cut in group)
                {
                    cut.Sensor.Drop(cut.Count, cancelledReason, run);
                    rows += cut.Count;
                }

                return rows;
            }

            OnPackPassForTests?.Invoke();

            var block = Encode(group);
            var exceeded = Exceeds(block, maxBlock, limits);

            if (exceeded == null)
            {
                sink(block, group);
                return 0;
            }

            if (group.Count > 1)
            {
                var half = group.Count / 2;
                return EncodeAndQueue(run, group.GetRange(0, half), maxBlock, limits, token, cancelledReason, sink)
                     + EncodeAndQueue(run, group.GetRange(half, group.Count - half), maxBlock, limits, token, cancelledReason, sink);
            }

            var piece = group[0];

            if (piece.Count > 1)
            {
                var first = piece.Count / 2;
                return EncodeAndQueue(run, new List<Piece> { new Piece(piece.Sensor, piece.Buffer, piece.Start, first) }, maxBlock, limits, token, cancelledReason, sink)
                     + EncodeAndQueue(run, new List<Piece> { new Piece(piece.Sensor, piece.Buffer, piece.Start + first, piece.Count - first) }, maxBlock, limits, token, cancelledReason, sink);
            }

            piece.Sensor.Drop(1, TableDropReason.RowTooLarge, run);
            Note($"A row of the table sensor {piece.Sensor.Path} does not fit {exceeded} and is dropped.");
            return 0;
        }

        // The limit a written block breaks, in words that name the limit and not the block (a report is deduplicated by
        // its text); null when it breaks none. The same checks as the table connection's before a put.
        private static string Exceeds(byte[] block, int maxBlock, TableBlockLimits limits)
        {
            if (block.Length > maxBlock)
                return $"a frame of the table connection ({maxBlock} bytes)";

            var header = TableBlockReader.ReadHeader(block, ReadBackLimits);

            return header.DecompressedLength > limits.MaxDecompressedBytes ? $"the block limit of the table connection ({limits.MaxDecompressedBytes} bytes before compression)"
                : header.RowCount > limits.MaxRows ? $"the block limit of the table connection ({limits.MaxRows} rows)"
                : header.TableCount > limits.MaxTables ? $"the block limit of the table connection ({limits.MaxTables} tables)"
                : null;
        }

        /// <summary>For tests: runs when a stop has ended a run's tick and loop and is about to wait for a pack of it.</summary>
        internal Action OnStopWaitsForPackForTests { get; set; }

        /// <summary>For tests: runs after a block got its answer and before the answer is applied.</summary>
        internal Action OnAckForTests { get; set; }

        /// <summary>For tests: runs in a tick after the registration and before the pack.</summary>
        internal Func<Task> OnTickBeforePackForTests { get; set; }

        /// <summary>For tests: runs before every compression pass of a pack.</summary>
        internal Action OnPackPassForTests { get; set; }

        private byte[] Encode(List<Piece> group)
        {
            lock (_writerLock)
            {
                var writer = _writer ?? throw new ObjectDisposedException(nameof(TableBlockWriter));

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
        }

        private static byte[] NullMap(TableRowBuffer rows, int column, int start, int count)
        {
            if (!rows.HasNulls(column))
                return Array.Empty<byte>();

            var map = new byte[TableNullMap.ByteLength(count)];
            rows.CopyNullMap(column, start, count, map);

            return map;
        }

        // Under the run's pack gate, so the sequence is the run's own and rises in the order of the blocks.
        private void Enqueue(TableRun run, byte[] bytes, List<Piece> group)
        {
            var tables = new PendingTable[group.Count];
            var rows = 0;

            for (var i = 0; i < group.Count; i++)
            {
                tables[i] = new PendingTable(group[i].Sensor, group[i].Count);
                rows += group[i].Count;
            }

            var block = new PendingBlock(run, run.NextSequence++, bytes, tables, rows);

            lock (run.BacklogLock)
            {
                run.Backlog.AddLast(block);
                Interlocked.Add(ref run.BacklogBytes, bytes.Length);

                // The oldest blocks go first, never the one being sent.
                var node = run.Backlog.First;

                while (Interlocked.Read(ref run.BacklogBytes) > _maxBacklogBytes && node != null)
                {
                    var next = node.Next;

                    if (!ReferenceEquals(node.Value, run.InFlight))
                    {
                        run.Backlog.Remove(node);
                        Interlocked.Add(ref run.BacklogBytes, -node.Value.Bytes.Length);
                        node.Value.DropRows(TableDropReason.BacklogFull);
                    }

                    node = next;
                }
            }
        }


        // ---- Delivery ----------------------------------------------------------------------------------------------

        private async Task DeliveryLoopAsync(TableRun run)
        {
            var token = run.Token;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await run.DeliverySignal.WaitAsync(token).ConfigureAwait(false);
                    await DeliverAsync(run, token, atStop: false).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (ObjectDisposedException) when (run.IsClosed)
                {
                    return;
                }
                catch (Exception ex)
                {
                    NoteFault("The table delivery loop", ex);
                    FlushReports();
                }
            }
        }

        /// <summary>
        /// Sends the backlog of <paramref name="run"/> from its head, one block at a time, each after the answer to the one
        /// before (stop and wait), until it is empty or a block is not delivered. A block not delivered stays at the head
        /// and goes again later with the same producer and sequence. An answer that came back is applied whatever the
        /// token says: a block the server stored is never counted as lost.
        /// </summary>
        internal async Task DeliverAsync(TableRun run, CancellationToken token, bool atStop)
        {
            var channel = _channel;

            if (channel == null)
                return;

            await run.DeliveryGate.WaitAsync(token).ConfigureAwait(false);

            try
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();

                    if (!atStop && DateTime.UtcNow.Ticks < Interlocked.Read(ref run.RetryNotBeforeTicks))
                        return;

                    PendingBlock head;

                    lock (run.BacklogLock)
                    {
                        head = run.IsClosed ? null : run.Backlog.First?.Value;
                        run.InFlight = head;
                    }

                    if (head == null)
                        return;

                    TableBlockAck ack;

                    try
                    {
                        ack = await channel.PutBlockAsync(run.Producer, head.Sequence, head.Bytes, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        // No answer: the block stays where it is, no longer in flight. It may have reached the server.
                        lock (run.BacklogLock)
                        {
                            head.MaybeSent = true;

                            if (ReferenceEquals(run.InFlight, head))
                                run.InFlight = null;
                        }

                        throw;
                    }
                    catch (Exception ex)
                    {
                        ack = TableBlockAck.NotDelivered(ex.Message);
                    }

                    OnAckForTests?.Invoke();

                    if (ack.Outcome == TableBlockOutcome.TooLarge)
                    {
                        // Its frame went out on a connection of this put that was lost before the ack: the server may
                        // hold its sequence, which is then never given to other rows (RepackAsync).
                        if (ack.MaybeSent)
                            lock (run.BacklogLock)
                                head.MaybeSent = true;

                        // A block of one row cannot be made smaller: the answer is final, its row dropped and counted.
                        if (head.Rows <= 1)
                        {
                            if (DropHead(run, head, TableDropReason.RowTooLarge))
                                Note($"A row of the table sensor {(head.Tables.Length > 0 ? head.Tables[0].Sensor.Path : "?")} is larger than the table connection takes now and is dropped.");

                            continue;
                        }

                        // Packed for larger limits than the table connection takes now: its rows are packed again into
                        // blocks of at most half its rows, in its place, and the delivery goes on with the first of
                        // them. Every such answer removes the head or halves it, so the answers TooLarge to the rows of
                        // one block are bounded (at most twice its rows); no answer makes this loop turn without either
                        // progress or a return to wait.
                        await RepackAsync(run, head, ack.Error).ConfigureAwait(false);
                        continue;
                    }

                    if (ack.Outcome == TableBlockOutcome.NotDelivered)
                    {
                        lock (run.BacklogLock)
                        {
                            head.MaybeSent = true;

                            if (ReferenceEquals(run.InFlight, head))
                                run.InFlight = null;
                        }

                        if (ack.RetryAfter.HasValue)
                        {
                            var wait = ack.RetryAfter.Value < MaxRetryAfter ? ack.RetryAfter.Value : MaxRetryAfter;
                            Interlocked.Exchange(ref run.RetryNotBeforeTicks, DateTime.UtcNow.Add(wait).Ticks);
                        }

                        Note($"Table blocks are not delivered: {ack.Error}. They are kept and sent again.");
                        return;
                    }

                    Settle(run, head, ack);
                }
            }
            finally
            {
                run.DeliveryGate.Release();
                FlushReports();
            }
        }

        // The limits a block written by this collector is read back with: the format's own, so a block packed for any
        // server reads.
        private static readonly TableBlockLimits ReadBackLimits = new TableBlockLimits(TableBlockLimits.FormatMaxDecompressedBytes, int.MaxValue, int.MaxValue,
                                                                                         TableBlockLimits.FormatMaxColumnsPerTable, TableBlockLimits.FormatMaxTableIdLength);

        /// <summary>For tests: blocks packed again because a frame of the table connection took less than they need.</summary>
        internal long RepackedBlocks => Interlocked.Read(ref _repacked);

        private long _repacked;

        // A block packed for a larger frame (or larger block limits) than the table connection takes now: a server whose
        // hello came after the pack states smaller numbers. Its rows are read back and packed again into blocks that fit,
        // which take its place at the head of the backlog. Each new block has at most half the head's rows (the head has
        // two or more), whatever the limits read now say: the limits the put judged by may be newer than the ones read
        // here, and a repack that built the same block again would be answered the same way forever. A row that breaks a
        // limit on its own is dropped and counted (EncodeAndQueue). Sequences keep their order: the new blocks take the
        // head's number (the next one when the head may have reached the server), and the blocks behind it, which were
        // never put, are renumbered after them. Under the run's pack gate, the owner of the sequence.
        private async Task RepackAsync(TableRun run, PendingBlock head, string why)
        {
            await run.PackGate.WaitAsync().ConfigureAwait(false);

            var buffers = new List<TableRowBuffer>();

            try
            {
                if (run.IsClosed)
                    return;

                Interlocked.Increment(ref _repacked);

                var block = TableBlockReader.Read(head.Bytes, ReadBackLimits);
                var current = _channel?.BlockLimits ?? TableBlockLimits.Default;
                var limits = new TableBlockLimits(current.MaxDecompressedBytes, current.MaxTables, Math.Min(current.MaxRows, Math.Max(1, head.Rows / 2)),
                                                  current.MaxColumnsPerTable, current.MaxTableIdLength);
                var maxBlock = _channel?.MaxBlockBytes ?? int.MaxValue;
                var pieces = new List<Piece>();

                for (var index = 0; index < block.Tables.Count && index < head.Tables.Length; index++)
                {
                    var sensor = head.Tables[index].Sensor;
                    var rows = ReadBack(sensor, block.Tables[index]);

                    buffers.Add(rows);
                    Cut(sensor, rows, limits, pieces);
                }

                var packed = new List<KeyValuePair<byte[], List<Piece>>>();

                foreach (var group in Group(pieces, limits))
                    EncodeAndQueue(run, group, maxBlock, limits, CancellationToken.None, TableDropReason.NotDeliveredAtStop,
                                   (bytes, rows) => packed.Add(new KeyValuePair<byte[], List<Piece>>(bytes, rows)));

                lock (run.BacklogLock)
                {
                    var node = run.Backlog.Find(head);

                    // A stop dropped it meanwhile: counted there.
                    if (node == null)
                        return;

                    var next = head.Sequence + (head.MaybeSent ? 1 : 0);
                    var after = node.Next;

                    foreach (var entry in packed)
                    {
                        var tables = new PendingTable[entry.Value.Count];
                        var count = 0;

                        for (var i = 0; i < tables.Length; i++)
                        {
                            tables[i] = new PendingTable(entry.Value[i].Sensor, entry.Value[i].Count);
                            count += entry.Value[i].Count;
                        }

                        run.Backlog.AddBefore(node, new PendingBlock(run, next++, entry.Key, tables, count));
                        Interlocked.Add(ref run.BacklogBytes, entry.Key.Length);
                    }

                    run.Backlog.Remove(node);
                    Interlocked.Add(ref run.BacklogBytes, -head.Bytes.Length);

                    if (ReferenceEquals(run.InFlight, head))
                        run.InFlight = null;

                    // The blocks behind were never put (one block is put at a time, always the head): they follow in
                    // order. No sequence that was on the wire is given out again: the head's is skipped when its frame
                    // may have been written (on an earlier put, or on an earlier connection of this one), and every
                    // block put before it had a lower sequence.
                    for (var later = after; later != null; later = later.Next)
                        later.Value.Sequence = next++;

                    run.NextSequence = next;
                }

                NoteLog($"A table block was packed again into {packed.Count} block(s): {why}.");
            }
            catch (Exception ex)
            {
                NoteFault("Packing a table block again", ex);
                DropHead(run, head, TableDropReason.PackingFailed);
            }
            finally
            {
                run.PackGate.Release();
            }
        }

        private static TableRowBuffer ReadBack(TableSensor sensor, BlockTable table)
        {
            var rows = sensor.NewBuffer();
            var columns = table.Columns.Count - 1;
            var cells = new TableCell[columns];

            for (var row = 0; row < table.RowCount; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var data = table.Columns[column + 1];

                    cells[column] = data.IsNull(row) ? TableCell.Null
                        : data.Type == HSMTableBlocks.TableColumnType.Double ? TableCell.FromDouble(data.DoubleValues[row])
                        : TableCell.FromInt64(data.Int64Values[row]);
                }

                rows.Append(table.Time[row], cells);
            }

            return rows;
        }

        // True when this call removed the block and counted its rows (false: a stop dropped and counted it already).
        private bool DropHead(TableRun run, PendingBlock head, TableDropReason reason)
        {
            bool removed;

            lock (run.BacklogLock)
            {
                removed = run.Backlog.Remove(head);

                if (removed)
                    Interlocked.Add(ref run.BacklogBytes, -head.Bytes.Length);

                if (ReferenceEquals(run.InFlight, head))
                    run.InFlight = null;
            }

            if (removed)
                head.DropRows(reason);

            return removed;
        }

        // A block with a final answer leaves the backlog; its refused tables are counted and acted on.
        // The block stays in flight, and so out of reach of the backlog bound, until this step removes it: the removal,
        // the end of in-flight and the accounting are one step under the backlog's lock. A block the stop dropped
        // already was counted there and is not counted again.
        private void Settle(TableRun run, PendingBlock block, TableBlockAck ack)
        {
            bool removed;

            lock (run.BacklogLock)
            {
                removed = run.Backlog.Remove(block);

                if (removed)
                    Interlocked.Add(ref run.BacklogBytes, -block.Bytes.Length);

                if (ReferenceEquals(run.InFlight, block))
                    run.InFlight = null;
            }

            if (!removed)
                return;

            switch (ack.Outcome)
            {
                case TableBlockOutcome.Stored:
                case TableBlockOutcome.Duplicate:
                    ApplyRefusals(run, block, ack, countRows: true);
                    break;

                case TableBlockOutcome.AllRefused:
                    ApplyRefusals(run, block, ack, countRows: false);
                    block.DropRows(TableDropReason.RefusedByServer);
                    break;

                case TableBlockOutcome.Invalid:
                    block.DropRows(TableDropReason.RefusedByServer);
                    Note($"The server refused a table block as invalid ({ack.Error}); its {block.Rows} row(s) are lost and it is not sent again.");
                    break;
            }
        }

        private void ApplyRefusals(TableRun run, PendingBlock block, TableBlockAck ack, bool countRows)
        {
            foreach (var refusal in ack.Refused)
            {
                if (refusal.Index < 0 || refusal.Index >= block.Tables.Length)
                {
                    NoteLog($"The server refused table {refusal.Index} ({refusal.Table}) of a block of {block.Tables.Length} table(s): {refusal.Reason}.");
                    continue;
                }

                var table = block.Tables[refusal.Index];
                var sensor = table.Sensor;

                if (countRows)
                    sensor.Drop(table.Rows, TableDropReason.RefusedByServer, run);

                switch (refusal.Reason)
                {
                    case "UnknownSensor":
                        // An operator may have removed the sensor: it is registered again; the rows of this block are lost.
                        if (sensor.TryMove(run, TableSensorState.Registered, TableSensorState.Pending))
                        {
                            Note($"The server does not know the table sensor {sensor.Path} any more; it is registered again. The rows of the refused block are lost.");

                            if (!_manualTick)
                                KickRegistration(run);
                        }
                        break;

                    case "SensorBlocked":
                    case "SensorCreatedAfterBlock":
                        Note($"The server refused the rows of the table sensor {sensor.Path} in a block: {refusal.Reason}.");
                        break;

                    case "NotATableSensor":
                    case "SchemaIdMismatch":
                    case "ColumnTypesMismatch":
                    case "InvalidIdentifier":
                        if (sensor.TryMove(run, TableSensorState.Registered, TableSensorState.Refused, $"the server refused its rows: {refusal.Reason}"))
                        {
                            Note($"The server refused the rows of the table sensor {sensor.Path}: {refusal.Reason}. Its rows are dropped and counted from now on.");
                            sensor.DiscardRows(run, TableDropReason.SensorRefused);
                        }
                        break;

                    default:
                        Note($"The server refused the rows of the table sensor {sensor.Path} for a reason this collector does not know: {refusal.Reason}.");
                        break;
                }
            }

            if (ack.RefusedCount > ack.Refused.Count)
                Note($"The server refused {ack.RefusedCount} table(s) of a block and listed {ack.Refused.Count}; the rows of the others are lost too.");
        }


        private void Observe(Task task) =>
            task.ContinueWith(t =>
                {
                    if (t.Exception != null && !(t.Exception.InnerException is OperationCanceledException))
                    {
                        NoteFault("A table sensor registration", t.Exception.InnerException ?? t.Exception);
                        FlushReports();
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }
}
