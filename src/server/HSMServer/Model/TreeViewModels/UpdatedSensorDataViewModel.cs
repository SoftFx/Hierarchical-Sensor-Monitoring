using HSMServer.Extensions;

namespace HSMServer.Model.TreeViewModels
{
    public record UpdatedNodeDataViewModel
    {
        public string Id { get; }

        public string Status { get; }

        public string StatusColorClass { get; }

        public string UpdatedTimeStr { get; }


        internal UpdatedNodeDataViewModel(NodeViewModel node)
        {
            Id = node.EncodedId;
            Status = node.Status.ToString();
            StatusColorClass = node.Status.ToCssIconClass();
            UpdatedTimeStr = $"updated {node.GetTimeAgo()}";
        }
    }


    public record UpdatedSensorDataViewModel : UpdatedNodeDataViewModel
    {
        public string Value { get; }

        public string ValidationError { get; }


        public UpdatedSensorDataViewModel(SensorNodeViewModel sensor) : base(sensor)
        {
            Value = sensor.ShortStringValue;
            ValidationError = sensor.ValidationError;
        }
    }
}
