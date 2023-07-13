using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HSMServer.Notifications.Telegram.AddressBook.MessageBuilder
{
    internal sealed class CDict<T> : CDictBase<string, T> where T : new() { }


    internal abstract class CDictBase<T, U> : ConcurrentDictionary<T, U> where U : new()
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


    internal class CHash<T> : HashSet<T>
    {
        private readonly object _lock = new();

        internal CHash() : base() { }

        internal CHash(IEqualityComparer<T> comparer) : base(comparer) { }


        internal new void Add(T item)
        {
            lock (_lock)
                base.Add(item);
        }

        internal new void Remove(T item)
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
