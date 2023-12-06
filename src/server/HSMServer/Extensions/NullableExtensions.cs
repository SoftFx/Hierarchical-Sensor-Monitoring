using System.Diagnostics.CodeAnalysis;

namespace HSMServer.Extensions
{
    public static class NullableExtensions
    {
        public static bool IsNullOrEqual<T>([AllowNull] this T first, T second) => first is null || first.Equals(second);
    }
}
