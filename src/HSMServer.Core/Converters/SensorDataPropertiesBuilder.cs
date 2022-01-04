using System;
using System.IO;
using System.Text.Json;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.BarData;
using HSMSensorDataObjects.FullDataObject;
using HSMSensorDataObjects.TypedDataObject;

namespace HSMServer.Core.Converters
{
    internal static class SensorDataPropertiesBuilder
    {
        private const double SizeDenominator = 1024.0;


        internal static string GetStringValue(SensorValueBase sensorValue, DateTime timeCollected) =>
            sensorValue switch
            {
                BoolSensorValue boolSensorValue => GetStringValue(boolSensorValue, timeCollected),
                IntSensorValue intSensorValue => GetStringValue(intSensorValue, timeCollected),
                DoubleSensorValue doubleSensorValue => GetStringValue(doubleSensorValue, timeCollected),
                StringSensorValue stringSensorValue => GetStringValue(stringSensorValue, timeCollected),
                IntBarSensorValue intBarSensorValue => GetStringValue(intBarSensorValue, timeCollected),
                DoubleBarSensorValue doubleBarSensorValue => GetStringValue(doubleBarSensorValue, timeCollected),
                FileSensorBytesValue fileSensorBytesValue => GetStringValue(fileSensorBytesValue, timeCollected),
                FileSensorValue fileSensorValue => GetStringValue(fileSensorValue, timeCollected),
                UnitedSensorValue unitedSensorValue => GetStringValue(unitedSensorValue, timeCollected),
                _ => null,
            };

        internal static string GetShortStringValue(SensorValueBase sensorValue) =>
            sensorValue switch
            {
                BoolSensorValue boolSensorValue => GetShortStringValue(boolSensorValue),
                IntSensorValue intSensorValue => GetShortStringValue(intSensorValue),
                DoubleSensorValue doubleSensorValue => GetShortStringValue(doubleSensorValue),
                StringSensorValue stringSensorValue => GetShortStringValue(stringSensorValue),
                IntBarSensorValue intBarSensorValue => GetShortStringValue(intBarSensorValue),
                DoubleBarSensorValue doubleBarSensorValue => GetShortStringValue(doubleBarSensorValue),
                FileSensorBytesValue fileSensorBytesValue => GetShortStringValue(fileSensorBytesValue),
                FileSensorValue fileSensorValue => GetShortStringValue(fileSensorValue),
                UnitedSensorValue unitedSensorValue => GetShortStringValue(unitedSensorValue),
                _ => null,
            };

        internal static string GetStringValue(SensorDataEntity dataEntity)
        {
            switch ((SensorType)dataEntity.DataType)
            {
                case SensorType.BooleanSensor:
                    BoolSensorData boolData = JsonSerializer.Deserialize<BoolSensorData>(dataEntity.TypedData);
                    return GetSimpleSensorsString(dataEntity.TimeCollected, boolData.Comment, boolData.BoolValue);
                case SensorType.IntSensor:
                    IntSensorData intData = JsonSerializer.Deserialize<IntSensorData>(dataEntity.TypedData);
                    return GetSimpleSensorsString(dataEntity.TimeCollected, intData.Comment, intData.IntValue);
                case SensorType.DoubleSensor:
                    DoubleSensorData doubleData = JsonSerializer.Deserialize<DoubleSensorData>(dataEntity.TypedData);
                    return GetSimpleSensorsString(dataEntity.TimeCollected, doubleData.Comment, doubleData.DoubleValue);
                case SensorType.StringSensor:
                    StringSensorData stringData = JsonSerializer.Deserialize<StringSensorData>(dataEntity.TypedData);
                    return GetSimpleSensorsString(dataEntity.TimeCollected, stringData.Comment, stringData.StringValue);
                case SensorType.IntegerBarSensor:
                    IntBarSensorData intBarData = JsonSerializer.Deserialize<IntBarSensorData>(dataEntity.TypedData);
                    return GetBarSensorsString(dataEntity.TimeCollected, intBarData.Comment, intBarData.Min, intBarData.Mean,
                                               intBarData.Max, intBarData.Count, intBarData.LastValue);
                case SensorType.DoubleBarSensor:
                    DoubleBarSensorData doubleBarData = JsonSerializer.Deserialize<DoubleBarSensorData>(dataEntity.TypedData);
                    return GetBarSensorsString(dataEntity.TimeCollected, doubleBarData.Comment, doubleBarData.Min,
                                               doubleBarData.Mean, doubleBarData.Max, doubleBarData.Count, doubleBarData.LastValue);
                case SensorType.FileSensorBytes:
                    FileSensorBytesData fileSensorBytesData = JsonSerializer.Deserialize<FileSensorBytesData>(dataEntity.TypedData);
                    return GetFileSensorsString(dataEntity.TimeCollected, fileSensorBytesData.Comment, fileSensorBytesData.FileName,
                                                fileSensorBytesData.Extension, fileSensorBytesData.FileContent?.Length ?? 0);
                case SensorType.FileSensor:
                    FileSensorData fileSensorData = JsonSerializer.Deserialize<FileSensorData>(dataEntity.TypedData);
                    return GetFileSensorsString(dataEntity.TimeCollected, fileSensorData.Comment, fileSensorData.FileName,
                                                fileSensorData.Extension, fileSensorData.FileContent?.Length ?? 0);
            }

            return null;
        }

