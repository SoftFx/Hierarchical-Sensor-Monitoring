using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Threading;

namespace HSMDataCollector.Core
{
    internal sealed class DataQueue : IDataQueue, IValuesQueue
    {
        private const int MaxSensorValueStringLength = 1024;
        private const int MaxValuesMessageCapacity = 1000;
        private const int MaxQueueCapacity = 10000;

        private readonly Queue<SensorValueBase> _valuesQueue;
        private readonly List<SensorValueBase> _failedList;
        private readonly object _lockObj;
        private readonly object _listLock;

        private int _internalCount = 0;
        private Timer _sendTimer;
        private bool _hasFailedData;

        public bool Disposed { get; private set; }


        public event EventHandler<List<SensorValueBase>> SendValues;
        public event EventHandler<DateTime> QueueOverflow;
        public event EventHandler<FileSensorValue> FileReceving;


        public DataQueue()
        {
            _valuesQueue = new Queue<SensorValueBase>();
            _failedList = new List<SensorValueBase>();
            _lockObj = new object();
            _listLock = new object();
            Disposed = false;
        }


        public void ReturnData(List<SensorValueBase> values)
        {
            lock (_listLock)
            {
                _failedList.AddRange(values);
            }

            _hasFailedData = true;
        }

        public void ReturnFile(FileSensorValue file)
        {
            lock (_listLock)
            {
                _failedList.Add(file);
            }

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
            TimeSpan timerTime = TimeSpan.FromSeconds(15);
            _sendTimer = new Timer(OnTimerTick, null, timerTime, timerTime);
        }

        public void Stop()
        {
            _sendTimer?.Dispose();
            Disposed = true;
        }

        public void Clear()
        {
            ClearData();
        }

        private void Enqueue(SensorValueBase value)
        {
            lock (_lockObj)
            {
                _valuesQueue.Enqueue(value);
            }

            ++_internalCount;
            if (_internalCount == MaxQueueCapacity)
                OnQueueOverflow();
        }

        public void EnqueueData(SensorValueBase value)
        {
            TrimDataIfNecessary(value);
            Enqueue(value);
        }

        private void OnTimerTick(object state)
        {
            var data = DequeueData();
            OnSendValues(data);
        }

        private void ClearData()
        {
            if (_hasFailedData)
            {
                lock (_listLock)
                {
                    _failedList.Clear();
                }
            }

            lock (_lockObj)
            {
                _valuesQueue.Clear();
            }
        }

        private List<SensorValueBase> DequeueData()
        {
            var dataList = new List<SensorValueBase>(1 << 3);

            if (_hasFailedData)
            {
                lock (_listLock)
                {
                    foreach (var failedValue in _failedList)
                    {
                        switch (failedValue)
                        {
                            case FileSensorValue fileValue:
                                FileReceving?.Invoke(this, fileValue);
                                break;

                            case BarSensorValueBase barSensor when barSensor.Count == 0:
                                break;

                            default:
                                dataList.Add(failedValue);
                                break;
                        }
                    }

                    _failedList.Clear();
                }

                _hasFailedData = false;
            }

            int count = 0;
            lock (_lockObj)
            {
                while (count < MaxValuesMessageCapacity && _internalCount > 0)
                {
                    var value = _valuesQueue.Dequeue();
                    switch (value)
                    {
                        case FileSensorValue fileValue:
                            FileReceving?.Invoke(this, fileValue);
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
            }

            return dataList;
        }

        private void OnQueueOverflow()
        {
            QueueOverflow?.Invoke(this, DateTime.Now);
        }

        private void OnSendValues(List<SensorValueBase> values)
        {
            SendValues?.Invoke(this, values);
        }

        private static void TrimDataIfNecessary(SensorValueBase value)
        {
            if (value?.Comment != null && value.Comment.Length > MaxSensorValueStringLength)
            {
                value.Comment = value.Comment.Substring(0, MaxSensorValueStringLength);
            }
        }
    }
}
