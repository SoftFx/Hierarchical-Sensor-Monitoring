using HSMClientWPFControls.ViewModel;

namespace HSMClientWPFControls.SensorExpandingService
{
    public interface ISensorExpandingService
    {
        public void Expand(MonitoringSensorViewModel sensor);
    }
}