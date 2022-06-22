using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BenchmarkPlatform.SyncQueuePerformance
{
    internal class LockQueue
    {
        private readonly object _lock = new();
        private readonly Queue<int> _queue;
        private bool _run = false;


        internal Action<int> NewItemEvent;


        internal LockQueue()
        {
            _queue = new Queue<int>();
            _run = true;

            ThreadPool.QueueUserWorkItem(_ => RunManageThread());
        }


        internal void AddItem(int data)
        {
            lock (_lock)
            {
                _queue.Enqueue(data);
            }
        }


        private async void RunManageThread()
        {
            while (_run)
            {
                var result = false;

                lock (_lock)
                {
                    result = _queue.TryDequeue(out var data);

                    if (result)
                        NewItemEvent?.Invoke(data);
                }

                if (!result)
                    await Task.Delay(10);
            }
        }

        internal void Stop()
        {
            _run = false;
        }
    }
}
