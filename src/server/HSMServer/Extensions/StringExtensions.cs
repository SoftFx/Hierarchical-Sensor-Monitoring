using System;

namespace HSMServer.Extensions
{
    public static class StringExtensions
    {
        public static bool IgnoreCaseContains(this string src, string value) =>
            src.Contains(value, StringComparison.CurrentCultureIgnoreCase);

        public static Guid ToGuid(this string src) => Guid.TryParse(src, out var guid) ? guid : Guid.Empty;
    }
}