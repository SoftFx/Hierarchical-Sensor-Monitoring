using Microsoft.AspNetCore.StaticFiles;

namespace HSMServer.Extensions
{
    internal static class FileExtensions
    {
        internal static string GetContentType(this string fileName)
        {
            var provider = new FileExtensionContentTypeProvider();
            if (!provider.TryGetContentType(fileName, out var contentType))
            {
                contentType = System.Net.Mime.MediaTypeNames.Application.Octet;
            }

            return contentType;
        }
    }
}
