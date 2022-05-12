using System;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class SensorDataStringValuesFactory
    {
        internal static string GetSimpleSensorsString<T>(DateTime timeCollected, string comment, T value) =>
            $"Time: {timeCollected.ToUniversalTime():G}. Value = {value}" +
            $"{(string.IsNullOrEmpty(comment) ? string.Empty : $", comment = {comment}")}.";

        internal static string GetFileSensorsString(DateTime timeCollected, string comment, string fileName, string extension, int contentLength) =>
           $"Time: {timeCollected.ToUniversalTime():G}. {GetFileSensorsShortString(fileName, extension, contentLength)}".AddComment(comment);

        internal static string GetFileSensorsShortString(string fileName, string extension, int contentLength)
        {
            string sizeString = $"{contentLength} bytes";
            string fileNameString = $"{fileName}.{extension}.";

            return $"File size: {sizeString}. File name: {fileNameString}";
        }

        internal static string GetBarSensorsString<T>(DateTime timeCollected, string comment, T min, T mean, T max, int count, T lastValue) where T : struct =>
            $"Time: {timeCollected.ToUniversalTime():G}. Value: {GetBarSensorsShortString(min, mean, max, count, lastValue)}".AddComment(comment);

        internal static string GetBarSensorsShortString<T>(T min, T mean, T max, int count, T lastValue) where T : struct =>
            $"Min = {min}, Mean = {mean}, Max = {max}, Count = {count}, Last = {lastValue}.";
    }


    public static class StringExtensions
    {
        public static string AddComment(this string source, string comment) =>
             $"{source}{(string.IsNullOrEmpty(comment) ? string.Empty : $" Comment = {comment}.")}";
    }
}
