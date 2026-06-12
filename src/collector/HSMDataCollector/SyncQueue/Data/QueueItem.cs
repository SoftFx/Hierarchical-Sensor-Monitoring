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

        // Explicit BuildDate overload — used by tests that need deterministic ordering for the
        // #1090 watermark policy (DateTime.UtcNow's resolution is platform-dependent and can
        // collapse ordering of items constructed in quick succession).
        internal QueueItem(T value, DateTime buildDate)
        {
            BuildDate = buildDate;
            Value = value;
        }
    }
}