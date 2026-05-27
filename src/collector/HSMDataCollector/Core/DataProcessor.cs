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
        private readonly TimeSpan _stopFlushTimeout;

        private DefaultSensorsCollection DefaultSensors => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? (DefaultSensorsCollection)SensorStorage.Windows : (DefaultSensorsCollection)SensorStorage.Unix;

        internal SensorsStorage SensorStorage { get; }

        private int _isStarted;
        private int _isStopping;

        public bool IsStarted => Volatile.Read(ref _isStarted) == 1;

        internal bool CanStartNewSensors => IsStarted && Volatile.Read(ref _isStopping) == 0;

        public DataProcessor(CollectorOptions options, LoggerManager logger)
        {
            _logger = logger;
            _stopFlushTimeout = TimeSpan.FromTicks(Math.Min(Math.Max(options.RequestTimeout.Ticks, TimeSpan.FromSeconds(1).Ticks),
                                                            TimeSpan.FromSeconds(5).Ticks));

            SensorStorage  = new SensorsStorage(options, this, logger);

            _dataQueue     = new DataQueueProcessor(options, this, logger);
            _priorityQueue = new PriorityDataQueueProcessor(options, this, logger);
            _fileQueue     = new FileQueueProcessor(options, this, logger);
            _commandQueue  = new CommandQueueProcessor(options, this, logger);
            _messageDeduplicator = new MessageDeduplicator((msg) => { _logger.Error(msg);
                                                                      DefaultSensors?.CollectorErrors?.SendCollectorError(msg);
                                                                    }, options.ExceptionDeduplicatorWindow, options.MaxDeduplicatedMessages);
        }


        public bool Start()
        {
            if (IsStarted)
                return true;

            Volatile.Write(ref _isStopping, 0);

            var dataStarted = _dataQueue.Start();
            var priorityStarted = dataStarted && _priorityQueue.Start();
            var fileStarted = priorityStarted && _fileQueue.Start();
            var commandStarted = fileStarted && _commandQueue.Start();

            if (!commandStarted)
            {
                StopStartedQueues(dataStarted, priorityStarted, fileStarted, commandStarted);
                return false;
            }

            Volatile.Write(ref _isStarted, 1);
            return true;
        }

        public async Task InitAsync()
        {
            await SensorStorage.InitAsync().ConfigureAwait(false);
            await SensorStorage.StartAsync().ConfigureAwait(false);
        }

        public async Task StopAsync()
        {
            Volatile.Write(ref _isStopping, 1);
            try
            {
                await SensorStorage.StopAsync().ConfigureAwait(false);
                Volatile.Write(ref _isStarted, 0);

                var dataQueueStopped = await _dataQueue.StopAsync(clearQueue: false).ConfigureAwait(false);
                var priorityQueueStopped = await _priorityQueue.StopAsync(clearQueue: false).ConfigureAwait(false);

                if (priorityQueueStopped)
                {
                    using (var flushCancellation = new CancellationTokenSource(_stopFlushTimeout))
                        await _priorityQueue.FlushAsync(flushCancellation.Token).ConfigureAwait(false);

                    LogDiscardedItems(_priorityQueue.ClearQueue(), _priorityQueue.QueueName);
                }

                if (dataQueueStopped)
                {
                    using (var flushCancellation = new CancellationTokenSource(_stopFlushTimeout))
                        await _dataQueue.FlushAsync(flushCancellation.Token).ConfigureAwait(false);

                    LogDiscardedItems(_dataQueue.ClearQueue(), _dataQueue.QueueName);
                }

                await _fileQueue.StopAsync().ConfigureAwait(false);
                await _commandQueue.StopAsync().ConfigureAwait(false);
            }
            finally
            {
                Volatile.Write(ref _isStarted, 0);
                Volatile.Write(ref _isStopping, 0);
            }
        }

        public void Dispose()
        {
            _messageDeduplicator?.Dispose();
            _dataQueue?.Dispose();
            _priorityQueue?.Dispose();
            _fileQueue?.Dispose();
            _commandQueue?.Dispose();
            SensorStorage?.Dispose();
        }

        public void AddData(ISensor sender, SensorValueBase data)
        {
            if (!IsStarted)
                return;

            SendQueueOverflow(sender, _dataQueue.Enqeue(data), _dataQueue.QueueName);
        }

        public void AddData(ISensor sender, IEnumerable<SensorValueBase> items)
        {
            if (!IsStarted)
                return;

            SendQueueOverflow(sender, _dataQueue.Enqeue(items), _dataQueue.QueueName);
        }

        public void AddPriorityData(ISensor sender, SensorValueBase data)
        {
            if (!IsStarted)
                return;

            SendQueueOverflow(sender, _priorityQueue.Enqeue(data), _priorityQueue.QueueName);
        }

        public void AddPriorityData(ISensor sender, IEnumerable<SensorValueBase> items)
        {
            if (!IsStarted)
                return;

            SendQueueOverflow(sender, _priorityQueue.Enqeue(items), _priorityQueue.QueueName);
        }

        public void AddCommand(ISensor sender, CommandRequestBase command)
        {
            if (!IsStarted)
                return;

            SendQueueOverflow(sender, _commandQueue.Enqeue(command), _commandQueue.QueueName);
        }

        public void AddCommand(ISensor sender, IEnumerable<CommandRequestBase> commands)
        {
            if (!IsStarted)
                return;

            SendQueueOverflow(sender, _commandQueue.Enqeue(commands), _commandQueue.QueueName);
        }

        public void AddFile(ISensor sender, FileSensorValue file)
        {
            if (!IsStarted)
                return;

            SendQueueOverflow(sender, _fileQueue.Enqeue(file), _fileQueue.QueueName);
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
