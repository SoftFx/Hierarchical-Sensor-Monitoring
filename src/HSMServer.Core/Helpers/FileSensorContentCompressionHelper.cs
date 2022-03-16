using HSMSensorDataObjects.FullDataObject;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Core.Model.Sensor;
using System.IO;
using System.IO.Compression;

namespace HSMServer.Core.Helpers
{
    internal static class FileSensorContentCompressionHelper
    {
        internal static FileSensorBytesValue CompressContent(this FileSensorBytesValue sensorValue)
        {
            using var output = new MemoryStream();
            using (var dstream = new DeflateStream(output, CompressionLevel.Optimal))
            {
                dstream.Write(sensorValue.FileContent, 0, sensorValue.FileContent.Length);
            }

            sensorValue.FileContent = output.ToArray();
            return sensorValue;
        }

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
