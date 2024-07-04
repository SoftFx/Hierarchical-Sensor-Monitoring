using System;

namespace HSMDataCollector.SyncQueue.Data
{
    internal readonly struct QueueItem<T>
    {
        public DateTime BuildDate { get; }

        public T Value { get; }


        internal QueueItem(T value)
        {
            BuildDate = DateTime.UtcNow;
            Value = value;
        }
    }
}