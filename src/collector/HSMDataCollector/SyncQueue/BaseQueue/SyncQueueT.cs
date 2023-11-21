using HSMDataCollector.Core;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HSMDataCollector.SyncQueue
{
    internal abstract class SyncQueue<T> : SyncQueue, ISyncQueue<T>
    {
        private protected readonly ConcurrentQueue<T> _valuesQueue = new ConcurrentQueue<T>();
        private protected readonly ConcurrentQueue<T> _failedQueue = new ConcurrentQueue<T>();

        private readonly int _maxValuesInPackage;
        private readonly int _maxQueueSize;

        private bool _flushing;

        public event Action<List<T>> NewValuesEvent;
        public event Action<T> NewValueEvent;


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

                    Dequeue(_failedQueue, dataList);
                    Dequeue(_valuesQueue, dataList);

                    if (dataList.Count > 0)
                        NewValuesEvent?.Invoke(dataList);
                    else
                        break;
                }

                _flushing = false;
            }
        }


        public virtual void Push(T value) => Enqueue(_valuesQueue, value);

        public virtual void PushFailValue(T value) => Enqueue(_failedQueue, value);


        protected virtual bool IsSendValue(T value) => true;


        protected void InvokeNewValue(T value) => NewValueEvent?.Invoke(value);

        protected void Enqueue(ConcurrentQueue<T> queue, T value)
        {
            if (IsStopped)
                return;

            queue.Enqueue(value);

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

        protected List<T> Dequeue(ConcurrentQueue<T> queue, List<T> dataList)
        {
            while (dataList.Count < _maxValuesInPackage && queue.TryDequeue(out var value))
                if (IsSendValue(value))
                    dataList.Add(value);

            return dataList;
        }
    }
}