using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using System;
using System.Collections.Generic;
using System.Threading;

namespace HSMDataCollector.Core
{
    internal class DataQueue : IDataQueue, IValuesQueue
    {
        // 07.07.2021: Use new data object
        [Obsolete]
        private readonly Queue<CommonSensorValue> _valuesQueue;
        [Obsolete]
        private readonly List<CommonSensorValue> _failedList;

        private readonly Queue<FullSensorValue> _queue;
        private readonly List<FullSensorValue> _list;
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
            //_valuesQueue = new Queue<CommonSensorValue>();
            //_failedList = new List<CommonSensorValue>();
            _queue = new Queue<FullSensorValue>();
            _list = new List<FullSensorValue>();
            _lockObj = new object();
            _listLock = new object();
            Disposed = false;
        }
        
        public event EventHandler<List<CommonSensorValue>> SendData;
        public event EventHandler<List<FullSensorValue>> SendValues;
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
            //values.AddRange(DequeueData());
            return values;
        }

        public void ReturnData(List<FullSensorValue> values)
        {
            lock (_listLock)
            {
                _list.AddRange(values);
            }

            _hasFailedData = true;
        }

        public List<FullSensorValue> GetCollectedData()
        {
            List<FullSensorValue> values = new List<FullSensorValue>();
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

        private void Enqueue(FullSensorValue value)
        {
            lock (_lockObj)
            {
                _queue.Enqueue(value);
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

        public void EnqueueData(FullSensorValue value)
        {
            throw new NotImplementedException();
        }

        private void OnTimerTick(object state)
        {
            var data = DequeueData();
            //OnSendData(data);
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
        //private List<CommonSensorValue> DequeueData()
        //{
        //    List<CommonSensorValue> dataList = new List<CommonSensorValue>();
        //    if (_hasFailedData)
        //    {
        //        lock (_listLock)
        //        {
        //            dataList.AddRange(_failedList);
        //            _failedList.Clear();
        //        }

        //        _hasFailedData = false;
        //    
        //    else
        //    {
        //        int count = 0;
        //        lock (_lockObj)
        //        {
        //            while (count < MAX_VALUES_MESSAGE_CAPACITY && _internalCount > 0)
        //            {
        //                dataList.Add(_valuesQueue.Dequeue());
        //                ++count;
        //                --_internalCount;
        //            }                    
        //        }
        //    }

        //    return dataList;
        //}
        private List<FullSensorValue> DequeueData()
        {
            List<FullSensorValue> dataList = new List<FullSensorValue>();
            if (_hasFailedData)
            {
                lock (_listLock)
                {
                    dataList.AddRange(_list);
                    _list.Clear();
                }

                _hasFailedData = false;
            }

            int count = 0;
            lock (_lockObj)
            {
                while (count < MAX_VALUES_MESSAGE_CAPACITY && _internalCount > 0)
                {
                    dataList.Add(_queue.Dequeue());
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
        private void OnSendData(List<CommonSensorValue> values)
        {
            SendData?.Invoke(this, values);
        }

        private void OnSendValues(List<FullSensorValue> values)
        {
            SendValues?.Invoke(this, values);
        }
    }
}
