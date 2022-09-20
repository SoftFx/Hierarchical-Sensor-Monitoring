using HSMServer.Extensions;
using System;

namespace HSMServer.Model.TreeViewModels
{
    public record UpdatedSensorDataViewModel
    {
        public string Id { get; }

        public string Value { get; }

        public string Status { get; }

        public string UpdatedTimeStr { get; }

        public string ValidationError { get; }

        public string StatusColorClass { get; }


        public UpdatedSensorDataViewModel(SensorNodeViewModel sensor)
        {
            Id = sensor.EncodedId;
            Value = sensor.ShortStringValue;
            Status = sensor.Status.ToString();
            StatusColorClass = sensor.Status.ToCssIconClass();
            UpdatedTimeStr = sensor.UpdateTime != DateTime.MinValue
                ? $"updated {sensor.GetTimeAgo(DateTime.UtcNow - sensor.UpdateTime)}"
                : "updated - no data";
            ValidationError = sensor.ValidationError;
        }
    }
}
