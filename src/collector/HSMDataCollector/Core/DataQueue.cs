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
        private readonly TimeSpan _packageSendingPeriod;
        private readonly int _maxQueueSize;
        private readonly int _maxValuesInPackage;

        private readonly ConcurrentQueue<SensorValueBase> _valuesQueue;
        private readonly ConcurrentQueue<SensorValueBase> _failedList;

        private Timer _sendTimer;
        private int _internalCount = 0;
        private bool _hasFailedData;

        public bool Disposed { get; private set; }


        public event Action<List<SensorValueBase>> SendValues;
        public event Action<FileSensorValue> FileReceiving;


        public DataQueue(CollectorOptions options)
        {
            _maxQueueSize = options.MaxQueueSize;
            _maxValuesInPackage = options.MaxValuesInPackage;
            _packageSendingPeriod = options.PackageSendingPeriod;

            _valuesQueue = new ConcurrentQueue<SensorValueBase>();
            _failedList = new ConcurrentQueue<SensorValueBase>();

            Disposed = false;
        }


        public void ReturnData(List<SensorValueBase> values)
        {
            foreach (var sensorValueBase in values)
                _failedList.Enqueue(sensorValueBase);

            _hasFailedData = true;
        }

        public void ReturnFile(FileSensorValue file)
        {
            _failedList.Enqueue(file);

            _hasFailedData = true;
        }

        public List<SensorValueBase> GetCollectedData()
        {
            var values = new List<SensorValueBase>(1 << 3);

            if (_hasFailedData)
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

            ++_internalCount;
            if (_internalCount == _maxQueueSize)
                DequeueData();
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

            if (_hasFailedData)
            {
                while (_failedList.TryDequeue(out var failedValue))
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

            _hasFailedData = false;


            int count = 0;
            while (count < _maxValuesInPackage && _internalCount > 0)
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
                        ++count;
                        break;
                }

                --_internalCount;
            }

            return dataList;
        }

        private void OnSendValues(List<SensorValueBase> values) => SendValues?.Invoke(values);
    }
}