        internal static string GetShortStringValue(SensorDataEntity dataEntity)
        {
            switch ((SensorType)dataEntity.DataType)
            {
                case SensorType.BooleanSensor:
                    BoolSensorData boolData = JsonSerializer.Deserialize<BoolSensorData>(dataEntity.TypedData);
                    return boolData.BoolValue.ToString();
                case SensorType.IntSensor:
                    IntSensorData intData = JsonSerializer.Deserialize<IntSensorData>(dataEntity.TypedData);
                    return intData.IntValue.ToString();
                case SensorType.DoubleSensor:
                    DoubleSensorData doubleData = JsonSerializer.Deserialize<DoubleSensorData>(dataEntity.TypedData);
                    return doubleData.DoubleValue.ToString();
                case SensorType.StringSensor:
                    StringSensorData stringData = JsonSerializer.Deserialize<StringSensorData>(dataEntity.TypedData);
                    return stringData.StringValue;
                case SensorType.IntegerBarSensor:
                    IntBarSensorData intBarData = JsonSerializer.Deserialize<IntBarSensorData>(dataEntity.TypedData);
                    return GetBarSensorsShortString(intBarData.Min, intBarData.Mean, intBarData.Max, intBarData.Count, intBarData.LastValue);
                case SensorType.DoubleBarSensor:
                    DoubleBarSensorData doubleBarData = JsonSerializer.Deserialize<DoubleBarSensorData>(dataEntity.TypedData);
                    return GetBarSensorsShortString(doubleBarData.Min, doubleBarData.Mean, doubleBarData.Max, doubleBarData.Count, doubleBarData.LastValue);
                case SensorType.FileSensorBytes:
                    FileSensorBytesData fileSensorBytesData = JsonSerializer.Deserialize<FileSensorBytesData>(dataEntity.TypedData);
                    return GetFileSensorsShortString(fileSensorBytesData.FileName, fileSensorBytesData.Extension, fileSensorBytesData.FileContent?.Length ?? 0);
                case SensorType.FileSensor:
                    FileSensorData fileSensorData = JsonSerializer.Deserialize<FileSensorData>(dataEntity.TypedData);
                    return GetFileSensorsShortString(fileSensorData.FileName, fileSensorData.Extension, fileSensorData.FileContent?.Length ?? 0);
            }

            return null;
        }


        private static string GetStringValue(BoolSensorValue value, DateTime timeCollected) =>
            GetSimpleSensorsString(timeCollected, value.Comment, value.BoolValue);

        private static string GetStringValue(IntSensorValue value, DateTime timeCollected) =>
            GetSimpleSensorsString(timeCollected, value.Comment, value.IntValue);

        private static string GetStringValue(DoubleSensorValue value, DateTime timeCollected) =>
            GetSimpleSensorsString(timeCollected, value.Comment, value.DoubleValue);

        private static string GetStringValue(StringSensorValue value, DateTime timeCollected) =>
            GetSimpleSensorsString(timeCollected, value.Comment, value.StringValue);

        private static string GetStringValue(FileSensorValue value, DateTime timeCollected) =>
            GetFileSensorsString(timeCollected, value.Comment, value.FileName, value.Extension, value.FileContent?.Length ?? 0);

        private static string GetStringValue(FileSensorBytesValue value, DateTime timeCollected) =>
            GetFileSensorsString(timeCollected, value.Comment, value.FileName, value.Extension, value.FileContent?.Length ?? 0);

        private static string GetStringValue(IntBarSensorValue value, DateTime timeCollected) =>
            GetBarSensorsString(timeCollected, value.Comment, value.Min, value.Mean, value.Max, value.Count, value.LastValue);

