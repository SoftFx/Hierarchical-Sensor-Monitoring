using HSMServer.Core.Model;
using HSMServer.Extensions;
using HSMServer.Model.Authentication;

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
            Status = node.Status.ToEmpty(node.HasData).ToString();
            StatusIconColorClass = node.Status.ToEmpty(node.HasData).ToCssIconClass();
            GridCellColorClass = node.Status.ToCssGridCellClass();
            UpdatedTimeStr = $"updated {node.UpdateTime.GetTimeAgo()}";
            Tooltip = node.Tooltip;
        }
    }


    public record UpdatedSensorDataViewModel : UpdatedNodeDataViewModel
    {
        private const int MaxNewValuesCount = 99;


        public SensorType SensorType { get; }


        public string Value { get; }

        public string NewValuesCount { get; }

        public string ValidationError { get; }

        public bool IsValidationErrorVisible { get; }


        public string FileNameString { get; }

        public string Size { get; }

        public string SendingTime { get; }

        public string ReceivingTime { get; }

        public string Comment { get; }

        internal UpdatedSensorDataViewModel(SensorNodeViewModel sensor) : base(sensor)
        {
            SensorType = sensor.Type;

            Value = sensor.ShortStringValue;
            ValidationError = sensor.ValidationError;
            IsValidationErrorVisible = sensor.IsValidationErrorVisible;

            if (sensor.Type is SensorType.File)
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
            else
            {
                Comment = sensor.LastValue?.Comment;
            }
        }

        internal UpdatedSensorDataViewModel(SensorNodeViewModel sensor, User user) : this(sensor)
        {
            var count = user.History.NewValuesCnt;

            if (count > MaxNewValuesCount)
                NewValuesCount = $"{MaxNewValuesCount}+";
            else if (count > 0)
                NewValuesCount = $"{count}";
        }
    }
}
