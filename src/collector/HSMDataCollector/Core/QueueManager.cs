using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.Logging;
using HSMDataCollector.SyncQueue.SpecificQueue;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.Core
{
    internal sealed class QueueManager : IQueueManager, IDisposable
    {
        private readonly DataQueueProcessor _dataQueue;
        private readonly PriorityDataQueueProcessor _priorityQueue;
        private readonly FileQueueProcessor _fileQueue;
        private readonly CommandQueueProcessor _commandQueue;
        private readonly ICollectorLogger _logger;

        private readonly DefaultSensorsCollection _defaultSensors;

        public QueueManager(CollectorOptions options, ICollectorLogger logger)
        {
            _logger = logger;

            _dataQueue     = new DataQueueProcessor(options);
            _priorityQueue = new PriorityDataQueueProcessor(options);
            _fileQueue     = new FileQueueProcessor(options);
            _commandQueue  = new CommandQueueProcessor(options);
        }

        public void Init()
        {
            _dataQueue.Init();
            _priorityQueue.Init();
            _fileQueue.Init();
            _commandQueue.Init();
        }

        public void Stop()
        {
            _dataQueue.Stop();
            _priorityQueue.Stop();
            _fileQueue.Stop();
            _commandQueue.Stop();
        }

        public void Dispose()
        {
            _dataQueue?.Dispose();
            _priorityQueue?.Dispose();
            _fileQueue?.Dispose();
            _commandQueue?.Dispose();
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
            _defaultSensors.CollectorErrors?.SendCollectorError(msg);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SendQueueOverflow(int overflow)
        {
            if (overflow > 0)
                _defaultSensors.QueueOverflowSensor?.AddValue(overflow);
        }

    }
}
