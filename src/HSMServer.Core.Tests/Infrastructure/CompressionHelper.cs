using System.IO;
using System.IO.Compression;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class CompressionHelper
    {
        internal static byte[] GetCompressedData(byte[] data)
        {
            using var output = new MemoryStream();
            using (var dstream = new DeflateStream(output, CompressionLevel.Optimal))
            {
                dstream.Write(data, 0, data.Length);
            }

            return output.ToArray();
        }
    }
}
