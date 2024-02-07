using System;

namespace HSMCommon.Extensions
{
    public static class StringExtensions
    {
        private const double SizeDenominator = 1024.0;

        public static string ToReadableMemoryFormat(this long bytes)
        {
            const int maxGBCounter = 3;

            double size = bytes;
            int counter = 0;

            while (size > SizeDenominator && counter++ < maxGBCounter)
                size /= SizeDenominator;

            string units = counter switch
            {
                0 => "bytes",
                1 => "KB",
                2 => "MB",
                _ => "GB",
            };

            return $"{size:F2} {units}";
        }


        public static string CapitalizeFirstChar(this string str) => string.IsNullOrEmpty(str)
            ? string.Empty
            : string.Create(str.Length, str, UpperFirstCharBuilder);


        private static void UpperFirstCharBuilder(Span<char> chars, string str)
        {
            chars[0] = char.ToUpperInvariant(str[0]);
            str.AsSpan(1).CopyTo(chars[1..]);
        }
    }
}
