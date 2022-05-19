using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace BenchmarkPlatform.SyncQueuePerformance
{
    internal sealed class ConcurrentQueue
    {
        private readonly ConcurrentQueue<int> _queue;

        private bool _run = false;

        internal Action<int> NewItemEvent;


        internal ConcurrentQueue()
        {
            _queue = new ConcurrentQueue<int>();
            _run = true;

            ThreadPool.QueueUserWorkItem(_ => RunManageThread());
        }


        internal void AddItem(int data)
        {
            _queue.Enqueue(data);
        }


        private async void RunManageThread()
        {
            while (_run)
            {
                if (_queue.TryDequeue(out var data))
                    NewItemEvent?.Invoke(data);
                else
                    await Task.Delay(10);
            }
        }

        internal void Stop()
        {
            _run = false;
        }
    }
}
