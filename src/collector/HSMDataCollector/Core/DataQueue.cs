using HSMSensorDataObjects.FullDataObject;
using System;
using System.Collections.Generic;
using System.Threading;

namespace HSMDataCollector.Core
{
    internal class DataQueue : IDataQueue, IValuesQueue
    {
        private readonly Queue<UnitedSensorValue> _valuesQueue;
        private readonly List<UnitedSensorValue> _failedList;
        private const int MAX_VALUES_MESSAGE_CAPACITY = 10000;
        private const int MAX_QUEUE_CAPACITY = 100000;
        private int _internalCount = 0;
        private readonly object _lockObj;
        private readonly object _listLock;
        private Timer _sendTimer;
        private bool _hasFailedData;
        public bool Disposed { get; private set; }

        public DataQueue()
        {
            _valuesQueue = new Queue<UnitedSensorValue>();
            _failedList = new List<UnitedSensorValue>();
            _lockObj = new object();
            _listLock = new object();
            Disposed = false;
        }
        
        public event EventHandler<List<UnitedSensorValue>> SendValues;
        public event EventHandler<DateTime> QueueOverflow;

        public void ReturnData(List<UnitedSensorValue> values)
        {
            lock (_listLock)
            {
                _failedList.AddRange(values);
            }

            _hasFailedData = true;
        }

        public List<UnitedSensorValue> GetCollectedData()
        {
            List<UnitedSensorValue> values = new List<UnitedSensorValue>();
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

        private void Enqueue(UnitedSensorValue value)
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
        
        public void EnqueueData(UnitedSensorValue value)
        {
            TrimDataIfNecessary(value);
            Enqueue(value);
        }

        private void TrimDataIfNecessary(UnitedSensorValue value)
        {
            if (value?.Data == null || value.Data.Length <= Constants.MaxSensorValueStringLength)
                return;

            value.Data = value.Data.Substring(0, Constants.MaxSensorValueStringLength);
            if (!string.IsNullOrEmpty(value.Comment))
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
        private List<UnitedSensorValue> DequeueData()
        {
            List<UnitedSensorValue> dataList = new List<UnitedSensorValue>();
            if (_hasFailedData)
            {
                lock (_listLock)
                {
                    dataList.AddRange(_failedList);
                    _failedList.Clear();
                }

                _hasFailedData = false;
            }

            int count = 0;
            lock (_lockObj)
            {
                while (count < MAX_VALUES_MESSAGE_CAPACITY && _internalCount > 0)
                {
                    dataList.Add(_valuesQueue.Dequeue());
                    ++count;
                    --_internalCount;
                }
            }

            return dataList;
        }

        private void OnQueueOverflow()
        {
            QueueOverflow?.Invoke(this, DateTime.Now);
        }
        
        private void OnSendValues(List<UnitedSensorValue> values)
        {
            SendValues?.Invoke(this, values);
        }
    }
}
