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


    public class CDictBase<T, U> : ConcurrentDictionary<T, U> where U : new()
    {
        public new U this[T key]
        {
            get => GetOrAdd(key, new U());
            set => base[key] = value;
        }


        public CDictBase() : base() { }

        public CDictBase(Dictionary<T, U> dict) : base(dict) { }

    }
}