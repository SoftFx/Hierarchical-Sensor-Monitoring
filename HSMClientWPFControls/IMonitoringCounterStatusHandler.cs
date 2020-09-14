using HSMClientWPFControls.ViewModel;

namespace HSMClientWPFControls
{
    public interface IMonitoringCounterStatusHandler
    {
        void UpdateStatus(MonitoringCounterBaseViewModel counter);
    }
}
