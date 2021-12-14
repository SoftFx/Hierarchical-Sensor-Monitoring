using System;
using HSMSensorDataObjects.FullDataObject;

namespace HSMServer.Core.Converters
{
    internal static class SensorDataPropertiesBuilder
    {
        private const double SIZE_DENOMINATOR = 1024.0;


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
                _ => null,
            };


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


        private static string GetShortStringValue(BoolSensorValue value) =>
            value.BoolValue.ToString();

        private static string GetShortStringValue(IntSensorValue value) =>
            value.IntValue.ToString();

        private static string GetShortStringValue(DoubleSensorValue value) =>
            value.DoubleValue.ToString();

        private static string GetShortStringValue(StringSensorValue value) =>
            value.StringValue;

        private static string GetShortStringValue(IntBarSensorValue value) =>
            GetBarSensorsShortString(value.Min, value.Mean, value.Max, value.Count, value.LastValue);

        private static string GetShortStringValue(DoubleBarSensorValue value) =>
            GetBarSensorsShortString(value.Min, value.Mean, value.Max, value.Count, value.LastValue);

        private static string GetShortStringValue(FileSensorValue value) =>
            GetFileSensorsShortString(value.FileName, value.Extension, value.FileContent?.Length ?? 0);

        private static string GetShortStringValue(FileSensorBytesValue value) =>
            GetFileSensorsShortString(value.FileName, value.Extension, value.FileContent?.Length ?? 0);


        private static string GetSimpleSensorsString<T>(DateTime timeCollected, string comment, T value) =>
            $"Time: {timeCollected.ToUniversalTime():G}. Value = {value}" +
            $"{(string.IsNullOrEmpty(comment) ? string.Empty : $", comment = {comment}")}.";

        private static string GetFileSensorsString(DateTime timeCollected, string comment, string fileName, string extension, int contentLength) =>
            $"Time: {timeCollected.ToUniversalTime():G}. {GetFileSensorsShortString(fileName, extension, contentLength)}" +
            $"{(string.IsNullOrEmpty(comment) ? string.Empty : $" Comment = {comment}.")}";

        private static string GetBarSensorsString<T>(DateTime timeCollected, string comment, T min, T mean, T max, int count, T lastValue) where T : struct =>
            $"Time: {timeCollected.ToUniversalTime():G}. Value: {GetBarSensorsShortString(min, mean, max, count, lastValue)}" +
            $"{(string.IsNullOrEmpty(comment) ? string.Empty : $" Comment = {comment}.")}";

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

            if (fileName.IndexOf('.') != -1)
                return $"File name: {fileName}.";

            return $"File name: {fileName}.{extension}.";
        }

        private static string FileSizeToNormalString(int size)
        {
            if (size < SIZE_DENOMINATOR)
                return $"{size} bytes";

            double kb = size / SIZE_DENOMINATOR;
            if (kb < SIZE_DENOMINATOR)
                return $"{kb:#,##0} KB";

            double mb = kb / SIZE_DENOMINATOR;
            if (mb < SIZE_DENOMINATOR)
                return $"{mb:#,##0.0} MB";

            double gb = mb / SIZE_DENOMINATOR;
            return $"{gb:#,##0.0} GB";
        }
    }
}
