using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HSMServer.Core.TreeStateSnapshot
{
    public sealed class StateCollection<T> : ConcurrentDictionary<Guid, T> where T : class, new()
    {
        public new T this[Guid id] => GetOrAdd(id, new T());


        public StateCollection(Dictionary<Guid, T> dict) : base(dict) { }
    }
}