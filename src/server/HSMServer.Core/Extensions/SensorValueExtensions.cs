using HSMServer.Core.Model;
using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HSMServer.Core.Extensions
{
    public static class SensorValueExtensions
    {
        private const int RoundPrecision = 2;


        private static readonly JsonSerializerOptions _options = new()
        {
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };


        public static BaseValue ToValue<T>(this byte[] bytes) where T : BaseValue
        {
            return bytes == null ? null : (BaseValue)JsonSerializer.Deserialize<T>(bytes, _options);
        }

        public static double Round(this double value) => Math.Round(value, RoundPrecision, MidpointRounding.AwayFromZero);

        public static bool InRange<T>(this T value, DateTime from, DateTime to) where T : BaseValue
        {
            return value.LastUpdateTime >= from && value.ReceivingTime <= to;
        }

        public static FileValue CompressContent(this FileValue file)
        {
            if (file.Value == null)
                return file;

            using var output = new MemoryStream();
            using (var dstream = new DeflateStream(output, CompressionLevel.Optimal))
            {
                dstream.Write(file.Value, 0, file.Value.Length);
            }

            var compressedValue = output.ToArray();
            if (compressedValue.Length >= file.Value.Length)
                return file;

            return file with
            {
                Value = compressedValue,
            };
        }


        public static FileValue DecompressContent(this FileValue file)
        {
            if (file.Value == null || file.Value.Length == file.OriginalSize)
                return file;

            using var input = new MemoryStream(file.Value);
            using var output = new MemoryStream();
            using (var dstream = new DeflateStream(input, CompressionMode.Decompress))
            {
                dstream.CopyTo(output);
            }

            return file with
            {
                Value = output.ToArray()
            };
        }
    }
}