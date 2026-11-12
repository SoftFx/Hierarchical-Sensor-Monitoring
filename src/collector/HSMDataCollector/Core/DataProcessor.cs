using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.DefaultSensors.Diagnostic;
using HSMDataCollector.Exceptions;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue.Data;
using HSMDataCollector.SyncQueue.SpecificQueue;
using HSMDataCollector.Threading;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.Core
{
    internal sealed class DataProcessor : IDisposable
    {
        private readonly DataQueueProcessor _dataQueue;
        private readonly PriorityDataQueueProcessor _priorityQueue;
        private readonly FileQueueProcessor _fileQueue;
        private readonly CommandQueueProcessor _commandQueue;
        private readonly LoggerManager _logger;
        private readonly MessageDeduplicator _messageDeduplicator;
        private readonly CollectorLifecycle _lifecycle;
        private readonly object _lifecycleGate;
        private readonly TimeSpan _stopFlushTimeout;

        // 1 while a stop drains a queue whose package diagnostics must not be recorded: during the
        // command flush, which goes first, and from the data/priority drain boundary on (the file
        // flush). Those flushes still produce package diagnostics; they observe this flag and skip
        // <see cref="AddPackageInfo"/>/<see cref="AddPackageSendingInfo"/> so the diagnostics do
        // not enqueue into already-stopped data queues and survive into a later restart as stale
        // collector telemetry. Reset on every <see cref="Start"/>.
        //
        // Policy (issue #1075): suppress queue self-diagnostics after the data-drain boundary.
        // Option A from the issue — simplest lifecycle, no stale carry-over after restart. Other
        // diagnostic sources (sensor errors, overflow) are unaffected because they do not flow
        // through these two entry points.
        private int _diagnosticsSuppressedFlag;

        private DefaultSensorsCollection DefaultSensors => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? (DefaultSensorsCollection)SensorStorage.Windows : (DefaultSensorsCollection)SensorStorage.Unix;

        internal SensorsStorage SensorStorage { get; }

        /// <summary>
        /// Per-collector scheduler used by sensors and the message deduplicator. Owned by the
        /// outer <see cref="DataCollector"/>; disposed there.
        /// </summary>
        internal ICollectorScheduler Scheduler { get; }

        internal bool CanStartNewSensors => _lifecycle.CanStartNewSensors;

        internal bool CanRegisterSensors => _lifecycle.CanRegisterSensors;

        internal bool CanAcceptData => _lifecycle.CanAcceptData;

        /// <summary>
        /// The collector-wide lifecycle lock (DataCollector._opLock), shared so that sensor
        /// registration (SensorsStorage.Register) is serialized with Start/Stop/Dispose transitions.
        ///
        /// LOCK ORDER INVARIANT: this gate is the OUTER lock. Any code path that needs both this gate
        /// and <see cref="CollectorLifecycle"/>'s internal lock must take this gate first
        /// (gate → CollectorLifecycle._lock). All current callers — DataCollector.Start/Stop/Dispose
        /// and SensorsStorage.Register — already do. Never acquire this gate while holding the
        /// CollectorLifecycle lock, or from inside a sensor/queue callback.
        /// </summary>
        internal object LifecycleGate => _lifecycleGate;

        public DataProcessor(CollectorOptions options, CollectorLifecycle lifecycle, object lifecycleGate, ICollectorScheduler scheduler, LoggerManager logger)
        {
            _logger = logger;
            _lifecycle = lifecycle;
            _lifecycleGate = lifecycleGate ?? throw new ArgumentNullException(nameof(lifecycleGate));
            Scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            _stopFlushTimeout = TimeSpan.FromTicks(Math.Min(Math.Max(options.RequestTimeout.Ticks, TimeSpan.FromSeconds(1).Ticks),
                                                            TimeSpan.FromSeconds(5).Ticks));

            SensorStorage  = new SensorsStorage(options, this, logger);

            _dataQueue     = new DataQueueProcessor(options, this, logger);
            _priorityQueue = new PriorityDataQueueProcessor(options, this, logger);
            _fileQueue     = new FileQueueProcessor(options, this, logger);
            _commandQueue  = new CommandQueueProcessor(options, this, logger);
            _messageDeduplicator = new MessageDeduplicator(scheduler,
                                                           (msg) => { _logger.Error(msg);
                                                                      DefaultSensors?.CollectorErrors?.SendCollectorError(msg);
                                                                    }, options.ExceptionDeduplicatorWindow, options.MaxDeduplicatedMessages);
        }


        public bool Start()
        {
            Volatile.Write(ref _diagnosticsSuppressedFlag, 0);

            var dataStarted = _dataQueue.Start();
            var priorityStarted = dataStarted && _priorityQueue.Start();
            var fileStarted = priorityStarted && _fileQueue.Start();
            var commandStarted = fileStarted && _commandQueue.Start();

            if (!commandStarted)
            {
                RollbackStartedQueues(dataStarted, priorityStarted, fileStarted, commandStarted);
                return false;
            }

            return true;
        }

        public async Task InitAsync()
        {
            await SensorStorage.InitAsync().ConfigureAwait(false);
            await SensorStorage.StartAsync().ConfigureAwait(false);
        }

        public Task StopAsync() => StopAsync(ShutdownMode.GracefulStop);

        public async Task StopAsync(ShutdownMode mode)
        {
            // Collect failures across phases so a single phase exception does not leave background queues running.
            // After all phases attempt to stop, rethrow as AggregateException so the caller knows the stop was degraded.
            var failures = new List<Exception>();

            await TryStopPhase(() => SensorStorage.WaitForDynamicStartTasksAsync(), failures).ConfigureAwait(false);
            await TryStopPhase(() => SensorStorage.StopAsync(), failures).ConfigureAwait(false);

            if (mode.FlushAcceptedWork())
                await StopWithFlushAsync(mode, failures).ConfigureAwait(false);
            else
                await StopWithoutFlushAsync(mode, failures).ConfigureAwait(false);

            ReportUnreportedOverflow();

            if (failures.Count > 0)
                throw new AggregateException("One or more phases of DataProcessor.StopAsync failed; remaining phases completed.", failures);
        }

        private async Task StopWithFlushAsync(ShutdownMode mode, List<Exception> failures)
        {
            // Stop all queues so their processing loops exit; items remain in each queue's buffer
            // ready for a bounded flush. Each StopAsync returns whether the loop actually exited
            // within the request timeout — only the queues that exited cleanly are eligible for
            // flush below (an unresponsive sender would otherwise block flush against itself).
            // We pass the original ShutdownMode through so the queue's processing loop catches
            // observe the right PreserveCanceledPackages policy (graceful stop and terminal dispose
            // both re-enqueue the canceled package for the drain below; start-rollback drops it).
            //
            // The four loops are stopped together (#1659 A7): each wait is bounded by the mode's
            // stop timeout, and waiting for them one after another made a sender that ignores
            // cancellation cost four of those in a row.
            var dataStopped = false;
            var priorityStopped = false;
            var fileStopped = false;
            var commandStopped = false;

            await Task.WhenAll(
                TryStopPhase(async () => dataStopped = await _dataQueue.StopAsync(mode).ConfigureAwait(false), failures),
                TryStopPhase(async () => priorityStopped = await _priorityQueue.StopAsync(mode).ConfigureAwait(false), failures),
                TryStopPhase(async () => fileStopped = await _fileQueue.StopAsync(mode).ConfigureAwait(false), failures),
                TryStopPhase(async () => commandStopped = await _commandQueue.StopAsync(mode).ConfigureAwait(false), failures)).ConfigureAwait(false);

            // One budget for the whole drain (#1659 A7), as in the native collector: a stop must
            // not hold its host's restart for a budget per queue. A queue that finds the budget
            // spent sends nothing and its items are discarded and counted.
            using (var drainBudget = new CancellationTokenSource(GetFlushTimeout(mode)))
            {
                // Registrations first: the values behind them are of little use to a server that
                // was never told what those sensors are. Their package diagnostics are suppressed
                // like every diagnostic of a non-data flush (#1075, below).
                var suppressedBefore = Interlocked.Exchange(ref _diagnosticsSuppressedFlag, 1);

                if (commandStopped)
                    await FlushAndLogAsync(_commandQueue, drainBudget.Token, failures).ConfigureAwait(false);

                // Back to what it was, not to "recorded": a stop that found the boundary already
                // crossed must not reopen it for the flushes below.
                Volatile.Write(ref _diagnosticsSuppressedFlag, suppressedBefore);

                if (priorityStopped)
                    await FlushAndLogAsync(_priorityQueue, drainBudget.Token, failures).ConfigureAwait(false);

                if (dataStopped)
                    await FlushAndLogAsync(_dataQueue, drainBudget.Token, failures).ConfigureAwait(false);

                // Self-diagnostics boundary (#1075). After the data/priority queues have drained their
                // accepted user data, any further diagnostic value generated by the file flush would
                // have nowhere safe to land — the data queue is no longer accepting writes, and a
                // later restart would otherwise see stale stop-cycle telemetry as the first emitted values.
                Volatile.Write(ref _diagnosticsSuppressedFlag, 1);

                if (fileStopped)
                    await FlushAndLogAsync(_fileQueue, drainBudget.Token, failures).ConfigureAwait(false);
            }
        }

        private async Task StopWithoutFlushAsync(ShutdownMode mode, List<Exception> failures)
        {
            // Start-rollback only: the StopAsync dispatch above routes both GracefulStop and
            // TerminalDispose to StopWithFlushAsync (because mode.FlushAcceptedWork() is true for
            // both), and the only mode for which it returns false is StartRollback. We keep the
            // `mode` parameter to plumb the right ShutdownMode.ClearOnStop semantics into each
            // queue's StopAsync — that's what discards leftovers without a flush phase.
            Volatile.Write(ref _diagnosticsSuppressedFlag, 1);

            var stopTasks = new[]
            {
                TryStopPhase(() => _dataQueue.StopAsync(mode).AsTask(), failures),
                TryStopPhase(() => _priorityQueue.StopAsync(mode).AsTask(), failures),
                TryStopPhase(() => _fileQueue.StopAsync(mode).AsTask(), failures),
                TryStopPhase(() => _commandQueue.StopAsync(mode).AsTask(), failures),
            };
            await Task.WhenAll(stopTasks).ConfigureAwait(false);
        }

        private async Task FlushAndLogAsync(IQueueProcessor queue, CancellationToken drainBudget, List<Exception> failures)
        {
            await TryStopPhase(async () =>
            {
                await queue.FlushAsync(drainBudget).ConfigureAwait(false);

                LogDiscardedItems(queue.ClearQueue(), queue.QueueName);
            }, failures).ConfigureAwait(false);
        }

        private TimeSpan GetFlushTimeout(ShutdownMode mode) =>
            mode == ShutdownMode.TerminalDispose ? mode.StopWaitTimeout(_stopFlushTimeout) : _stopFlushTimeout;

        private async Task TryStopPhase(Func<Task> phase, List<Exception> failures)
        {
            try
            {
                await phase().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error($"DataProcessor stop phase failed: {ex}");
                lock (failures)
                    failures.Add(ex);
            }
        }

        public void Dispose()
        {
            _messageDeduplicator?.Dispose();
            SensorStorage?.Dispose();
            _dataQueue?.Dispose();
            _priorityQueue?.Dispose();
            _fileQueue?.Dispose();
            _commandQueue?.Dispose();
        }

        public void AddData(ISensor sender, SensorValueBase data)
        {
            HandleEnqueueResult(sender, EnqueueGated(_dataQueue, data), _dataQueue.QueueName);
        }

        public void AddData(ISensor sender, IEnumerable<SensorValueBase> items)
        {
            HandleEnqueueResult(sender, EnqueueGated(_dataQueue, items), _dataQueue.QueueName);
        }

        public void AddPriorityData(ISensor sender, SensorValueBase data)
        {
            HandleEnqueueResult(sender, EnqueueGated(_priorityQueue, data), _priorityQueue.QueueName);
        }

        public void AddPriorityData(ISensor sender, IEnumerable<SensorValueBase> items)
        {
            HandleEnqueueResult(sender, EnqueueGated(_priorityQueue, items), _priorityQueue.QueueName);
        }

        public void AddCommand(ISensor sender, CommandRequestBase command)
        {
            HandleEnqueueResult(sender, EnqueueGated(_commandQueue, command), _commandQueue.QueueName);
        }

        public void AddCommand(ISensor sender, IEnumerable<CommandRequestBase> commands)
        {
            HandleEnqueueResult(sender, EnqueueGated(_commandQueue, commands), _commandQueue.QueueName);
        }

        public void AddFile(ISensor sender, FileSensorValue file)
        {
            HandleEnqueueResult(sender, EnqueueGated(_fileQueue, file), _fileQueue.QueueName);
        }

        // Wraps each queue's typed Enqueue with the collector-wide lifecycle gate. When the
        // collector is not in an accepting state we surface RejectedCollectorNotAcceptingData
        // so the caller can distinguish a lifecycle reject from a queue-stopped reject — the
        // public AddXxx surface stays void, but internal tests/diagnostics now see the right
        // status instead of an opaque early return.
        private EnqueueResult EnqueueGated<TItem>(QueueProcessorBase<TItem> queue, TItem item)
        {
            if (!_lifecycle.CanAcceptData)
                return EnqueueResult.RejectedNotAccepting();

            return queue.Enqueue(item);
        }

        private EnqueueResult EnqueueGated<TItem>(QueueProcessorBase<TItem> queue, IEnumerable<TItem> items)
        {
            if (!_lifecycle.CanAcceptData)
                return EnqueueResult.RejectedNotAccepting();

            return queue.Enqueue(items);
        }

        public void AddException(string sensorPath, Exception ex)
        {
            var msg = $"Sensor: {sensorPath}, {ex}";
            _messageDeduplicator.AddMessage(msg);
        }

        /// <summary>
        /// Deduplicated error logging for the queue processing/flush loops. Retry-forever on a
        /// failure that repeats (e.g. server returns 4xx for a poison value) would otherwise
        /// produce one log entry per <see cref="CollectorOptions.PackageCollectPeriod"/> cycle;
        /// routing the identical error through <see cref="MessageDeduplicator"/> collapses the
        /// flood to one entry per dedup window while preserving the per-cycle retry behavior
        /// (which the ShutdownMode/PreserveCanceledPackages policy still relies on).
        /// </summary>
        public void AddQueueLoopError(string queueName, Exception ex)
        {
            // A package the sender failed is described by its report alone. Its stack trace
            // says nothing about the cause and differs between the first pass of the loop and
            // the later ones, and its item counts change from cycle to cycle; either in the key
            // made several entries of one failure (#1659 A4). The counts stay at Debug.
            string msg;

            if (ex is PackageSendFailedException failed)
            {
                msg = $"Queue: {queueName}, {failed.Report}";
                _logger.Debug($"Queue: {queueName}, {failed.Message}");
            }
            else
                msg = $"Queue: {queueName}, {ex}";

            _messageDeduplicator.AddMessage(msg);
        }

        /// <summary>
        /// A collector fault that is not tied to one sensor or queue (a failed send, a failed
        /// sample of a built-in source). Deduplicated like every other error: logged and posted
        /// to <c>Collector errors</c> once per window, with the repeat count when the window ends.
        /// </summary>
        public void AddError(string message)
        {
            _messageDeduplicator.AddMessage(message);
        }

        /// <summary>
        /// A package the server refused (4xx) on a queue that does not send a refusal again
        /// (#1659 A2). The items are gone, so this is reported like any other loss: an error in
        /// the log and in the <c>Collector errors</c> sensor, deduplicated so a host that keeps
        /// registering against a rejected key does not flood either (a repeat of the same text
        /// is counted and reported as "N times" when its window ends).
        /// </summary>
        public void ReportRejectedPackage(string queueName, string items, PackageSendingInfo info)
        {
            _messageDeduplicator.AddMessage(BuildRejectionMessage(queueName, items, info));
        }

        internal static string BuildRejectionMessage(string queueName, string items, PackageSendingInfo info) =>
            $"Queue: {queueName}, the server rejected {items} with status {info.StatusCode}; not sent again. {info.Error}";

        /// <summary>
        /// Diagnostic hook for callers that silently dropped a value because it failed validation
        /// (NaN/Infinity, null, status out of range, partial-bar stats outside [min, max], etc.).
        /// Emits at Debug level so producers do not flood the log when a noisy upstream keeps
        /// sending bad values; users who want visibility into dropped values enable Debug on
        /// their <see cref="HSMDataCollector.Logging.ICollectorLogger"/>.
        /// </summary>
        public void LogDroppedValue(string sensorPath, string reason)
        {
            _logger.Debug($"Sensor: {sensorPath}, value rejected: {reason}");
        }

        public void AddPackageInfo(string name, PackageInfo info)
        {
            if (Volatile.Read(ref _diagnosticsSuppressedFlag) == 1)
                return;

            if (info.ValuesCount != 0)
            {
                DefaultSensors.PackageProcessTimeSensor?.AddValue(name, info);
                DefaultSensors.PackageDataCountSensor?.AddValue(name, info);
            }
        }

        public void AddPackageSendingInfo(PackageSendingInfo info)
        {
            if (Volatile.Read(ref _diagnosticsSuppressedFlag) == 1)
                return;

            if (info.ContentSize != default)
                DefaultSensors.PackageSizeSensor?.AddValue(info);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void HandleEnqueueResult(ISensor sender, EnqueueResult result, string queueName)
        {
            // Rejected results are intentionally silent at this layer — CanAcceptData has already
            // gated the typical case, and a late callback that races a queue stop is logged via the
            // overflow sensor only if it actually evicted older items. The sender check breaks the
            // QueueOverflowSensor self-loop (its own AddValue lands in the data queue and could
            // re-trigger overflow reporting).
            if (!(sender is QueueOverflowSensor))
                EmitOverflowDrops(queueName, result.DroppedCount);
        }

        /// <summary>
        /// Issue #1088: surface evictions from the retry path. When a failed-retry item is dropped
        /// because the queue is at <see cref="CollectorOptions.MaxQueueSize"/>, the public
        /// AddXxx → HandleEnqueueResult path does not see it (re-enqueue is internal to the queue
        /// processor). Route it through the same QueueOverflowSensor so a sustained outage shows
        /// the lost-payload count instead of silently discarding old retries.
        ///
        /// Note: NOT gated on <see cref="_diagnosticsSuppressedFlag"/>. The field's own contract
        /// (see comment at declaration) explicitly carves overflow telemetry OUT of suppression —
        /// overflow IS data loss, and silencing it during the stop-flush window is exactly when
        /// the operator most needs the count. QueueOverflowSensor.AddValue is in-memory bar
        /// accumulation (no enqueue-back hazard at this layer), so it is safe past the boundary.
        /// </summary>
        public void ReportRequeueEviction(string queueName, int droppedCount) =>
            EmitOverflowDrops(queueName, droppedCount);

        private void EmitOverflowDrops(string queueName, int droppedCount)
        {
            if (droppedCount <= 0)
                return;

            var sensor = DefaultSensors.QueueOverflowSensor;

            // Without the sensor the drops have nowhere to be counted; they are kept for the
            // stop report so the loss is never silent (#1659 A5, rule "no silent data loss").
            if (sensor != null)
                sensor.AddValue(queueName, droppedCount);
            else
                Interlocked.Add(ref _unreportedOverflow, droppedCount);
        }

        // Values evicted from a full queue while no Queue overflow sensor was registered.
        private long _unreportedOverflow;

        /// <summary>
        /// One line per run for the drops no sensor counted, at Error: it is the only report of
        /// that loss. Same wording as the native collector's stop line. Taken with an exchange,
        /// so a later run does not report this run's drops again.
        /// </summary>
        private void ReportUnreportedOverflow()
        {
            var lost = Interlocked.Exchange(ref _unreportedOverflow, 0);

            if (lost > 0)
                _logger.Error($"Collector stop: {lost} value(s) dropped from the full send queue during this run (Queue overflow sensor not registered).");
        }

        private void LogDiscardedItems(int count, string queueName)
        {
            // Info, not Error: discarding still-buffered values on a graceful shutdown is expected and
            // contracted (a stop must not block on a dead/slow transport) — it is not a fault.
            if (count > 0)
                _logger.Info($"{queueName} queue discarded {count} item(s) during collector shutdown.");
        }

        private void RollbackStartedQueues(bool dataStarted, bool priorityStarted, bool fileStarted, bool commandStarted)
        {
            if (commandStarted)
                _commandQueue.StopAsync(ShutdownMode.StartRollback).ConfigureAwait(false).GetAwaiter().GetResult();

            if (fileStarted)
                _fileQueue.StopAsync(ShutdownMode.StartRollback).ConfigureAwait(false).GetAwaiter().GetResult();

            if (priorityStarted)
                _priorityQueue.StopAsync(ShutdownMode.StartRollback).ConfigureAwait(false).GetAwaiter().GetResult();

            if (dataStarted)
                _dataQueue.StopAsync(ShutdownMode.StartRollback).ConfigureAwait(false).GetAwaiter().GetResult();
        }

    }
}
