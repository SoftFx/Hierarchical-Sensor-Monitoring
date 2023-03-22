using HSMDataCollector.Extensions;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace HSMDataCollector.Core
{
    internal sealed class DataQueue : IDataQueue, IValuesQueue
    {
        private readonly ConcurrentQueue<SensorValueBase> _valuesQueue = new ConcurrentQueue<SensorValueBase>();
        private readonly ConcurrentQueue<SensorValueBase> _failedQueue = new ConcurrentQueue<SensorValueBase>();

        private readonly TimeSpan _packageSendingPeriod;
        private readonly int _maxQueueSize;
        private readonly int _maxValuesInPackage;
        
        private Timer _sendTimer;
        
        private bool HasFailedData => _failedQueue.Count > 0;
        
        
        public bool Disposed { get; private set; }
        

        public event Action<List<SensorValueBase>> SendValues;
        public event Action<FileSensorValue> FileReceiving;


        public DataQueue(CollectorOptions options)
        {
            _maxQueueSize = options.MaxQueueSize;
            _maxValuesInPackage = options.MaxValuesInPackage;
            _packageSendingPeriod = options.PackageSendingPeriod;

            Disposed = false;
        }


        public void ReturnData(List<SensorValueBase> values)
        {
            foreach (var sensorValueBase in values)
                _failedQueue.Enqueue(sensorValueBase);
        }

        public void ReturnFile(FileSensorValue file)
        {
            _failedQueue.Enqueue(file);
        }

        public List<SensorValueBase> GetCollectedData()
        {
            var values = new List<SensorValueBase>(1 << 3);

            if (HasFailedData)
                values.AddRange(DequeueData());

            values.AddRange(DequeueData());

            return values;
        }

        public void InitializeTimer()
        {
            if (_sendTimer == null)
                _sendTimer = new Timer(OnTimerTick, null, _packageSendingPeriod, _packageSendingPeriod);
        }

        public void Stop()
        {
            _sendTimer?.Dispose();
            Disposed = true;
        }

        private void Enqueue(SensorValueBase value)
        {
            _valuesQueue.Enqueue(value);

            if (_valuesQueue.Count == _maxQueueSize)
                while (_valuesQueue.Count > _maxQueueSize)
                    if (!_valuesQueue.TryDequeue(out var item))
                        break;
        }

        public void EnqueueData(SensorValueBase value)
        {
            value?.TrimLongComment();
            Enqueue(value);
        }

        private void OnTimerTick(object state)
        {
            var data = DequeueData();
            OnSendValues(data);
        }

        private List<SensorValueBase> DequeueData()
        {
            var dataList = new List<SensorValueBase>(1 << 3);

            if (HasFailedData)
            {
                while (_failedQueue.TryDequeue(out var failedValue))
                {
                    switch (failedValue)
                    {
                        case FileSensorValue fileValue:
                            FileReceiving?.Invoke(fileValue);
                            break;

                        case BarSensorValueBase barSensor when barSensor.Count == 0:
                            break;

                        default:
                            dataList.Add(failedValue);
                            break;
                    }   
                }
            }
            
            
            while (dataList.Count < _maxValuesInPackage && _valuesQueue.Count > 0)
            {
                _valuesQueue.TryDequeue(out var value);
                switch (value)
                {
                    case FileSensorValue fileValue:
                        FileReceiving?.Invoke(fileValue);
                        break;

                    case BarSensorValueBase barSensor when barSensor.Count == 0:
                        break;

                    default:
                        dataList.Add(value);
                        break;
                }
            }

            return dataList;
        }

        private void OnSendValues(List<SensorValueBase> values) => SendValues?.Invoke(values);
    }
}