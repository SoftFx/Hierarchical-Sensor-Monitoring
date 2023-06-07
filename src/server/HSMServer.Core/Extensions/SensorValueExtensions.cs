using HSMServer.Core.Model;
using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace HSMServer.Core.Extensions
{
    public static class SensorValueExtensions
    {
        public static BaseValue ToValue<T>(this byte[] bytes) where T : BaseValue
        {
            if (bytes == null)
                return null;

            var rootElement = JsonDocument.Parse(bytes).RootElement;

            return rootElement.Deserialize<T>();
        }


        public static bool InRange<T>(this T value, DateTime from, DateTime to) where T : BaseValue
        {
            return value.ReceivingTime >= from && value.ReceivingTime <= to;
        }

        public static FileValue CompressContent(this FileValue sensorValue)
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


        public static FileValue DecompressContent(this FileValue value)
        {
            if (value.Value.Length == value.OriginalSize)
                return value;

            using var input = new MemoryStream(value.Value);
            using var output = new MemoryStream();
            using (var dstream = new DeflateStream(input, CompressionMode.Decompress))
            {
                dstream.CopyTo(output);
            }

            return value with
            {
                Value = output.ToArray()
            };
        }
    }
}