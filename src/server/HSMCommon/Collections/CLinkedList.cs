using System.Collections.Generic;

namespace HSMCommon.Collections
{
    public sealed class CLinkedList<T> : LinkedList<T>
    {
        private readonly object _lock = new();


        public CLinkedList() : base() { }

        public CLinkedList(IEnumerable<T> collection) : base(collection) { }


        public new LinkedListNode<T> AddFirst(T value)
        {
            lock (_lock)
            {
                return base.AddFirst(value);
            }
        }

        public new LinkedListNode<T> AddLast(T value)
        {
            lock (_lock)
            {
                return base.AddLast(value);
            }
        }

        public new void RemoveFirst()
        {
            lock (_lock)
            {
                base.RemoveFirst();
            }
        }

        public new void RemoveLast()
        {
            lock (_lock)
            {
                base.RemoveLast();
            }
        }


        public new void Clear()
        {
            lock (_lock)
            {
                base.Clear();
            }
        }
    }
}