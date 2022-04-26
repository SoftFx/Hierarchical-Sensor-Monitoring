using HSMCommon.Constants;
using HSMSensorDataObjects;
using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Converters;
using HSMServer.Core.Extensions;
using HSMServer.Helpers;
using System;

namespace HSMServer.Model.TreeViewModels
{
    public class SensorNodeViewModel : NodeViewModel
    {
        private const string ExtensionPattern = "Extension: ";
        private const string FileNamePattern = "File name: ";

        private readonly TimeSpan _minimumUpdateInterval = new(0, 0, 5, 0);

        private bool _isSensorValueOutdated;

        private string _validationError;


        public Guid Id { get; }

        public string EncodedId => SensorPathHelper.EncodeGuid(Id);

        public override SensorStatus Status
        {
            get
            {
                var lastUpdateInterval = DateTime.UtcNow - UpdateTime;

                if (lastUpdateInterval < _minimumUpdateInterval || ExpectedUpdateInterval == TimeSpan.Zero ||
                    lastUpdateInterval < ExpectedUpdateInterval)
                {
                    _isSensorValueOutdated = false;
                    return base.Status;
                }

                _isSensorValueOutdated = true;
                return base.Status.GetWorst(SensorStatus.Warning);
            }
            protected set => base.Status = value;
        }

        public string ValidationError
        {
            get
            {
                if (_isSensorValueOutdated)
                    return $"{_validationError}{Environment.NewLine}{ValidationConstants.SensorValueOutdated}";

                return _validationError;
            }
            private set => _validationError = value;
        }

        public SensorType SensorType { get; private set; }

        public string Description { get; private set; }

        public bool HasData { get; private set; }

        public string ShortStringValue { get; private set; }

        public string Path { get; private set; }

        public string Product { get; private set; }

        public string FileNameString { get; private set; }

        public bool IsPlottingSupported { get; private set; }

        internal TimeSpan ExpectedUpdateInterval { get; private set; }

        internal string Unit { get; private set; }


        public SensorNodeViewModel(SensorModel model)
        {
            Id = model.Id;

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
            ExpectedUpdateInterval = model.ExpectedUpdateInterval;

            Name = model.SensorName;
            SensorType = model.SensorType;
            Status = model.Status;
            Description = model.Description;
            UpdateTime = model.LastUpdateTime;
            ValidationError = model.ValidationError;
            Product = model.ProductName;
            Path = model.Path;
            Unit = model.Unit;

            HasData = !string.IsNullOrEmpty(model.TypedData);
            ShortStringValue = SensorDataPropertiesBuilder.GetShortStringValue(model.SensorType, model.TypedData);

            IsPlottingSupported = IsSensorPlottingAvailable(model.SensorType);
            FileNameString = GetFileNameString(model.SensorType, ShortStringValue);
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
