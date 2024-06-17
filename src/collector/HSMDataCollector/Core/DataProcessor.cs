using System;
using System.Collections.Generic;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue.SpecificQueue;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.Core
{
    internal class DataProcessor : IDataProcessor, IDisposable
    {
        private DataQueueProcessor _dataQueue;
        private PriorityDataQueueProcessor _priorityQueue;
        private FileQueueProcessor _fileQueue;
        private CommandQueueProcessor _commandQueue;
        private readonly CollectorOptions _options;
        private ICollectorLogger _logger;

        public DataProcessor(CollectorOptions options, ICollectorLogger logger)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));

            _logger = logger;

            _dataQueue     = new DataQueueProcessor(options);
            _priorityQueue = new PriorityDataQueueProcessor(options);
            _fileQueue     = new FileQueueProcessor(options);
            _commandQueue  = new CommandQueueProcessor(options);
        }

        public void Dispose()
        {
            _dataQueue?.Dispose();
            _priorityQueue?.Dispose();
            _fileQueue?.Dispose();
            _commandQueue?.Dispose();
        }

        public void AddData(SensorValueBase data) => _dataQueue.Enqeue(data);

        public void AddData(IEnumerable<SensorValueBase> items) => _dataQueue.Enqeue(items);

        public void AddPriorityData(SensorValueBase data) => _priorityQueue.Enqeue(data);

        public void AddPriorityData(IEnumerable<SensorValueBase> items) => _priorityQueue.Enqeue(items);

        public void AddCommand(CommandRequestBase command) => _commandQueue.Enqeue(command);

        public void AddCommand(IEnumerable<CommandRequestBase> commands) => _commandQueue.Enqeue(commands);

        public void AddFile(FileSensorValue file) => _fileQueue.Enqeue(file);

        public void AddException(string sensorPath, Exception ex)
        {
            _logger.Error($"Sensor: {sensorPath}, {ex}");
        }
    }
}
