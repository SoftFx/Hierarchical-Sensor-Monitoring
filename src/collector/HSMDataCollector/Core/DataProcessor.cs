using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using HSMDataCollector.DefaultSensors;
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

        public void AddData(SensorValueBase data) => SendQueueOverflow(_dataQueue.Enqeue(data));

        public void AddData(IEnumerable<SensorValueBase> items) => SendQueueOverflow(_dataQueue.Enqeue(items));

        public void AddPriorityData(SensorValueBase data) => SendQueueOverflow(_priorityQueue.Enqeue(data));

        public void AddPriorityData(IEnumerable<SensorValueBase> items) => SendQueueOverflow(_priorityQueue.Enqeue(items));

        public void AddCommand(CommandRequestBase command) => SendQueueOverflow(_commandQueue.Enqeue(command));

        public void AddCommand(IEnumerable<CommandRequestBase> commands) => SendQueueOverflow(_commandQueue.Enqeue(commands));

        public void AddFile(FileSensorValue file) => SendQueueOverflow(_fileQueue.Enqeue(file));

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
        private void SendQueueOverflow(int overflow)
        {
            if (overflow > 0)
                DefaultSensors.QueueOverflowSensor?.AddValue(overflow);
        }

    }
}
