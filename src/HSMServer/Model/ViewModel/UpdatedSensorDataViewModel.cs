using HSMServer.HtmlHelpers;
using System;

namespace HSMServer.Model.ViewModel
{
    public class UpdatedSensorDataViewModel
    {
        public string Id { get; }

        public string Value { get; }

        public string StatusColorClass { get; }

        public string UpdatedTimeStr { get; }

        public string ValidationError { get; }


        public UpdatedSensorDataViewModel(TreeViewModels.SensorViewModel sensor)
        {
            Id = sensor.EncodedId;
            Value = sensor.ShortStringValue;
            StatusColorClass = ViewHelper.GetStatusHeaderColorClass(sensor.Status);
            UpdatedTimeStr = $"updated {sensor.GetTimeAgo(DateTime.UtcNow - sensor.UpdateTime)}";
            ValidationError = sensor.ValidationError;
        }
    }
}
