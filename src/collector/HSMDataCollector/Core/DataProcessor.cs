using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.DefaultSensors.Diagnostic;
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

        private DefaultSensorsCollection DefaultSensors => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? (DefaultSensorsCollection)SensorStorage.Windows : (DefaultSensorsCollection)SensorStorage.Unix;

        internal SensorsStorage SensorStorage { get; }

        public bool IsStarted { get; private set; } = false;

        public DataProcessor(CollectorOptions options, LoggerManager logger)
        {
            _logger = logger;

            SensorStorage  = new SensorsStorage(options, this, logger);

            _dataQueue     = new DataQueueProcessor(options, this, logger);
            _priorityQueue = new PriorityDataQueueProcessor(options, this, logger);
            _fileQueue     = new FileQueueProcessor(options, this, logger);
            _commandQueue  = new CommandQueueProcessor(options, this, logger);
        }


        public void Start()
        {
            IsStarted = true;
            _dataQueue.Start();
            _priorityQueue.Start();
            _fileQueue.Start();
            _commandQueue.Start();
        }

        public async Task InitAsync()
        {
            await SensorStorage.InitAsync().ConfigureAwait(false);
            await SensorStorage.StartAsync().ConfigureAwait(false);
        }

        public async Task StopAsync()
        {
            IsStarted = false;
            await _dataQueue.StopAsync().ConfigureAwait(false);
            await _priorityQueue.StopAsync().ConfigureAwait(false);
            await _fileQueue.StopAsync().ConfigureAwait(false);
            await _commandQueue.StopAsync().ConfigureAwait(false);
            await SensorStorage.StopAsync().ConfigureAwait(false);
        }

        public void Dispose()
        {
            _dataQueue?.Dispose();
            _priorityQueue?.Dispose();
            _fileQueue?.Dispose();
            _commandQueue?.Dispose();
            SensorStorage?.Dispose();
        }

        public void AddData(SensorBase sender, SensorValueBase data) => SendQueueOverflow(sender, _dataQueue.Enqeue(data), _dataQueue.QueueName);

        public void AddData(SensorBase sender, IEnumerable<SensorValueBase> items) => SendQueueOverflow(sender, _dataQueue.Enqeue(items), _dataQueue.QueueName);

        public void AddPriorityData(SensorBase sender, SensorValueBase data) => SendQueueOverflow(sender, _priorityQueue.Enqeue(data), _dataQueue.QueueName);

        public void AddPriorityData(SensorBase sender, IEnumerable<SensorValueBase> items) => SendQueueOverflow(sender, _priorityQueue.Enqeue(items), _dataQueue.QueueName);

        public void AddCommand(SensorBase sender, CommandRequestBase command) => SendQueueOverflow(sender, _commandQueue.Enqeue(command), _dataQueue.QueueName);

        public void AddCommand(SensorBase sender, IEnumerable<CommandRequestBase> commands) => SendQueueOverflow(sender, _commandQueue.Enqeue(commands), _dataQueue.QueueName);

        public void AddFile(SensorBase sender, FileSensorValue file) => SendQueueOverflow(sender, _fileQueue.Enqeue(file), _dataQueue.QueueName);

        public void AddException(string sensorPath, Exception ex)
        {
            var msg = $"Sensor: {sensorPath}, {ex}";
            _logger.Error(msg);
            DefaultSensors?.CollectorErrors?.SendCollectorError(msg);
        }

        public void AddPackageInfo(string name, PackageInfo info)
        {
            DefaultSensors.PackageProcessTimeSensor?.AddValue(name, info);
            DefaultSensors.PackageDataCountSensor?.AddValue(name, info);
        }

        public void AddPackageSendingInfo(PackageSendingInfo info)
        {
            DefaultSensors.PackageSizeSensor?.AddValue(info);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SendQueueOverflow(SensorBase sender, int overflow, string queueName)
        {
            if (sender is QueueOverflowSensor)
                return;

            if (overflow > 0)
                DefaultSensors.QueueOverflowSensor?.AddValue(queueName, overflow);
        }

    }
}
