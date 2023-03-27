using HSMServer.Core.Model;
using HSMServer.Extensions;

namespace HSMServer.Model.TreeViewModel
{
    public record UpdatedNodeDataViewModel
    {
        public string Id { get; }

        public string Status { get; }

        public string StatusIconColorClass { get; }

        public string GridCellColorClass { get; }

        public string UpdatedTimeStr { get; }

        public string Tooltip { get; }


        internal UpdatedNodeDataViewModel(NodeViewModel node)
        {
            Id = node.EncodedId;
            Status = node.Status.ToString();
            StatusIconColorClass = node.Status.ToCssIconClass();
            GridCellColorClass = node.Status.ToCssGridCellClass();
            UpdatedTimeStr = $"updated {node.UpdateTime.GetTimeAgo()}";
            Tooltip = node.Tooltip;
        }
    }


    public record UpdatedSensorDataViewModel : UpdatedNodeDataViewModel
    {
        public SensorType SensorType { get; }
        
        
        public string Value { get; }

        public string ValidationError { get; }

        public bool IsValidationErrorVisible { get; }
        
        
        public string FileNameString { get; }
        
        public string Size { get; }
        
        public string SendingTime { get; }
        
        public string ReceivingTime { get; }
        
        public string Comment { get; }

        public UpdatedSensorDataViewModel(SensorNodeViewModel sensor) : base(sensor)
        {
            SensorType = sensor.SensorType;
            
            Value = sensor.ShortStringValue;
            ValidationError = sensor.ValidationError;
            IsValidationErrorVisible = sensor.IsValidationErrorVisible;

            if (sensor.SensorType is SensorType.File)
            {
                FileNameString = sensor.FileNameString;
                var file = (FileValue)sensor.LastValue;
                if (file is not null)
                {
                    SendingTime = file.Time.ToUniversalTime().ToDefaultFormat();
                    ReceivingTime = file.ReceivingTime.ToDefaultFormat();
                    Comment = file.Comment;
                    Size = file.FileSizeToNormalString();
                }
            }
        }
    }
}
