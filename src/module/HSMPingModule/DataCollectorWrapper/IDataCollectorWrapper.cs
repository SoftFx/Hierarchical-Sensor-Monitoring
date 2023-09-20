using HSMPingModule.Models;

namespace HSMPingModule.DataCollectorWrapper;

internal interface IDataCollectorWrapper
{
    internal Task PingResultSend(WebSite webSite, string country, string hostname, Task<PingResponse> taskReply);

    internal void AddApplicationException(string exceptionMessage);

    internal Task Start();

    internal Task Stop();
}