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

        // 1 after the data/priority drain boundary has been crossed during a graceful stop. Late
        // file/command flushes that still produce package diagnostics observe this flag and skip
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
            // observe the right PreserveCanceledPackages policy (graceful re-enqueues canceled
            // packages; terminal dispose drops them so we stop bounded under broken transport).
            var dataStopped = false;
            var priorityStopped = false;
            var fileStopped = false;
            var commandStopped = false;

            await TryStopPhase(async () =>
            {
                dataStopped = await _dataQueue.StopAsync(mode).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);

            await TryStopPhase(async () =>
            {
                priorityStopped = await _priorityQueue.StopAsync(mode).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);

            await TryStopPhase(async () =>
            {
                fileStopped = await _fileQueue.StopAsync(mode).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);

            await TryStopPhase(async () =>
            {
                commandStopped = await _commandQueue.StopAsync(mode).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);

            if (priorityStopped)
                await FlushAndLogAsync(_priorityQueue, mode, failures).ConfigureAwait(false);

            if (dataStopped)
                await FlushAndLogAsync(_dataQueue, mode, failures).ConfigureAwait(false);

            // Self-diagnostics boundary (#1075). After the data/priority queues have drained their
            // accepted user data, any further diagnostic value generated by the file/command flushes
            // would have nowhere safe to land — the data queue is no longer accepting writes, and a
            // later restart would otherwise see stale stop-cycle telemetry as the first emitted values.
            Volatile.Write(ref _diagnosticsSuppressedFlag, 1);

            if (fileStopped)
                await FlushAndLogAsync(_fileQueue, mode, failures).ConfigureAwait(false);

            if (commandStopped)
                await FlushAndLogAsync(_commandQueue, mode, failures).ConfigureAwait(false);
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

        private async Task FlushAndLogAsync(IQueueProcessor queue, ShutdownMode mode, List<Exception> failures)
        {
            await TryStopPhase(async () =>
            {
                using (var flushCancellation = new CancellationTokenSource(GetFlushTimeout(mode)))
                    await queue.FlushAsync(flushCancellation.Token).ConfigureAwait(false);

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
        /// non-retryable failure (e.g. server returns 4xx for a poison value) would otherwise
        /// produce one log entry per <see cref="CollectorOptions.PackageCollectPeriod"/> cycle;
        /// routing the identical error through <see cref="MessageDeduplicator"/> collapses the
        /// flood to one entry per dedup window while preserving the per-cycle retry behavior
        /// (which the ShutdownMode/PreserveCanceledPackages policy still relies on).
        /// </summary>
        public void AddQueueLoopError(string queueName, Exception ex)
        {
            var msg = $"Queue: {queueName}, {ex}";
            _messageDeduplicator.AddMessage(msg);
        }

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
            // overflow sensor only if it actually evicted older items.
            if (result.DroppedCount > 0 && !(sender is QueueOverflowSensor))
                DefaultSensors.QueueOverflowSensor?.AddValue(queueName, result.DroppedCount);
        }

        /// <summary>
        /// Issue #1088: surface evictions from the retry path. When a failed-retry item is dropped
        /// because the queue is at <see cref="CollectorOptions.MaxQueueSize"/>, the public
        /// AddXxx → HandleEnqueueResult path does not see it (re-enqueue is internal to the queue
        /// processor). Route it through the same QueueOverflowSensor so a sustained outage shows
        /// the lost-payload count instead of silently discarding old retries.
        /// </summary>
        public void ReportRequeueEviction(string queueName, int droppedCount)
        {
            if (droppedCount <= 0)
                return;

            if (Volatile.Read(ref _diagnosticsSuppressedFlag) == 1)
                return;

            DefaultSensors.QueueOverflowSensor?.AddValue(queueName, droppedCount);
        }

        private void LogDiscardedItems(int count, string queueName)
        {
            if (count > 0)
                _logger.Error($"{queueName} queue discarded {count} item(s) during collector shutdown.");
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
