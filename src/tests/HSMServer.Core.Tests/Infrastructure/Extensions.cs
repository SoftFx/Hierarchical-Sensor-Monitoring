using HSMCommon.Constants;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class StringExtensions
    {
        internal static string GetSensorName(this string path) => path?.Split(CommonConstants.SensorPathSeparator)?[^1];

        internal static string WithoutFirstSlash(this string path) =>
            path.FirstOrDefault() == CommonConstants.SensorPathSeparator ? path[1..] : path;
    }


    internal static class EnumerableExtensions
    {
        internal static ValueTask<List<T>> Flatten<T>(this IAsyncEnumerable<List<T>> enumerable) =>
            enumerable.SelectMany(x => x.ToAsyncEnumerable()).ToListAsync();
    }
}
