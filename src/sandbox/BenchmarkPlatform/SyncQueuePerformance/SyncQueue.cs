using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;

namespace BenchmarkPlatform.SyncQueuePerformance
{
    internal class SyncQueue
    {
        private readonly Queue _syncQueue;
        private bool _run = false;


        internal Action<int> NewItemEvent;


        internal SyncQueue()
        {
            _syncQueue = Queue.Synchronized(new Queue());

            _run = true;

            ThreadPool.QueueUserWorkItem(_ => RunManageThread());
        }


        internal void AddItem(int data)
        {
            _syncQueue.Enqueue(data);
        }


        private async void RunManageThread()
        {
            while (_run)
            {
                object data = null;

                lock (_syncQueue.SyncRoot)
                {
                    if (_syncQueue.Count > 0)
                        data = _syncQueue.Dequeue();
                }

                if (data is null)
                    await Task.Delay(10);
                else
                    NewItemEvent?.Invoke((int)data);
            }
        }

        internal void Stop()
        {
            _run = false;
        }
    }
}
