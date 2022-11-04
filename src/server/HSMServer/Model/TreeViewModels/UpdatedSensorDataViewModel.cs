using HSMServer.Extensions;

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
            UpdatedTimeStr = $"updated {sensor.GetTimeAgo()}";
            ValidationError = sensor.ValidationError;
        }
    }
}
