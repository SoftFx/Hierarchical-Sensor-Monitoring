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

        private readonly TimeSpan _packageCollectPeriod;
        private readonly int _maxQueueSize;
        private readonly int _maxValuesInPackage;

        private Timer _sendTimer;


        public event Action<List<SensorValueBase>> NewValuesEvent;
        public event Action<FileSensorValue> NewValueEvent;


        public DataQueue(CollectorOptions options)
        {
            _maxQueueSize = options.MaxQueueSize;
            _maxValuesInPackage = options.MaxValuesInPackage;
            _packageCollectPeriod = options.PackageCollectPeriod;
        }


        public void Init()
        {
            if (_sendTimer == null)
                _sendTimer = new Timer((_) => Flush(), null, _packageCollectPeriod, _packageCollectPeriod);
        }

        public void Stop()
        {
            Flush();

            _sendTimer?.Dispose();
            _sendTimer = null;
        }

        public void Flush() => NewValuesEvent?.Invoke(DequeueAllData());


        public void Push(SensorValueBase value) => Enqueue(_valuesQueue, value.TrimLongComment());

        public void PushFailValue(SensorValueBase value) => Enqueue(_failedQueue, value);


        private void Enqueue(ConcurrentQueue<SensorValueBase> queue, SensorValueBase value)
        {
            if (_sendTimer == null)
                return;

            queue.Enqueue(value);

            while (queue.Count > _maxQueueSize)
                queue.TryDequeue(out _);
        }

        private List<SensorValueBase> Dequeue(ConcurrentQueue<SensorValueBase> queue, List<SensorValueBase> dataList)
        {
            while (dataList.Count < _maxValuesInPackage && queue.TryDequeue(out var value))
                switch (value)
                {
                    case FileSensorValue fileValue:
                        NewValueEvent?.Invoke(fileValue);
                        break;

                    case BarSensorValueBase barSensor when barSensor.Count == 0:
                        break;

                    default:
                        dataList.Add(value);
                        break;
                }

            return dataList;
        }

        private List<SensorValueBase> DequeueAllData()
        {
            var dataList = new List<SensorValueBase>(1 << 3);

            Dequeue(_failedQueue, dataList);

            return Dequeue(_valuesQueue, dataList);
        }
    }
}