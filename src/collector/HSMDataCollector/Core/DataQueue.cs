using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using HSMDataCollector.Extensions;

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
        
        
        public bool Disposed { get; private set; }
        

        public event Action<List<SensorValueBase>> SendValues;
        public event Action<FileSensorValue> FileReceiving;


        public DataQueue(CollectorOptions options)
        {
            _maxQueueSize = options.MaxQueueSize;
            _maxValuesInPackage = options.MaxValuesInPackage;
            _packageSendingPeriod = options.PackageSendingPeriod;
        }


        public void ReturnData(List<SensorValueBase> values) 
        {
            foreach (var sensorValueBase in values) 
                Enqueue(_failedQueue, sensorValueBase);
        }

        public List<SensorValueBase> DequeueData()
        {
            var dataList = new List<SensorValueBase>(1 << 3);
            
            Dequeue(_failedQueue, dataList);
            Dequeue(_valuesQueue, dataList);
            
            return dataList;
        }

        public void ReturnSensorValue(SensorValueBase value) => Enqueue(_failedQueue, value);
        
        public void Enqueue(SensorValueBase value) => Enqueue(_valuesQueue, value.TrimLongComment());
        
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


        private void OnTimerTick(object state) => SendValues?.Invoke(DequeueData());
        
        private void Enqueue(ConcurrentQueue<SensorValueBase> queue, SensorValueBase value)
        {
            queue.Enqueue(value);

            while (queue.Count > _maxQueueSize)
                queue.TryDequeue(out _);
        }

        private void Dequeue(ConcurrentQueue<SensorValueBase> queue, List<SensorValueBase> dataList)
        {
            while (dataList.Count <= _maxValuesInPackage && queue.TryDequeue(out var value))
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
    }
}