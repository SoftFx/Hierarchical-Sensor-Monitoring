using HSMSensorDataObjects;
using HSMSensorDataObjects.TypedDataObject;
using System.IO;
using System.Text.Json;

namespace HSMServer.Core.Converters
{
    public static class SensorStringPropertyBuilder
    {
        private const double SizeDenominator = 1024.0;


        public static string GetShortStringValue(SensorType sensorType, string typedData, int originalFileSensorSize)
        {
            if (typedData == null)
                return string.Empty;

            switch (sensorType)
            {
                case SensorType.BooleanSensor:
                    BoolSensorData boolData = JsonSerializer.Deserialize<BoolSensorData>(typedData);
                    return boolData.BoolValue.ToString();
                case SensorType.IntSensor:
                    IntSensorData intData = JsonSerializer.Deserialize<IntSensorData>(typedData);
                    return intData.IntValue.ToString();
                case SensorType.DoubleSensor:
                    DoubleSensorData doubleData = JsonSerializer.Deserialize<DoubleSensorData>(typedData);
                    return doubleData.DoubleValue.ToString();
                case SensorType.StringSensor:
                    StringSensorData stringData = JsonSerializer.Deserialize<StringSensorData>(typedData);
                    return stringData.StringValue;
                case SensorType.IntegerBarSensor:
                    IntBarSensorData intBarData = JsonSerializer.Deserialize<IntBarSensorData>(typedData);
                    return GetBarSensorsShortString(intBarData.Min, intBarData.Mean, intBarData.Max, intBarData.Count, intBarData.LastValue);
                case SensorType.DoubleBarSensor:
                    DoubleBarSensorData doubleBarData = JsonSerializer.Deserialize<DoubleBarSensorData>(typedData);
                    return GetBarSensorsShortString(doubleBarData.Min, doubleBarData.Mean, doubleBarData.Max, doubleBarData.Count, doubleBarData.LastValue);
                case SensorType.FileSensorBytes:
                    FileSensorBytesData fileSensorBytesData = JsonSerializer.Deserialize<FileSensorBytesData>(typedData);
                    return GetFileSensorsShortString(fileSensorBytesData.FileName, fileSensorBytesData.Extension, GetFileSensorBytesOriginalSize(fileSensorBytesData, originalFileSensorSize));
            }

            return null;
        }


        private static int GetFileSensorBytesOriginalSize(FileSensorBytesData data, int originalSize) =>
            originalSize == 0 ? data.FileContent?.Length ?? 0 : originalSize;

        private static string GetBarSensorsShortString<T>(T min, T mean, T max, int count, T lastValue) where T : struct =>
            $"Min = {min}, Mean = {mean}, Max = {max}, Count = {count}, Last = {lastValue}.";

        private static string GetFileSensorsShortString(string fileName, string extension, int contentLength)
        {
            string sizeString = FileSizeToNormalString(contentLength);
            string fileNameString = GetFileNameString(fileName, extension);

            return $"File size: {sizeString}. {fileNameString}";
        }

        private static string GetFileNameString(string fileName, string extension)
        {
            if (string.IsNullOrEmpty(extension) && string.IsNullOrEmpty(fileName))
                return "No file info specified!";

            if (string.IsNullOrEmpty(fileName))
                return $"Extension: {extension}.";

            if (!string.IsNullOrEmpty(Path.GetExtension(fileName)))
                return $"File name: {fileName}.";

            return $"File name: {Path.ChangeExtension(fileName, extension)}.";
        }

        private static string FileSizeToNormalString(int size)
        {
            if (size < SizeDenominator)
                return $"{size} bytes";

            double kb = size / SizeDenominator;
            if (kb < SizeDenominator)
                return $"{kb:#,##0} KB";

            double mb = kb / SizeDenominator;
            if (mb < SizeDenominator)
                return $"{mb:#,##0.0} MB";

            double gb = mb / SizeDenominator;
            return $"{gb:#,##0.0} GB";
        }
    }
}
