using NLog.LayoutRenderers.Wrappers;
using System;
using System.Collections;
using System.Collections.Generic;


namespace HSMDataCollector.SyncQueue.Data
{
    internal sealed class DataPackage<T> : IEnumerable<T>
    {
        private readonly List<QueueItem<T>> _items;

        internal int Count => _items.Count;

        internal IReadOnlyCollection<QueueItem<T>> Items => _items;

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
            foreach (var item in _items)
                yield return item.Value;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        internal void Clear()
        {
            _items.Clear();
        }


        internal PackageInfo GetInfo()
        {
            var now = DateTime.UtcNow;
            double time = 0;

            foreach (var item in _items)
            {
                time += (now - item.BuildDate).TotalMilliseconds;
            }

            return new PackageInfo(time, _items.Count);
        }
    }
}