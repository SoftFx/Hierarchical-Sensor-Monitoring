using HSMPingModule.PingServices;

namespace HSMPingModule.DataCollectorWrapper;

internal interface IDataCollectorWrapper
{
    internal void SendPingResult(ResourceSensor resource, PingResponse pingResponse);

    internal void AddApplicationException(string exceptionMessage);

    internal Task Start();

    internal Task Stop();
}