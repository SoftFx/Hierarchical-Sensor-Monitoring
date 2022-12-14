using HSMSensorDataObjects.FullDataObject;
using System;
using System.Collections.Generic;
using System.Threading;

namespace HSMDataCollector.Core
{
    internal class DataQueue : IDataQueue, IValuesQueue
    {
        private readonly Queue<object> _valuesQueue;
        private readonly List<object> _failedList;
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
            _valuesQueue = new Queue<object>();
            _failedList = new List<object>();
            _lockObj = new object();
            _listLock = new object();
            Disposed = false;
        }

        public event EventHandler<List<UnitedSensorValue>> SendValues;
        public event EventHandler<DateTime> QueueOverflow;
        public event EventHandler<FileSensorBytesValue> FileReceving;

        public void ReturnData(List<UnitedSensorValue> values)
        {
            lock (_listLock)
            {
                _failedList.AddRange(values);
            }

            _hasFailedData = true;
        }

        public void ReturnFile(FileSensorBytesValue file)
        {
            lock (_listLock)
            {
                _failedList.Add(file);
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

        private void Enqueue(object value)
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

        public void EnqueueObject(object value)
        {
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
                    foreach (var failedValue in _failedList)
                    {
                        if (failedValue is FileSensorBytesValue fileValue)
                            FileReceving?.Invoke(this, fileValue);
                        else
                            dataList.Add(failedValue as UnitedSensorValue);
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

                    if (value is FileSensorBytesValue fileValue)
                        FileReceving?.Invoke(this, fileValue);
                    else
                        dataList.Add(value as UnitedSensorValue);

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
