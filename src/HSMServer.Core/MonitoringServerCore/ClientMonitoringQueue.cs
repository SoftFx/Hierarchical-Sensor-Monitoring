using System;
using System.Collections.Generic;
using System.Timers;
using HSMCommon.Model.SensorsData;

namespace HSMServer.Core.MonitoringServerCore
{
    internal class ClientMonitoringQueue
    {
        private readonly object _lockObj = new object();
        private readonly string _userName;
        //private readonly Timer _timer;
        private readonly Queue<SensorData> _monitoringQueue;
        private readonly List<SensorData> _emptyQueue = new List<SensorData>();
        private int _elementsCount;
        private const int ErrorCapacity = 30000;
        private const int WarningCapacity = 15000;
        private const int UpdateListCapacity = 5000;
        public event EventHandler<ClientMonitoringQueue> QueueOverflow;
        public event EventHandler QueueOverflowWarning;
        public event EventHandler<string> UserDisconnected;
        

        private bool HasData => _elementsCount > 0;

        public ClientMonitoringQueue(string userName)
        {
            _userName = userName;
            _monitoringQueue = new Queue<SensorData>();
            _elementsCount = 0;
            //_timer = new Timer(15000);
            //_timer.Elapsed += DisconnectedTimer_Elapsed;
            //_timer.Start();
            //_timer.Enabled = true;
        }

        public void AddUpdate(SensorData message)
        {
            lock (_lockObj)
            {
                _monitoringQueue.Enqueue(message);
                ++_elementsCount;
            }

            if (_elementsCount >= ErrorCapacity)
            {
                OnQueueOverflow();
            }

            if (_elementsCount >= WarningCapacity)
            {
                OnQueueOverflowWarning();
            }
        }

        public List<SensorData> GetSensorsUpdates()
        {
            if (!HasData)
            {
                return _emptyQueue;
            }
            List<SensorData> updateList = new List<SensorData>();
            lock (_lockObj)
            {
                for (int i = 0; i < UpdateListCapacity; i++)
                {
                    if (_elementsCount > 0)
                    {
                        updateList.Add(_monitoringQueue.Dequeue());
                        --_elementsCount;
                    }
                    else
                    {
                        break;
                    }
                }

            }
            //_timer.Stop();
            //_timer.Start();
            return updateList;
        }

        public List<SensorData> GetSensorsUpdates(int n)
        {
            if (!HasData)
            {
                return _emptyQueue;
            }
            List<SensorData> updateList = new List<SensorData>();
            lock (_lockObj)
            {
                int loopStepCount = n > UpdateListCapacity ? UpdateListCapacity : n;
            }
            return updateList;
        }

        public void Clear()
        {
            lock (_lockObj)
            {
                for (int i = 0; i < _elementsCount; i++)
                {
                    _monitoringQueue.Dequeue();
                }
            }

            _elementsCount = 0;
        }

        private void DisconnectedTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            OnUserDisconnected();
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
            UserDisconnected?.Invoke(this, _userName);
        }

        public string GetUserName()
        {
            return _userName;
        }
    }
}
