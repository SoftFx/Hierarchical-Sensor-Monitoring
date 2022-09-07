using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMCommon.Extensions
{
    public static class CollectionExtensions
    {
        public static TResult MaxOrDefault<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> selector) =>
            source.Any() ? source.Max(selector) : default;
    }
}
