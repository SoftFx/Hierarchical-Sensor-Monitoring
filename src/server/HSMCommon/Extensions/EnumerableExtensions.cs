using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMCommon.Extensions
{
    public static class EnumerableExtensions
    {
        public static ValueTask<List<T>> Flatten<T>(this IAsyncEnumerable<List<T>> enumerable) =>
            enumerable.SelectMany(x => x.ToAsyncEnumerable()).ToListAsync();

        public static List<(TKey Key, int Count)> ToGroupedList<TKey, TSource>(this IEnumerable<TSource> source, Func<TSource, TKey> selector) =>
            source.GroupBy(selector).OrderBy(o => o.Key).Select(s => (s.Key, s.Count())).ToList();

        public static (List<T> NotApply, List<T> Apply) SplitByCondition<T>(this IEnumerable<T> source, Predicate<T> predicate)
        {
            var notApply = new List<T>(1 << 2);
            var apply = new List<T>(1 << 2);

            foreach (var item in source)
                if (predicate(item))
                    apply.Add(item);
                else
                    notApply.Add(item);

            return (notApply, apply);
        }
    }
}