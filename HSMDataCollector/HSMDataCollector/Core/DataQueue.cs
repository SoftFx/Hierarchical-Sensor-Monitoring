using System;
using System.Collections.Generic;
using System.Threading;
using HSMSensorDataObjects;

namespace HSMDataCollector.Core
{
    internal class DataQueue : IDataQueue, IValuesQueue
    {
        private readonly Queue<CommonSensorValue> _valuesQueue;
        private readonly List<CommonSensorValue> _failedList; 
        private const int MAX_VALUES_MESSAGE_CAPACITY = 200;
        private const int MAX_QUEUE_CAPACITY = 1000;
        private int _internalCount = 0;
        private readonly object _lockObj;
        private readonly object _listLock;
        private Timer _sendTimer;
        private bool _hasFailedData;
        public bool Disposed { get; private set; }

        public DataQueue()
        {
            _valuesQueue = new Queue<CommonSensorValue>();
            _failedList = new List<CommonSensorValue>();
            _lockObj = new object();
            _listLock = new object();
            Disposed = false;
        }
        
        public event EventHandler<List<CommonSensorValue>> SendData;
        public event EventHandler<DateTime> QueueOverflow; 
        public void ReturnFailedData(List<CommonSensorValue> values)
        {
            lock (_listLock)
            {
                _failedList.AddRange(values);
            }

            _hasFailedData = true;
        }

        public List<CommonSensorValue> GetAllCollectedData()
        {
            List<CommonSensorValue> values = new List<CommonSensorValue>();
            if (_hasFailedData)
            {
                values.AddRange(DequeueData());
            }
            values.AddRange(DequeueData());
            return values;
        }

        public void InitializeTimer()
        {
            TimeSpan timerTime = TimeSpan.FromSeconds(20);
            _sendTimer = new Timer(OnTimerTick, null, timerTime, timerTime);
        }

        public void Stop()
        {
            _sendTimer.Dispose();
            Disposed = true;
        }

        public void Clear()
        {
            ClearData();
        }

        private void EnqueueValue(CommonSensorValue value)
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

        public void Enqueue(CommonSensorValue value)
        {
            EnqueueValue(value);
        }
        private void OnTimerTick(object state)
        {
            var data = DequeueData();
            OnSendData(data);
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
        private List<CommonSensorValue> DequeueData()
        {
            List<CommonSensorValue> dataList = new List<CommonSensorValue>();
            if (_hasFailedData)
            {
                lock (_listLock)
                {
                    dataList.AddRange(_failedList);
                    _failedList.Clear();
                }

                _hasFailedData = false;
            }
            else
            {
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
            }

            return dataList;
        }

        private void OnQueueOverflow()
        {
            QueueOverflow?.Invoke(this, DateTime.Now);
        }
        private void OnSendData(List<CommonSensorValue> values)
        {
            SendData?.Invoke(this, values);
        }
    }
}
