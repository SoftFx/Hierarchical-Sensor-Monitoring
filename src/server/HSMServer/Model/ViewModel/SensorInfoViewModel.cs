using HSMServer.Core.Model;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.ViewModel
{
    public class SensorInfoViewModel : NodeInfoBaseViewModel
    {
        public SensorType SensorType { get; }

        public string Unit { get; set; }


        // public constructor without parameters for action Home/UpdateSensorInfo
        public SensorInfoViewModel() : base() { }

        internal SensorInfoViewModel(SensorNodeViewModel sensor) : base(sensor)
        {
            SensorType = sensor.Type;
            Unit = sensor.Unit;
        }
    }
}
