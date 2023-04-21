using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Extensions
{
    internal static class EnumerableExtensions
    {
        internal static ValueTask<List<T>> Flatten<T>(this IAsyncEnumerable<List<T>> enumerable) =>
            enumerable.SelectMany(x => x.ToAsyncEnumerable()).ToListAsync();

        internal static List<(TKey Key, int Count)> ToGroupedList<TKey, TSource>(this IEnumerable<TSource> source, Func<TSource, TKey> selector) => 
            source.GroupBy(selector).OrderBy(o => o.Key).Select(s => (s.Key, s.Count())).ToList();
    }
}
