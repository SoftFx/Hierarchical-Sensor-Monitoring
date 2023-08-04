using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Extensions
{
    public static class CollectionExtensions
    {
        public static TResult MaxOrDefault<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> selector) =>
            source.Any() ? source.Max(selector) : default;

        public static List<SelectListItem> ToSelectedItems<T>(this IEnumerable<T> items, Func<T, string> keyFactory = null, Func<T, string> valueFactory = null)
        {
            static string GetValueDefault(T item) => item.ToString();

            keyFactory ??= GetValueDefault;
            valueFactory ??= GetValueDefault;

            return items.Select(item => new SelectListItem(keyFactory(item), valueFactory(item))).ToList();
        }
    }
}