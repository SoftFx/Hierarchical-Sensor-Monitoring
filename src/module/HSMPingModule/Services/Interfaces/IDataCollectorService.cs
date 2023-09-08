using HSMPingModule.Models;

namespace HSMPingModule.Services.Interfaces;

internal interface IDataCollectorService
{
    internal Task PingResultSend(WebSite webSite, string country, string hostname, Task<PingResponse> taskReply);

    internal Task StartAsync();

    internal Task StopAsync();
}