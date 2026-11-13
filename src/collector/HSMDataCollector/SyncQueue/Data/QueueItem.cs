using System;

namespace HSMDataCollector.SyncQueue.Data
{
    internal readonly struct QueueItem<T>
    {
        // One counter per item type: the order in which the items of a queue were issued.
        private static long _issued;

        public DateTime BuildDate { get; }

        /// <summary>
        /// The place of this item among the items of its type, in the order they were issued.
        /// An item that returns to the queue after a failed send keeps it. Unlike
        /// <see cref="BuildDate"/> it tells two items apart that were issued within one tick of
        /// the clock, and it does not move when the wall clock is set back.
        /// </summary>
        internal long Sequence { get; }

        public T Value { get; }


        internal QueueItem(T value)
        {
            BuildDate = DateTime.UtcNow;
            Sequence = System.Threading.Interlocked.Increment(ref _issued);
            Value = value;
        }

        // Explicit BuildDate overload — used by tests that need deterministic ordering for the
        // #1090 watermark policy (DateTime.UtcNow's resolution is platform-dependent and can
        // collapse ordering of items constructed in quick succession).
        internal QueueItem(T value, DateTime buildDate)
        {
            BuildDate = buildDate;
            Sequence = System.Threading.Interlocked.Increment(ref _issued);
            Value = value;
        }
    }
}