using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HSMServer.Notifications.Telegram.AddressBook.MessageBuilder
{
    internal sealed class CDict<T> : CDictBase<string, T> where T : class, new() { }


    internal sealed class CTupleDict<T> : CDictBase<(string, string), T> where T : class, new() { }


    internal abstract class CDictBase<T, U> : ConcurrentDictionary<T, U> where U : class, new()
    {
        public new U this[T key] => GetOrAdd(key);


        internal U GetOrAdd(T key)
        {
            if (!TryGetValue(key, out U value))
            {
                base[key] = new U();

                return base[key];
            }

            return value;
        }
    }


    internal sealed class CHash : HashSet<Guid>
    {
        private readonly object _lock = new();


        internal bool IsEmpty => Count == 0;


        internal new void Add(Guid item)
        {
            lock (_lock)
                base.Add(item);
        }

        internal new void Remove(Guid item)
        {
            lock (_lock)
                base.Remove(item);
        }

        internal new void Clear()
        {
            lock (_lock)
                base.Clear();
        }
    }
}
