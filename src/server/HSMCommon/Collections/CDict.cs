using System.Collections.Concurrent;

namespace HSMCommon.Collections
{
    public sealed class CDict<T> : CDictBase<string, T> where T : new() { }


    public abstract class CDictBase<T, U> : ConcurrentDictionary<T, U> where U : new()
    {
        public new U this[T key] => GetOrAdd(key);


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