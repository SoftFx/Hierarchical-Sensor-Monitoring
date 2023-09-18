using HSMPingModule.Models;

namespace HSMPingModule.Services.Interfaces;

internal interface IDataCollectorService
{
    internal Task PingResultSend(WebSite webSite, string country, string hostname, Task<PingResponse> taskReply);

    internal void AddApplicationException(string exceptionMessage);

    internal Task StartAsync();

    internal Task StopAsync();
}