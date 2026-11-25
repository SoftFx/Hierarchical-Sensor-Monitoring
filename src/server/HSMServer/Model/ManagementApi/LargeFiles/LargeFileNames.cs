using System;
using System.Text;
using Microsoft.Net.Http.Headers;

namespace HSMServer.Model.ManagementApi.LargeFiles
{
    /// <summary>The name a downloaded archive is saved as, and its Content-Disposition.</summary>
    public static class LargeFileNames
    {
        internal const string FallbackName = "file";

        private const int MaxNameLength = 200;


        /// <summary>
        /// "&lt;original name&gt;.zip" with everything a file system or a header could misread replaced: no path (only the
        /// part after the last slash or backslash), no drive letter, no control or reserved characters, no leading or
        /// trailing dots and spaces. The extension the collector sent is appended when the name does not already end with it.
        /// </summary>
        public static string DownloadFileName(string name, string extension)
        {
            var original = name ?? string.Empty;
            var ext = extension?.Trim().TrimStart('.') ?? string.Empty;

            if (ext.Length > 0 && !original.EndsWith($".{ext}", StringComparison.OrdinalIgnoreCase))
                original = $"{original}.{ext}";

            return $"{Sanitize(original)}.zip";
        }

        /// <summary>attachment; filename="&lt;ASCII fallback&gt;"; filename*=UTF-8''&lt;the full name&gt;.</summary>
        public static string ContentDisposition(string fileName)
        {
            var header = new ContentDispositionHeaderValue("attachment")
            {
                FileName = AsciiFallback(fileName),
            };

            header.FileNameStar = fileName;

            return header.ToString();
        }


        internal static string Sanitize(string name)
        {
            var lastSeparator = name.LastIndexOfAny(['/', '\\']);
            if (lastSeparator >= 0)
                name = name[(lastSeparator + 1)..];

            var builder = new StringBuilder(name.Length);

            foreach (var symbol in name)
                builder.Append(char.IsControl(symbol) || symbol is ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '_' : symbol);

            var result = builder.ToString().Trim().Trim('.').Trim();

            if (result.Length > MaxNameLength)
                result = result[..MaxNameLength].TrimEnd('.', ' ');

            return result.Length == 0 ? FallbackName : result;
        }

        // The plain filename parameter is read by clients that do not know filename*: printable ASCII only, and none of the
        // characters a quoted string would have to escape.
        internal static string AsciiFallback(string fileName)
        {
            var builder = new StringBuilder(fileName.Length);

            foreach (var symbol in fileName)
                builder.Append(symbol is >= ' ' and <= '~' and not '"' and not '\\' and not '%' ? symbol : '_');

            return builder.ToString();
        }
    }
}
