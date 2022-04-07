using HSMSensorDataObjects;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Core.Model.Sensor;
using HSMServer.Core.TreeValuesCache.Entities;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace HSMServer.Model.TreeViewModels
{
    public class SensorViewModel : NodeViewModel
    {
        private const string ExtensionPattern = "Extension: ";
        private const string FileNamePattern = "File name: ";

        public SensorType SensorType { get; set; }
        public string Description { get; set; }
        public string ShortStringValue { get; set; }
        public TransactionType TransactionType { get; set; }
        public string ValidationError { get; set; }

        public string Path { get; private set; }
        public string Product { get; private set; }
        public bool IsPlottingSupported { get; private set; }
        public string FileNameString { get; private set; }


        public SensorViewModel(SensorModel model, ProductViewModel parent)
        {
            Id = model.Id.ToString();
            Parent = parent;
            Name = model.SensorName;
            SensorType = model.SensorType;
            Status = model.Status;
            Description = model.Description;
            UpdateTime = model.LastUpdateTime;

            // TODO remove this logic
            if (SensorType == SensorType.BooleanSensor)
            {
                ShortStringValue = JsonSerializer.Deserialize<BoolSensorData>(model.TypedData).BoolValue.ToString(); // TODO: build ShortStringValue and StringValue for all sensors 
            }
            else if (SensorType == SensorType.FileSensorBytes)
            {
                var data = JsonSerializer.Deserialize<FileSensorBytesData>(model.TypedData);
                ShortStringValue = GetFileSensorsShortString(data.FileName, data.Extension, data.FileContent?.Length ?? 0);
            }

            IsPlottingSupported = IsSensorPlottingAvailable(model.SensorType);
            FileNameString = GetFileNameStting(model.SensorType, ShortStringValue);

            FillProductAndPath();
        }


        public string GetTimeAgo(TimeSpan time)
        {
            if (time.TotalDays > 30)
                return "> a month ago";

            if (time.TotalDays >= 1)
                return $"> {UnitsToString(time.TotalDays, "day")} ago";

            if (time.TotalHours >= 1)
            {
                return $"> {UnitsToString(time.TotalHours, "hour")} ago";
            }

            if (time.TotalMinutes >= 1)
            {
                return $"{UnitsToString(time.TotalMinutes, "minute")} ago";
            }

            if (time.TotalSeconds < 60)
                return "< 1 minute ago";

            return "no info";
        }

        private void FillProductAndPath()
        {
            var pathParts = new List<string>() { Name };

            var parentNode = Parent;
            while (parentNode.Parent != null)
            {
                pathParts.Add(parentNode.Name);
                parentNode = parentNode.Parent;
            }
            pathParts.Reverse();

            Path = string.Join('/', pathParts);
            Product = parentNode.Name;
        }

        private static string UnitsToString(double value, string unit)
        {
            int intValue = Convert.ToInt32(value);
            return intValue > 1 ? $"{intValue} {unit}s" : $"1 {unit}";
        }

        private static bool IsSensorPlottingAvailable(SensorType type) =>
            type == SensorType.IntSensor ||
            type == SensorType.DoubleSensor ||
            type == SensorType.DoubleBarSensor ||
            type == SensorType.IntegerBarSensor ||
            type == SensorType.BooleanSensor;

        private static string GetFileNameStting(SensorType sensorType, string value)
        {
            if (sensorType != SensorType.FileSensorBytes || string.IsNullOrEmpty(value))
                return string.Empty;

            var ind = value.IndexOf(FileNamePattern);
            if (ind != -1)
            {
                var fileNameString = value[(ind + FileNamePattern.Length)..];
                int firstDotIndex = fileNameString.IndexOf('.');
                int secondDotIndex = fileNameString[(firstDotIndex + 1)..].IndexOf('.');
                return fileNameString[..(firstDotIndex + secondDotIndex + 1)];
            }

            ind = value.IndexOf(ExtensionPattern);
            if (ind != -1)
            {
                var extensionString = value[(ind + ExtensionPattern.Length)..];
                int dotIndex = extensionString.IndexOf('.');
                return extensionString[..dotIndex];
            }

            return string.Empty;
        }


        // TODO remove this logic from viewmodel
        private const double SizeDenominator = 1024.0;


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

            if (!string.IsNullOrEmpty(System.IO.Path.GetExtension(fileName)))
                return $"File name: {fileName}.";

            return $"File name: {System.IO.Path.ChangeExtension(fileName, extension)}.";
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
