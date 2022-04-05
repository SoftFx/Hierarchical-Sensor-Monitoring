using HSMSensorDataObjects;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Core.Model.Sensor;
using HSMServer.Core.TreeValuesCache.Entities;
using System;
using System.Text.Json;

namespace HSMServer.Model.TreeViewModels
{
    public class SensorViewModel : NodeViewModel
    {
        private const string ExtensionPattern = "Extension: ";
        private const string FileNamePattern = "File name: ";

        public string StringValue { get; set; }
        public SensorType SensorType { get; set; }
        public string Description { get; set; }
        public string ShortStringValue { get; set; }
        public TransactionType TransactionType { get; set; }
        public string ValidationError { get; set; }

        public bool IsPlottingSupported =>
            SensorType == SensorType.IntSensor ||
            SensorType == SensorType.DoubleSensor ||
            SensorType == SensorType.DoubleBarSensor ||
            SensorType == SensorType.IntegerBarSensor ||
            SensorType == SensorType.BooleanSensor;

        public string FileNameString
        {
            get
            {
                var ind = StringValue.IndexOf(FileNamePattern);
                if (ind != -1)
                {
                    var fileNameString = StringValue[(ind + FileNamePattern.Length)..];
                    int firstDotIndex = fileNameString.IndexOf('.');
                    int secondDotIndex = fileNameString[(firstDotIndex + 1)..].IndexOf('.');
                    return fileNameString[..(firstDotIndex + secondDotIndex + 1)];
                }

                ind = StringValue.IndexOf(ExtensionPattern);
                if (ind != -1)
                {
                    var extensionString = StringValue[(ind + ExtensionPattern.Length)..];
                    int dotIndex = extensionString.IndexOf('.');
                    return extensionString[..dotIndex];
                }

                return string.Empty;
            }
        }


        public SensorViewModel(SensorModel model)
        {
            Id = model.Id.ToString();
            Name = model.SensorName;
            SensorType = model.SensorType;
            Status = model.Status;
            Description = model.Description;
            UpdateTime = model.LastUpdateTime;
            ShortStringValue = JsonSerializer.Deserialize<BoolSensorData>(model.TypedData).BoolValue.ToString(); // TODO: build ShortStringValue and StringValue for all sensors 
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

        private static string UnitsToString(double value, string unit)
        {
            int intValue = Convert.ToInt32(value);
            return intValue > 1 ? $"{intValue} {unit}s" : $"1 {unit}";
        }
    }
}
