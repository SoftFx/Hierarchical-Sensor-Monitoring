using System.Collections.Generic;

namespace HSMCommon.Collections
{
    public class CPriorityQueue<TElement, TKey> : PriorityQueue<TElement, TKey>
    {
        private readonly object _lock = new();


        public bool IsEmpty => Count == 0;


        public new void Enqueue(TElement element, TKey priority)
        {
            lock (_lock)
            {
                base.Enqueue(element, priority);
            }
        }

        public new bool TryDequeue(out TElement element, out TKey priority)
        {
            lock (_lock)
            {
                return base.TryDequeue(out element, out priority);
            }
        }
    }
}