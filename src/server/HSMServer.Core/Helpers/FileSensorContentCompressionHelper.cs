using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Sensor;
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

            var newValue = sensorValue with
            {
                Value = output.ToArray()
            };

            return newValue;
        }

        //ToDo
        internal static byte[] GetDecompressedContent(SensorHistoryData historyData, FileSensorBytesData data)
        {
            if (historyData.OriginalFileSensorContentSize == 0)
                return data.FileContent;

            using var input = new MemoryStream(data.FileContent);
            using var output = new MemoryStream();
            using (var dstream = new DeflateStream(input, CompressionMode.Decompress))
            {
                dstream.CopyTo(output);
            }

            return output.ToArray();
        }
    }
}
