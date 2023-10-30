using System.Collections.Generic;

namespace HSMCommon.Collections
{
    public class CHash<T> : HashSet<T>
    {
        private readonly object _lock = new();


        public CHash() : base() { }

        public CHash(int capacity) : base(capacity) { }

        public CHash(IEqualityComparer<T> comparer) : base(comparer) { }


        public new bool Add(T item)
        {
            lock (_lock)
                return base.Add(item);
        }

        public new bool Remove(T item)
        {
            lock (_lock)
                return base.Remove(item);
        }

        public new void Clear()
        {
            lock (_lock)
                base.Clear();
        }

        public void Remove(params T[] items)
        {
            lock (_lock)
                foreach (var item in items)
                    base.Remove(item);
        }

        public new IEnumerator<T> GetEnumerator()
        {
            lock (_lock)
                return base.GetEnumerator();
        }
    }
}