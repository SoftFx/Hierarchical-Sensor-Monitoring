using System;
using System.Collections.Generic;


namespace HSMCommon.Extensions
{
    public static class DicitonaryExtensions
    {
        public static T GetOrAdd<K, T>(this IDictionary<K, T> dictionary, K key, Func<T> valueProvider)
        {
            T value;
            if (dictionary.TryGetValue(key, out value))
                return value;

            value = valueProvider();
            dictionary[key] = value;
            return value;
        }

        public static T GetOrAdd<K, T>(this IDictionary<K, T> dictionary, K key, Func<K, T> valueProvider)
        {
            T value;
            if (dictionary.TryGetValue(key, out value))
                return value;

            value = valueProvider(key);
            dictionary[key] = value;
            return value;
        }

        public static T GetOrAdd<K, T>(this IDictionary<K, T> dictionary, K key)
            where T : new()
        {
            T value;
            if (dictionary.TryGetValue(key, out value))
                return value;

            value = new T();
            dictionary[key] = value;
            return value;
        }

        public static T GetOrDefault<K, T>(this IDictionary<K, T> dictionary, K key)
        {
            T value;
            if (dictionary.TryGetValue(key, out value))
                return value;

            return default(T);
        }

        public static T AddOrModify<K, T>(this IDictionary<K, T> dictionary, K key, Func<T> addFunc, Action<T> modifyAction)
        {
            T value;
            if (dictionary.TryGetValue(key, out value))
            {
                modifyAction(value);
                return value;
            }

            value = addFunc();
            dictionary[key] = value;
            return value;
        }
    }
}