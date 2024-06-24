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
    internal sealed class DataProcessor : IDataProcessor, IDisposable
    {
        private readonly DataQueueProcessor _dataQueue;
        private readonly PriorityDataQueueProcessor _priorityQueue;
        private readonly FileQueueProcessor _fileQueue;
        private readonly CommandQueueProcessor _commandQueue;
        private readonly LoggerManager _logger;

        private DefaultSensorsCollection DefaultSensors => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? (DefaultSensorsCollection)SensorStorage.Windows : (DefaultSensorsCollection)SensorStorage.Unix;

        internal SensorsStorage SensorStorage { get; }

        public DataProcessor(CollectorOptions options, LoggerManager logger)
        {
            _logger = logger;

            SensorStorage  = new SensorsStorage(options, this, logger);

            _dataQueue     = new DataQueueProcessor(options, this, logger);
            _priorityQueue = new PriorityDataQueueProcessor(options, this, logger);
            _fileQueue     = new FileQueueProcessor(options, this, logger);
            _commandQueue  = new CommandQueueProcessor(options, this, logger);
        }

        public async Task InitAsync()
        {
            _dataQueue.Init();
            _priorityQueue.Init();
            _fileQueue.Init();
            _commandQueue.Init();
            await SensorStorage.InitAsync().ConfigureAwait(false);
            await SensorStorage.StartAsync().ConfigureAwait(false);
        }

        public async Task StopAsync()
        {
            _dataQueue.Stop();
            _priorityQueue.Stop();
            _fileQueue.Stop();
            _commandQueue.Stop();
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
