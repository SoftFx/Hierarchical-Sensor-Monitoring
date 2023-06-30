﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HSMServer.Notifications.Telegram.AddressBook.MessageBuilder
{
    interface ICCollection
    {
        bool IsEmpty { get; }
    }


    internal sealed class CDict<T> : CDictBase<string, T> where T : new() { }


    internal sealed class CGuidDict<T> : CDictBase<Guid, T> where T : new() { }


    internal abstract class CDictBase<T, U> : ConcurrentDictionary<T, U>, ICCollection 
        where U : new()
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


        //internal void RemoveEmptyBranch(T key)
        //{
        //    if (TryGetValue(key, out U value) && value.IsEmpty)
        //        TryRemove(key, out _);
        //}
    }


    internal sealed class CGuidHash : CHash<Guid> { };

    internal sealed class CStringHash : CHash<string> { };


    internal class CHash<T> : HashSet<T>, ICCollection
    {
        private readonly object _lock = new();


        public bool IsEmpty => Count == 0;


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
