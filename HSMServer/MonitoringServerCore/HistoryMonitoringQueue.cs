using System.Collections.Generic;
using SensorsService;

namespace HSMServer.MonitoringServerCore
{
    public class HistoryMonitoringQueue
    {
        private readonly object _lockObj = new object();
        private readonly Queue<SensorHistoryMessage> _historyQueue;
        private readonly List<SensorHistoryMessage> _emptyQueue = new List<SensorHistoryMessage>();
        private int _elementsCount;

        private bool HasData => _elementsCount > 0;

        public HistoryMonitoringQueue()
        {
            _historyQueue = new Queue<SensorHistoryMessage>();
            _elementsCount = 0;
        }

        public void AddUpdate(SensorHistoryMessage message)
        {
            lock (_lockObj)
            {
                _historyQueue.Enqueue(message);
                ++_elementsCount;
            }
        }

        public List<SensorHistoryMessage> GetHistoryMessages(int n)
        {
            if (!HasData)
            {
                return _emptyQueue;
            }
            List<SensorHistoryMessage> updateList = new List<SensorHistoryMessage>();
            lock (_lockObj)
            {
                for (int i = 0; i < n; ++i)
                {
                    if (_elementsCount > 0)
                    {
                        updateList.Add(_historyQueue.Dequeue());
                        --_elementsCount;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            return updateList;
        }
    }
}
