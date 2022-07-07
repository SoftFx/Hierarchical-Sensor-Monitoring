﻿using HSMServer.Core.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.Core.SensorsUpdatesQueue
{
    public sealed class UpdatesQueue : IUpdatesQueue
    {
        private const int Delay = 10;
        private const int PackageMaxSize = 100;

        private readonly ConcurrentQueue<(StoreInfo, BaseValue)> _queue;

        private bool _run;

        public event Action<List<(StoreInfo, BaseValue)>> NewItemsEvent;


        public UpdatesQueue()
        {
            _queue = new ConcurrentQueue<(StoreInfo, BaseValue)>();
            _run = true;

            ThreadPool.QueueUserWorkItem(RunManageThread);
        }


        public void AddItem((StoreInfo, BaseValue) storeWithBase) =>
            _queue.Enqueue(storeWithBase);

        public void AddItems(List<(StoreInfo, BaseValue)> storesWithBases)
        {
            foreach(var tuple in storesWithBases)
                AddItem(tuple);
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
        private List<(StoreInfo, BaseValue)> GetDataPackage()
        {
            var data = new List<(StoreInfo, BaseValue)>(PackageMaxSize);

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
