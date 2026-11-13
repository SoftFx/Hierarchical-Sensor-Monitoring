using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

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

        /// <summary>
        /// Puts the items in the order they were issued in (<see cref="QueueItem{T}.Sequence"/>,
        /// not the timestamp: two items issued within one tick of the clock carry the same
        /// one). The package itself is reordered, not a copy handed to the sender: whoever
        /// reads the items afterwards (a sender that says which positions it delivered, the
        /// re-enqueue of the rest) must see the order that was sent.
        /// </summary>
        internal void SortByIssue()
        {
            if (_items.Count < 2)
                return;

            var ordered = _items.OrderBy(item => item.Sequence).ToList();

            _items.Clear();
            _items.AddRange(ordered);
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
