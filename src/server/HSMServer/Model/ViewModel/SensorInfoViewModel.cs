using HSMServer.Core.Model;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.ViewModel
{
    public sealed class SensorInfoViewModel : NodeInfoBaseViewModel
    {
        public SensorType SensorType { get; }


        public string StatusComment { get; set; }

        public string Comment { get; set; }

        public string ShortLastValue { get; set; }


        // public constructor without parameters for action Home/UpdateSensorInfo
        public SensorInfoViewModel() : base() { }

        internal SensorInfoViewModel(SensorNodeViewModel sensor) : base(sensor)
        {
            SensorType = sensor.Type;
            StatusComment = sensor.ValidationError;
            Comment = sensor.LastValue?.Comment;
            ShortLastValue = sensor.ShortStringValue;
        }
    }
}
