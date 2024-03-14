﻿using HSMDataCollector.Core;
using HSMDataCollector.SyncQueue.BaseQueue;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMDataCollector.SyncQueue
{
    internal abstract class SyncQueue<T> : SyncQueue, ISyncQueue<T>
    {
        private protected readonly ConcurrentQueue<SyncQueueItem<T>> _valuesQueue = new ConcurrentQueue<SyncQueueItem<T>>();
        private protected readonly ConcurrentQueue<SyncQueueItem<T>> _failedQueue = new ConcurrentQueue<SyncQueueItem<T>>();

        private readonly int _maxValuesInPackage;
        private readonly int _maxQueueSize;

        private bool _flushing;

        public event Func<List<T>, Task> NewValuesEvent;
        public event Func<T, Task> NewValueEvent;


        protected SyncQueue(CollectorOptions options, TimeSpan collectPeriod) : base(collectPeriod)
        {
            _maxQueueSize = options.MaxQueueSize;
            _maxValuesInPackage = options.MaxValuesInPackage;
        }


        public override void Flush()
        {
            if (!_flushing)
            {
                _flushing = true;

                var packagesCount = (_failedQueue.Count + _valuesQueue.Count) / _maxValuesInPackage + 1;

                while (packagesCount-- > 0)
                {
                    var dataList = new List<T>(_maxValuesInPackage);
                    var sumTimeInQueue = 0.0;

                    Dequeue(_failedQueue, dataList, ref sumTimeInQueue);
                    Dequeue(_valuesQueue, dataList, ref sumTimeInQueue);

                    if (dataList.Count > 0)
                    {
                        NewValuesEvent?.Invoke(dataList);
                        ThrowPackageInfo(new PackageInfo(sumTimeInQueue, dataList.Count));
                    }
                    else
                        break;
                }

                _flushing = false;
            }
        }


        public void AddFail(T value) => Enqueue(_failedQueue, value);

        public void Add(T value) => Enqueue(_valuesQueue, value);

        public void Send(T value) => NewValueEvent?.Invoke(CompressValue(value));


        protected virtual bool IsValidValue(T value) => true;

        protected virtual T CompressValue(T value) => value;


        protected void Enqueue(ConcurrentQueue<SyncQueueItem<T>> queue, T value)
        {
            queue.Enqueue(new SyncQueueItem<T>(value));

            var overflowCnt = 0;

            while (queue.Count > _maxQueueSize)
            {
                if (queue.TryDequeue(out _))
                    overflowCnt++;
                else
                    break;
            }

            ThrowQueueOverflowCount(overflowCnt);
        }

        protected List<T> Dequeue(ConcurrentQueue<SyncQueueItem<T>> queue, List<T> dataList, ref double sumTime)
        {
            while (dataList.Count < _maxValuesInPackage && queue.TryDequeue(out var item))
                if (IsValidValue(item.Value))
                {
                    dataList.Add(CompressValue(item.Value));
                    sumTime += (DateTime.UtcNow - item.BuildDate).TotalSeconds;
                }

            return dataList;
        }
    }
}