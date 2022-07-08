using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.Core.Cache
{
    public sealed class UpdatesQueue : IUpdatesQueue
    {
        private const int Delay = 10;
        private const int PackageMaxSize = 100;

        private readonly ConcurrentQueue<StoreInfo> _queue;

        private bool _run;

        public event Action<List<StoreInfo>> NewItemsEvent;


        public UpdatesQueue()
        {
            _queue = new ConcurrentQueue<StoreInfo>();
            _run = true;

            ThreadPool.QueueUserWorkItem(RunManageThread);
        }


        public void AddItem(StoreInfo storeInfo) =>
            _queue.Enqueue(storeInfo);

        public void AddItems(List<StoreInfo> storeInfos)
        {
            foreach (var store in storeInfos)
                AddItem(store);
        }

        public void Dispose() => _run = false;


        private async void RunManageThread(object _)
        {
            while (_run)
            {
                var data = GetDataPackage();

                if (data.Count > 0)
                    NewItemsEvent?.Invoke(data);
                else
                    await Task.Delay(Delay);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private List<StoreInfo> GetDataPackage()
        {
            var data = new List<StoreInfo>(PackageMaxSize);

            for (int i = 0; i < PackageMaxSize; ++i)
            {
                if (!_queue.TryDequeue(out var value))
                    break;

                data.Add(value);
            }

            return data;
        }
    }
}
