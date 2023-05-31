using System;
using System.Collections.Concurrent;

namespace HSMServer.Core.TreeStateSnapshot
{
    public sealed class StateCollection<T> : ConcurrentDictionary<Guid, T> where T : class, new()
    {
        public new T this[Guid id] => GetOrAdd(id, new T());
    }
}