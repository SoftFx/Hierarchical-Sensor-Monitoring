using System;
using System.Collections;
using System.Collections.Generic;

namespace HSMDataCollector.SyncQueue.Data
{
    internal sealed class DataPackage<T> : IEnumerable<T>
    {
        private readonly List<QueueItem<T>> _items;

        internal int Count => _items.Count;

        // Captured at construction so time-in-queue is measured from package-build time.
        private readonly DateTime _now = DateTime.UtcNow;
        private double _time;

        internal IReadOnlyCollection<QueueItem<T>> Items => _items;

        internal DataPackage(int maxCapacity)
        {
            _items = new List<QueueItem<T>>(maxCapacity);
        }

        internal void AddValue(QueueItem<T> item)
        {
            _items.Add(item);
            _time += (_now - item.BuildDate).TotalSeconds;
        }

        public IEnumerator<T> GetEnumerator()
        {
            foreach (var item in _items)
                yield return item.Value;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        internal PackageInfo GetInfo() => new PackageInfo(_time, Count);
    }
}
