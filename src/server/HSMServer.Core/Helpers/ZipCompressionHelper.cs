using System.IO;
using System.IO.Compression;
using System.Text;

namespace HSMServer.Core.Helpers
{
    public static class ZipCompressionHelper
    {
        private const string DefaultFileName = "temp";
        private const string DefaultFileExtension = "txt";


        public static byte[] CompressToZip(this string content, string fileName, string fileExtension)
        {
            using var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var readmeEntry = archive.CreateEntry($"{fileName ?? DefaultFileName}.{fileExtension ?? DefaultFileExtension}");

                using var writer = readmeEntry.Open();
                writer.Write(Encoding.UTF8.GetBytes(content));
            }

            return stream.ToArray();
        }
    }
}
