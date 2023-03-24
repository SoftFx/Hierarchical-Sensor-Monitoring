using System.Collections.Generic;

namespace HSMServer.Core.Extensions
{
    public static class CollectionExtensions
    {
        public static List<T> AddRangeFluent<T>(this List<T> list, IEnumerable<T> newData)
        {
            list.AddRange(newData);
            return list;
        }


        public static List<T> AddFluent<T>(this List<T> list, T newData)
        {
            list.Add(newData);
            return list;
        }


        public static SortedSet<T> UnionFluent<T>(this SortedSet<T> first, SortedSet<T> second)
        {
            var result = new SortedSet<T>(first);

            result.UnionWith(second);

            return result;
        }


        public static SortedSet<T> ExceptFluent<T>(this SortedSet<T> first, SortedSet<T> second)
        {
            var result = new SortedSet<T>(first);

            result.ExceptWith(second);

            return result;
        }
    }
}