        private static string GetStringValue(DoubleBarSensorValue value, DateTime timeCollected) =>
            GetBarSensorsString(timeCollected, value.Comment, value.Min, value.Mean, value.Max, value.Count, value.LastValue);

        private static string GetStringValue(UnitedSensorValue value, DateTime timeCollected)
        {
            switch (value.Type)
            {
                case SensorType.BooleanSensor:
                case SensorType.IntSensor:
                case SensorType.DoubleSensor:
                case SensorType.StringSensor:
                    return GetSimpleSensorsString(timeCollected, value.Comment, value.Data);
                case SensorType.IntegerBarSensor:
                    IntBarData intBarData = JsonSerializer.Deserialize<IntBarData>(value.Data);
                    return GetBarSensorsString(timeCollected, value.Comment, intBarData.Min, intBarData.Mean, intBarData.Max, intBarData.Count, intBarData.LastValue);
                case SensorType.DoubleBarSensor:
                    DoubleBarData doubleBarData = JsonSerializer.Deserialize<DoubleBarData>(value.Data);
                    return GetBarSensorsString(timeCollected, value.Comment, doubleBarData.Min, doubleBarData.Mean, doubleBarData.Max, doubleBarData.Count, doubleBarData.LastValue);
                default:
                    return string.Empty;
            }
        }


        private static string GetShortStringValue(BoolSensorValue value) => value.BoolValue.ToString();

        private static string GetShortStringValue(IntSensorValue value) => value.IntValue.ToString();

        private static string GetShortStringValue(DoubleSensorValue value) => value.DoubleValue.ToString();

        private static string GetShortStringValue(StringSensorValue value) => value.StringValue;

        private static string GetShortStringValue(IntBarSensorValue value) =>
            GetBarSensorsShortString(value.Min, value.Mean, value.Max, value.Count, value.LastValue);

        private static string GetShortStringValue(DoubleBarSensorValue value) =>
            GetBarSensorsShortString(value.Min, value.Mean, value.Max, value.Count, value.LastValue);

        private static string GetShortStringValue(FileSensorValue value) =>
            GetFileSensorsShortString(value.FileName, value.Extension, value.FileContent?.Length ?? 0);

        private static string GetShortStringValue(FileSensorBytesValue value) =>
            GetFileSensorsShortString(value.FileName, value.Extension, value.FileContent?.Length ?? 0);

        private static string GetShortStringValue(UnitedSensorValue value)
        {
            switch (value.Type)
            {
                case SensorType.BooleanSensor:
                case SensorType.IntSensor:
                case SensorType.DoubleSensor:
                case SensorType.StringSensor:
                    return value.Data;
                case SensorType.IntegerBarSensor:
                    IntBarData intBarData = JsonSerializer.Deserialize<IntBarData>(value.Data);
                    return GetBarSensorsShortString(intBarData.Min, intBarData.Mean, intBarData.Max, intBarData.Count, intBarData.LastValue);
                case SensorType.DoubleBarSensor:
                    DoubleBarData doubleBarData = JsonSerializer.Deserialize<DoubleBarData>(value.Data);
                    return GetBarSensorsShortString(doubleBarData.Min, doubleBarData.Mean, doubleBarData.Max, doubleBarData.Count, doubleBarData.LastValue);
                default:
                    return string.Empty;
            }
        }


        private static string GetSimpleSensorsString<T>(DateTime timeCollected, string comment, T value) =>
            $"Time: {timeCollected.ToUniversalTime():G}. Value = {value}" +
            $"{(string.IsNullOrEmpty(comment) ? string.Empty : $", comment = {comment}")}.";

        private static string GetFileSensorsString(DateTime timeCollected, string comment, string fileName, string extension, int contentLength) =>
            $"Time: {timeCollected.ToUniversalTime():G}. {GetFileSensorsShortString(fileName, extension, contentLength)}".AddComment(comment);

        private static string GetBarSensorsString<T>(DateTime timeCollected, string comment, T min, T mean, T max, int count, T lastValue) where T : struct =>
            $"Time: {timeCollected.ToUniversalTime():G}. Value: {GetBarSensorsShortString(min, mean, max, count, lastValue)}".AddComment(comment);

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


    public static class StringExtensions
    {
        public static string AddComment(this string source, string comment) =>
             $"{source}{(string.IsNullOrEmpty(comment) ? string.Empty : $" Comment = {comment}.")}";
    }
}
