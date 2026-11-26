using System;
using System.Collections.Generic;

namespace HSMDataCollector.LargeFiles
{
    /// <summary>
    /// The keys of what was said once, bounded: past <see cref="Capacity"/> the oldest key is forgotten (and may be said
    /// again), so texts that carry paths and system messages cannot grow it for the life of a collector. Thread-safe.
    /// </summary>
    internal sealed class SaidOnce
    {
        internal const int Capacity = 1000;

        private readonly HashSet<string> _keys = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> _order = new Queue<string>();

        /// <summary>True the first time a key is seen (while it is remembered).</summary>
        internal bool First(string key)
        {
            lock (_keys)
            {
                if (!_keys.Add(key))
                    return false;

                _order.Enqueue(key);

                if (_order.Count > Capacity)
                    _keys.Remove(_order.Dequeue());

                return true;
            }
        }

        internal void Clear()
        {
            lock (_keys)
            {
                _keys.Clear();
                _order.Clear();
            }
        }

        internal int Count
        {
            get
            {
                lock (_keys)
                    return _keys.Count;
            }
        }
    }
}
