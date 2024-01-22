using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HSMCommon.Collections
{
    public sealed class CStringDict<T> : CDictBase<string, T> where T : new() { }


    public sealed class CDict<T> : CDictBase<Guid, T> where T : new()
    {
        public CDict() : base() { }

        public CDict(Dictionary<Guid, T> dict) : base(dict) { }
    }


    public sealed class CTimeDict<T> : CDictBase<DateTime, T> where T : new()
    {
        public CTimeDict() : base() { }

        public CTimeDict(Dictionary<DateTime, T> dict) : base(dict) { }
    }


    public abstract class CDictBase<T, U> : ConcurrentDictionary<T, U> where U : new()
    {
        public new U this[T key]
        {
            get => GetOrAdd(key);
            set => base[key] = value;
        }


        protected CDictBase() : base() { }

        protected CDictBase(Dictionary<T, U> dict) : base(dict) { }


        public U GetOrAdd(T key)
        {
            if (!TryGetValue(key, out U value))
            {
                base[key] = new U();

                return base[key];
            }

            return value;
        }
    }
}