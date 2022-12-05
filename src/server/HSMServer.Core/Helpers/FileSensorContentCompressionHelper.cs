using HSMServer.Core.Model;
using System.IO;
using System.IO.Compression;

namespace HSMServer.Core.Helpers
{
    internal static class FileSensorContentCompressionHelper
    {
        internal static FileValue CompressContent(this FileValue sensorValue)
        {
            using var output = new MemoryStream();
            using (var dstream = new DeflateStream(output, CompressionLevel.Optimal))
            {
                dstream.Write(sensorValue.Value, 0, sensorValue.Value.Length);
            }

            var compressedValue = output.ToArray();
            if (compressedValue.Length >= sensorValue.Value.Length)
                return sensorValue;

            return sensorValue with
            {
                Value = compressedValue,
            };
        }

        internal static FileValue DecompressContent(this FileValue value)
        {
            if (value.Value.Length == value.OriginalSize)
                return value;

            using var input = new MemoryStream(value.Value);
            using var output = new MemoryStream();
            using (var dstream = new DeflateStream(input, CompressionMode.Decompress))
            {
                dstream.CopyTo(output);
            }

            return value with { Value = output.ToArray() };
        }
    }
}
