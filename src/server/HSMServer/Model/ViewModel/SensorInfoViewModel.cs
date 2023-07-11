using HSMServer.Core;
using HSMServer.Core.Model;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.ViewModel
{
    public sealed class SensorInfoViewModel : NodeInfoBaseViewModel
    {
        public SensorType SensorType { get; }

        public string StatusComment { get; }

        public bool HasGrafana { get; }

        public bool IsMuted { get; }


        // public constructor without parameters for action Home/UpdateSensorInfo
        public SensorInfoViewModel() : base() { }

        internal SensorInfoViewModel(SensorNodeViewModel sensor) : base(sensor)
        {
            SensorType = sensor.Type;
            StatusComment = sensor.ValidationError;
            HasGrafana = sensor.Integration.HasGrafana();
            IsMuted = sensor.State == SensorState.Muted;
        }
    }
}
