using HSMPingModule.PingServices;
using HSMPingModule.SensorStructure;

namespace HSMPingModule.DataCollectorWrapper;

internal interface IDataCollectorWrapper
{
    internal ApplicationNode AppNode { get; }

    internal TimeSpan PostPeriod { get; }


    internal Task Start();

    internal Task Stop();


    internal void SendPingResult(ResourceSensor resource, List<PingResponse> pingResponse);
}