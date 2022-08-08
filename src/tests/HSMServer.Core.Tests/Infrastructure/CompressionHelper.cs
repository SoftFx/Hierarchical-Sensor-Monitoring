using HSMServer.Core.Model;
using System.IO;
using System.IO.Compression;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class CompressionHelper
    {
        internal static FileValue GetCompressedValue(FileValue value)
        {
            using var output = new MemoryStream();
            using (var dstream = new DeflateStream(output, CompressionLevel.Optimal))
            {
                dstream.Write(value.Value, 0, value.Value.Length);
            }

            return value with { Value = output.ToArray() };
        }
    }
}
