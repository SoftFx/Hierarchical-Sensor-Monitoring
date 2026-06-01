using System;
using System.Collections;
using System.Collections.Generic;


namespace HSMDataCollector.SyncQueue.Data
{
    internal sealed class DataPackage<T> : IEnumerable<T>
    {
        private readonly List<QueueItem<T>> _items;

        private double _time = 0;
        private int _count = 0;

        internal IEnumerable<T> Items => this;

        internal int Count => _items.Count;

        internal IReadOnlyCollection<QueueItem<T>> RawItems => _items;

        internal DataPackage(int maxCapacity)
        {
            _items = new List<QueueItem<T>>(maxCapacity);
        }

        internal void AddValue(QueueItem<T> item)
        {
            _items.Add(item);
        }

        public IEnumerator<T> GetEnumerator()
        {
            DateTime now = DateTime.UtcNow;

            foreach (var queueItem in _items)
            {
                _time += (now - queueItem.BuildDate).TotalSeconds;
                _count += 1;

                yield return queueItem.Value;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        internal void Clear()
        {
            _items.Clear();
            _time = 0;
            _count = 0;
        }

        internal PackageInfo GetInfo() => new PackageInfo(_time, _count);
    }
}