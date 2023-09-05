using System.Collections.Generic;

namespace HSMCommon.Collections
{
    public class CHash<T> : HashSet<T>
    {
        private readonly object _lock = new();


        public CHash() : base() { }

        public CHash(IEqualityComparer<T> comparer) : base(comparer) { }


        public new void Add(T item)
        {
            lock (_lock)
                base.Add(item);
        }

        public new void Remove(T item)
        {
            lock (_lock)
                base.Remove(item);
        }

        public new void Clear()
        {
            lock (_lock)
                base.Clear();
        }
    }
}