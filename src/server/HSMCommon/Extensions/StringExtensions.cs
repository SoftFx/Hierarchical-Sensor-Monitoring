using System;

namespace HSMCommon.Extensions
{
    public static class StringExtensions
    {
        public static string CapitalizeFirstChar(this string str) => string.IsNullOrEmpty(str)
            ? string.Empty
            : string.Create(str.Length, str, static (Span<char> chars, string str) =>
            {
                chars[0] = char.ToUpperInvariant(str[0]);
                str.AsSpan(1).CopyTo(chars[1..]);
            });
    }
}
