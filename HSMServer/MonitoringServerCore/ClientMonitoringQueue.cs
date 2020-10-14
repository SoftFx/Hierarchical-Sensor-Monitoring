using System;
using System.Collections.Generic;
using SensorsService;

namespace HSMServer.MonitoringServerCore
{
    public class ClientMonitoringQueue
    {
        private readonly object _lockObj = new object();
        private string _userName;
        private readonly Queue<SensorUpdateMessage> _monitoringQueue;
        private readonly List<SensorUpdateMessage> _emptyQueue = new List<SensorUpdateMessage>();
        private const int ErrorCapacity = 10000;
        private const int WarningCapacity = 5000;
        private const int UpdateListCapacity = 1000;
        public event EventHandler<ClientMonitoringQueue> QueueOverflow;
        public event EventHandler QueueOverflowWarning;
        public event EventHandler UserDisconnected;

        private bool HasData
        {
            get
            {
                lock (_lockObj)
                {
                    return _monitoringQueue.Count > 0;
                }
            }
        } 

        public ClientMonitoringQueue(string userName)
        {
            _userName = userName;
            _monitoringQueue = new Queue<SensorUpdateMessage>();
        }

        public void AddUpdate(SensorUpdateMessage message)
        {
            int count = -1;
            lock (_lockObj)
            {
                _monitoringQueue.Enqueue(message);
                count = _monitoringQueue.Count;
            }

            if (count >= ErrorCapacity)
            {
                OnQueueOverflow();
            }

            if (count >= WarningCapacity)
            {
                OnQueueOverflowWarning();
            }
        }

        public List<SensorUpdateMessage> GetSensorUpdateMessages()
        {
            lock (_lockObj)
            {
                if (!HasData)
                {
                    return _emptyQueue;
                }
                List<SensorUpdateMessage> updateList = new List<SensorUpdateMessage>();
                for (int i = 0; i < UpdateListCapacity; i++)
                {
                    updateList.Add(_monitoringQueue.Dequeue());
                }

                return updateList;
            }
        }

        public void Clear()
        {
            lock (_lockObj)
            {
                int count = _monitoringQueue.Count;
                for (int i = 0; i < count; i++)
                {
                    _monitoringQueue.Dequeue();
                }
            }
        }
        private void OnQueueOverflow()
        {
            QueueOverflow?.Invoke(this, this);
        }

        private void OnQueueOverflowWarning()
        {
            QueueOverflowWarning?.Invoke(this, EventArgs.Empty);
        }

        private void OnUserDisconnected()
        {
            UserDisconnected?.Invoke(this, EventArgs.Empty);
        }

        public string GetUserName()
        {
            return _userName;
        }
    }
}
