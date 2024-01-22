using System.Collections.Generic;
using System.Linq;

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

        public bool TryPeekValue(out TElement element) => TryPeek(out element, out _);

        public List<TElement> UnwrapToList()
        {
            lock (_lock)
            {
                var elements = new List<(TElement value, TKey key)>(Count);

                while (!IsEmpty && TryDequeue(out var value, out var key))
                    elements.Add((value, key));

                foreach (var (value, key) in elements)
                    Enqueue(value, key);

                return elements.Select(u => u.value).ToList();
            }
        }
    }
}