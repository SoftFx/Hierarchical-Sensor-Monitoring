using System.Text;
using HSMServer.Core.LargeFiles.Archives;
using Microsoft.Net.Http.Headers;

namespace HSMServer.Model.ManagementApi.LargeFiles
{
    /// <summary>The name a downloaded archive is saved as, and its Content-Disposition.</summary>
    public static class LargeFileNames
    {
        /// <summary>
        /// "&lt;entry name&gt;.zip": the name of the archive's only entry (<see cref="ArchiveEntryNames.Sanitize"/>), so the
        /// saved file and the file inside it carry the same name, with the same characters dropped (control and format
        /// characters such as bidi overrides) or replaced.
        /// </summary>
        public static string DownloadFileName(string name, string extension) => $"{ArchiveEntryNames.Sanitize(name, extension)}.zip";

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
