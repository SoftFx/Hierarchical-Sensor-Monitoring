using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Extensions
{
    internal static class EnumerableExtensions
    {
        internal static ValueTask<List<T>> Flatten<T>(this IAsyncEnumerable<List<T>> enumerable) =>
            enumerable.SelectMany(x => x.ToAsyncEnumerable()).ToListAsync();
    }
}
