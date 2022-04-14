using HSMSensorDataObjects;
using HSMServer.Core.Converters;
using HSMServer.Core.TreeValuesCache.Entities;
using System;
using System.Collections.Generic;

namespace HSMServer.Model.TreeViewModels
{
    public class SensorViewModel : NodeViewModel
    {
        private const string ExtensionPattern = "Extension: ";
        private const string FileNamePattern = "File name: ";


        public SensorType SensorType { get; private set; }

        public string Description { get; private set; }

        public string ShortStringValue { get; private set; }

        public string ValidationError { get; private set; }

        public string Path { get; private set; }

        public string Product { get; private set; }

        public string FileNameString { get; private set; }

        public bool IsPlottingSupported { get; private set; }


        public SensorViewModel(SensorModel model, ProductViewModel parent)
        {
            Id = model.Id;
            Parent = parent;

            Update(model);
        }


        public string GetTimeAgo(TimeSpan time)
        {
            if (time.TotalDays > 30)
                return "> a month ago";

            if (time.TotalDays >= 1)
                return $"> {UnitsToString(time.TotalDays, "day")} ago";

            if (time.TotalHours >= 1)
                return $"> {UnitsToString(time.TotalHours, "hour")} ago";

            if (time.TotalMinutes >= 1)
                return $"{UnitsToString(time.TotalMinutes, "minute")} ago";

            if (time.TotalSeconds < 60)
                return "< 1 minute ago";

            return "no info";
        }

        internal void Update(SensorModel model)
        {
            Name = model.SensorName;
            SensorType = model.SensorType;
            Status = model.Status;
            Description = model.Description;
            UpdateTime = model.LastUpdateTime;
            ValidationError = model.ValidationError;

            ShortStringValue = SensorDataPropertiesBuilder.GetShortStringValue(model.SensorType, model.TypedData);

            IsPlottingSupported = IsSensorPlottingAvailable(model.SensorType);
            FileNameString = GetFileNameString(model.SensorType, ShortStringValue);

            FillProductAndPath();
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

        private static bool IsSensorPlottingAvailable(SensorType type) => type != SensorType.StringSensor;

        private static string GetFileNameString(SensorType sensorType, string value)
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
    }
}
