using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Threading;

namespace HSMDataCollector.Core
{
    internal class DataQueue : IDataQueue, IValuesQueue
    {
        private readonly Queue<SensorValueBase> _valuesQueue;
        private readonly List<SensorValueBase> _failedList;
        private const int MAX_VALUES_MESSAGE_CAPACITY = 1000;
        private const int MAX_QUEUE_CAPACITY = 10000;
        private int _internalCount = 0;
        private readonly object _lockObj;
        private readonly object _listLock;
        private Timer _sendTimer;
        private bool _hasFailedData;
        public bool Disposed { get; private set; }

        public DataQueue()
        {
            _valuesQueue = new Queue<SensorValueBase>();
            _failedList = new List<SensorValueBase>();
            _lockObj = new object();
            _listLock = new object();
            Disposed = false;
        }

        public event EventHandler<List<SensorValueBase>> SendValues;
        public event EventHandler<DateTime> QueueOverflow;
        public event EventHandler<FileSensorValue> FileReceving;

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
            var values = new List<SensorValueBase>();
            if (_hasFailedData)
            {
                values.AddRange(DequeueData());
            }
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
            if (_internalCount == MAX_QUEUE_CAPACITY)
            {
                OnQueueOverflow();
            }
        }

        public void EnqueueData(SensorValueBase value)
        {
            TrimDataIfNecessary(value);
            Enqueue(value);
        }

        private void TrimDataIfNecessary(SensorValueBase value)
        {
            if (!string.IsNullOrEmpty(value?.Comment))
            {
                value.Comment = value.Comment.Substring(0, Constants.MaxSensorValueStringLength);
            }
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
            var dataList = new List<SensorValueBase>();
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
                        
                            case IntBarSensorValue intBar when intBar.Count == 0:
                            case DoubleBarSensorValue doubleBar when doubleBar.Count == 0:
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
                while (count < MAX_VALUES_MESSAGE_CAPACITY && _internalCount > 0)
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
    }
}
