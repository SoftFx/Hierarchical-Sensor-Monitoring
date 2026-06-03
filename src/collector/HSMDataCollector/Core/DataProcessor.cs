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

        private DefaultSensorsCollection DefaultSensors => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? (DefaultSensorsCollection)SensorStorage.Windows : (DefaultSensorsCollection)SensorStorage.Unix;

        internal SensorsStorage SensorStorage { get; }

        /// <summary>
        /// Per-collector scheduler used by sensors and the message deduplicator. Owned by the
        /// outer <see cref="DataCollector"/>; disposed there.
        /// </summary>
        internal ICollectorScheduler Scheduler { get; }

        internal bool CanStartNewSensors => _lifecycle.CanStartNewSensors;

        internal bool CanRegisterSensors => _lifecycle.CanRegisterSensors;

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
            var dataStarted = _dataQueue.Start();
            var priorityStarted = dataStarted && _priorityQueue.Start();
            var fileStarted = priorityStarted && _fileQueue.Start();
            var commandStarted = fileStarted && _commandQueue.Start();

            if (!commandStarted)
            {
                StopStartedQueues(dataStarted, priorityStarted, fileStarted, commandStarted);
                return false;
            }

            return true;
        }

        public async Task InitAsync()
        {
            await SensorStorage.InitAsync().ConfigureAwait(false);
            await SensorStorage.StartAsync().ConfigureAwait(false);
        }

        public async Task StopAsync()
        {
            // Collect failures across phases so a single phase exception does not leave background queues running.
            // After all phases attempt to stop, rethrow as AggregateException so the caller knows the stop was degraded.
            var failures = new List<Exception>();

            await TryStopPhase(() => SensorStorage.WaitForDynamicStartTasksAsync(), failures).ConfigureAwait(false);
            await TryStopPhase(() => SensorStorage.StopAsync(), failures).ConfigureAwait(false);

            var dataQueueStopped = false;
            var priorityQueueStopped = false;

            await TryStopPhase(async () =>
            {
                dataQueueStopped = await _dataQueue.StopAsync(clearQueue: false).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);

            await TryStopPhase(async () =>
            {
                priorityQueueStopped = await _priorityQueue.StopAsync(clearQueue: false).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);

            if (priorityQueueStopped)
            {
                await TryStopPhase(async () =>
                {
                    using (var flushCancellation = new CancellationTokenSource(_stopFlushTimeout))
                        await _priorityQueue.FlushAsync(flushCancellation.Token).ConfigureAwait(false);

                    LogDiscardedItems(_priorityQueue.ClearQueue(), _priorityQueue.QueueName);
                }, failures).ConfigureAwait(false);
            }

            if (dataQueueStopped)
            {
                await TryStopPhase(async () =>
                {
                    using (var flushCancellation = new CancellationTokenSource(_stopFlushTimeout))
                        await _dataQueue.FlushAsync(flushCancellation.Token).ConfigureAwait(false);

                    LogDiscardedItems(_dataQueue.ClearQueue(), _dataQueue.QueueName);
                }, failures).ConfigureAwait(false);
            }

            await TryStopPhase(() => _fileQueue.StopAsync().AsTask(), failures).ConfigureAwait(false);
            await TryStopPhase(() => _commandQueue.StopAsync().AsTask(), failures).ConfigureAwait(false);

            if (failures.Count > 0)
                throw new AggregateException("One or more phases of DataProcessor.StopAsync failed; remaining phases completed.", failures);
        }

        private async Task TryStopPhase(Func<Task> phase, List<Exception> failures)
        {
            try
            {
                await phase().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error($"DataProcessor stop phase failed: {ex}");
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
            if (!_lifecycle.CanAcceptData)
                return;

            SendQueueOverflow(sender, _dataQueue.Enqueue(data), _dataQueue.QueueName);
        }

        public void AddData(ISensor sender, IEnumerable<SensorValueBase> items)
        {
            if (!_lifecycle.CanAcceptData)
                return;

            SendQueueOverflow(sender, _dataQueue.Enqueue(items), _dataQueue.QueueName);
        }

        public void AddPriorityData(ISensor sender, SensorValueBase data)
        {
            if (!_lifecycle.CanAcceptData)
                return;

            SendQueueOverflow(sender, _priorityQueue.Enqueue(data), _priorityQueue.QueueName);
        }

        public void AddPriorityData(ISensor sender, IEnumerable<SensorValueBase> items)
        {
            if (!_lifecycle.CanAcceptData)
                return;

            SendQueueOverflow(sender, _priorityQueue.Enqueue(items), _priorityQueue.QueueName);
        }

        public void AddCommand(ISensor sender, CommandRequestBase command)
        {
            if (!_lifecycle.CanAcceptData)
                return;

            SendQueueOverflow(sender, _commandQueue.Enqueue(command), _commandQueue.QueueName);
        }

        public void AddCommand(ISensor sender, IEnumerable<CommandRequestBase> commands)
        {
            if (!_lifecycle.CanAcceptData)
                return;

            SendQueueOverflow(sender, _commandQueue.Enqueue(commands), _commandQueue.QueueName);
        }

        public void AddFile(ISensor sender, FileSensorValue file)
        {
            if (!_lifecycle.CanAcceptData)
                return;

            SendQueueOverflow(sender, _fileQueue.Enqueue(file), _fileQueue.QueueName);
        }

        public void AddException(string sensorPath, Exception ex)
        {
            var msg = $"Sensor: {sensorPath}, {ex}";
            _messageDeduplicator.AddMessage(msg);
        }

        public void AddPackageInfo(string name, PackageInfo info)
        {
            if (info.ValuesCount != 0)
            {
                DefaultSensors.PackageProcessTimeSensor?.AddValue(name, info);
                DefaultSensors.PackageDataCountSensor?.AddValue(name, info);
            }
        }

        public void AddPackageSendingInfo(PackageSendingInfo info)
        {
            if (info.ContentSize != default)
                DefaultSensors.PackageSizeSensor?.AddValue(info);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SendQueueOverflow(ISensor sender, int overflow, string queueName)
        {
            if (sender is QueueOverflowSensor)
                return;

            if (overflow > 0)
                DefaultSensors.QueueOverflowSensor?.AddValue(queueName, overflow);
        }

        private void LogDiscardedItems(int count, string queueName)
        {
            if (count > 0)
                _logger.Error($"{queueName} queue discarded {count} item(s) during collector shutdown.");
        }

        private void StopStartedQueues(bool dataStarted, bool priorityStarted, bool fileStarted, bool commandStarted)
        {
            if (commandStarted)
                _commandQueue.StopAsync(clearQueue: false).ConfigureAwait(false).GetAwaiter().GetResult();

            if (fileStarted)
                _fileQueue.StopAsync(clearQueue: false).ConfigureAwait(false).GetAwaiter().GetResult();

            if (priorityStarted)
                _priorityQueue.StopAsync(clearQueue: false).ConfigureAwait(false).GetAwaiter().GetResult();

            if (dataStarted)
                _dataQueue.StopAsync(clearQueue: false).ConfigureAwait(false).GetAwaiter().GetResult();
        }

    }
}
