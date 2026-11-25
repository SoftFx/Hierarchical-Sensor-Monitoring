using System;
using System.Linq;
using System.Text;

namespace HSMDataCollector.Tables
{
    /// <summary>
    /// The path of a table sensor in the form the server stores it: no empty segments and no white space around a
    /// segment (the server's <c>Split(RemoveEmptyEntries | TrimEntries)</c>). A block names its table by that form byte for
    /// byte, and the server refuses a table registration under any other spelling (wire contract, "Table sensor
    /// registration").
    /// </summary>
    internal static class TablePath
    {
        internal const char Separator = '/';

        internal static string Normalize(string path) =>
            string.Join(Separator.ToString(),
                        (path ?? string.Empty).Split(Separator)
                                              .Select(segment => segment.Trim())
                                              .Where(segment => segment.Length > 0));

        /// <summary>
        /// Checks a normalized path; throws <see cref="ArgumentException"/> for a path the server would refuse whatever
        /// is retried: empty, with a backslash or a tab, or longer in UTF-8 than a block may name a table.
        /// </summary>
        internal static void Check(string normalized, string given, int maxBytes)
        {
            if (normalized.Length == 0)
                throw new ArgumentException($"A table sensor needs a path; '{given}' has no segment.", "path");

            if (normalized.IndexOf('\\') >= 0 || normalized.IndexOf('\t') >= 0)
                throw new ArgumentException($"The path '{given}' contains a backslash or a tab, which the server refuses.", "path");

            var bytes = Encoding.UTF8.GetByteCount(normalized);

            if (bytes > maxBytes)
                throw new ArgumentException($"A block names a table sensor by its path in UTF-8, at most {maxBytes} bytes; '{normalized}' has {bytes}.", "path");
        }
    }
}
