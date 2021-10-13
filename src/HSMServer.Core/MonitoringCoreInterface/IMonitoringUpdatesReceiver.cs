using HSMServer.Core.Model.Sensor;

namespace HSMServer.Core.MonitoringCoreInterface
{
    public interface IMonitoringUpdatesReceiver
    {
        void AddUpdate(SensorData update);
    }
}