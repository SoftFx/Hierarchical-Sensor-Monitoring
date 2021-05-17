using System.Collections.Generic;
using System.Threading.Tasks;
using HSMCommon.Model.SensorsData;
using Microsoft.AspNetCore.SignalR;

namespace HSMServer.SignalR
{
    public interface IMonitoringDataHub
    {
        HubCallerContext CallerContext { get; }
        Task SendSensorUpdates(List<SensorData> data);
    }
}