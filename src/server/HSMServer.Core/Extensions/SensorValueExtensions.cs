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
        private static readonly JsonSerializerOptions _options = new () 
        { 
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals 
        };
        
        
        public static BaseValue ToValue<T>(this byte[] bytes) where T : BaseValue
        {
            return bytes == null ? null : (BaseValue)JsonSerializer.Deserialize<T>(bytes, _options);
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