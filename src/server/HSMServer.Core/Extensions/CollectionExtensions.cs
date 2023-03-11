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
    }
}
