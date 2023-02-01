using HSMServer.Core.Model;
using System.Numerics;

namespace HSMServer.Core.Extensions
{
    internal static class BarValueExtensions
    {
        internal static BarBaseValue<T> Merge<T>(this BarBaseValue<T> first, BarBaseValue<T> second) where T : struct, INumber<T>
        {
            return second with
            {
                Count = first.Count + second.Count,
                Min = T.Min(first.Min, second.Min),
                Max = T.Max(first.Max, second.Max),
                Mean = (first.Mean + second.Mean) / (T.One + T.One),
            };
        }
    }
}
