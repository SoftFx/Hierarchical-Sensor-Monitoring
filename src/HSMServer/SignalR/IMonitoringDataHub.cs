using System.Collections.Generic;
using System.Threading.Tasks;
using HSMCommon.Model.SensorsData;

namespace HSMServer.SignalR
{
    public interface IMonitoringDataHub
    {
        Task SendSensorUpdates(List<SensorData> data);
    }
}