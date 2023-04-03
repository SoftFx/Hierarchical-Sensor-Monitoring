using HSMServer.Core.Model;
using HSMServer.Model.TreeViewModel;
using SensorStatus = HSMServer.Model.TreeViewModel.SensorStatus;

namespace HSMServer.Model.ViewModel
{
    public class SensorInfoViewModel : NodeInfoBaseViewModel
    {
        public SensorType SensorType { get; }
        
        public SensorStatus SensorStatus { get; }

        public string Unit { get; set; }

        public string StatusComment { get; set; }
        

        // public constructor without parameters for action Home/UpdateSensorInfo
        public SensorInfoViewModel() : base() { }

        internal SensorInfoViewModel(SensorNodeViewModel sensor) : base(sensor)
        {
            SensorType = sensor.SensorType;
            SensorStatus = sensor.Status;
            Unit = sensor.Unit;
            StatusComment = sensor.ValidationError;
        }
    }
}
