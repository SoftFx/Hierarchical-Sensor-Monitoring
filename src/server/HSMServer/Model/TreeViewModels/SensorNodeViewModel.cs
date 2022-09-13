using HSMCommon.Constants;
using HSMServer.Core.Model;
using HSMServer.Helpers;
using System;

namespace HSMServer.Model.TreeViewModels
{
    public class SensorNodeViewModel : NodeViewModel
    {
        private const string ExtensionPattern = "Extension: ";
        private const string FileNamePattern = "File name: ";


        public Guid Id { get; }

        public string EncodedId { get; }

        public SensorType SensorType { get; private set; }

        public string Description { get; private set; }

        public SensorState State { get; private set; }

        public bool HasData { get; private set; }

        public string ShortStringValue { get; private set; }

        public string Product { get; private set; }

        public string FileNameString { get; private set; }

        public bool IsPlottingSupported { get; private set; }

        internal TimeIntervalViewModel ExpectedUpdateInterval { get; private set; } = new();

        internal string Unit { get; private set; }

        internal BaseValue LastValue { get; private set; }

        public string ValidationError { get; private set; }


        public SensorNodeViewModel(BaseSensorModel model)
        {
            Id = model.Id;
            EncodedId = SensorPathHelper.EncodeGuid(Id);

            Update(model);
        }


        public string GetTimeAgo(TimeSpan time)
        {
            if (time == TimeSpan.MinValue)
                return " - no data";

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

        internal void Update(BaseSensorModel model)
        {
            Name = model.DisplayName;
            SensorType = model.Type;
            Description = model.Description;
            State = model.State;
            UpdateTime = model.LastUpdateTime;
            Status = model.ValidationResult.Result;
            ValidationError = model.ValidationResult.Message;
            Product = model.ProductName;
            Path = $"{CommonConstants.SensorPathSeparator}{model.Path}";
            Unit = model.Unit;

            ExpectedUpdateInterval.Update(model.ExpectedUpdateIntervalPolicy?.ToTimeInterval());

            LastValue = model.LastValue;
            HasData = model.HasData;
            ShortStringValue = model.LastValue?.ShortInfo;

            IsPlottingSupported = IsSensorPlottingAvailable(model.Type);
            FileNameString = GetFileNameString(model.Type, ShortStringValue);
        }

        private static string UnitsToString(double value, string unit)
        {
            int intValue = Convert.ToInt32(value);
            return intValue > 1 ? $"{intValue} {unit}s" : $"1 {unit}";
        }

        private static bool IsSensorPlottingAvailable(SensorType type) => type != SensorType.String;

        private static string GetFileNameString(SensorType sensorType, string value)
        {
            if (sensorType != SensorType.File || string.IsNullOrEmpty(value))
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
