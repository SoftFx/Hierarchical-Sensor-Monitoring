using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace HSMCommon.Collections
{
    public sealed class CDict<T> : CDictBase<string, T> where T : new() { }


    public sealed class CGuidDict<T> : CDictBase<Guid, T> where T : new() { }


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