using Microsoft.AspNetCore.StaticFiles;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace HSMServer.Extensions
{
    internal static class FileExtensions
    {
        private const string DefaultFileName = "temp";
        private const string DefaultFileExtension = "txt";


        internal static string GetContentType(this string fileName)
        {
            var provider = new FileExtensionContentTypeProvider();
            if (!provider.TryGetContentType(fileName, out var contentType))
            {
                contentType = System.Net.Mime.MediaTypeNames.Application.Octet;
            }

            return contentType;
        }


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
