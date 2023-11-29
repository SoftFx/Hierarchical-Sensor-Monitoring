using System;

namespace HSMDataCollector.SyncQueue.BaseQueue
{
    internal readonly struct SyncQueueItem<T>
    {
        public DateTime BuildDate { get; }

        public T Value { get; }


        internal SyncQueueItem(T value)
        {
            BuildDate = DateTime.UtcNow;
            Value = value;
        }
    }
}