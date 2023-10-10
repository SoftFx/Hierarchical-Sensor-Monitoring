using System.Collections.Generic;

namespace HSMCommon.Collections
{
    public class CPriorityQueue<TElement, TKey> : PriorityQueue<TElement, TKey>
    {
        public bool IsEmpty => Count == 0;
    }
}