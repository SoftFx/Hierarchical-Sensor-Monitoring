using System.Diagnostics.CodeAnalysis;

namespace HSMServer.Extensions
{
    public static class NullableExtensions
    {
        public static bool IsNullOrEqual<T>([AllowNull] this T first, [AllowNull] T second) => first is null || second is null || first.Equals(second);
    }
}