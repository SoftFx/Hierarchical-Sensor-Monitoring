using HSMServer.Core.Model.Sensor;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.SignalR
{
    public interface IMonitoringDataHub
    {
        Task SendSensorUpdates(List<SensorData> data);
    }
}