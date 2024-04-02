using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMServer.BackgroundServices;
using Microsoft.AspNetCore.Http;

namespace HSMServer.WebRequestsNodes;

public sealed record TotalWebRequestNode : WebRequestNode
{
    private const string RequestPerSecondNode = "RPS";

    private readonly IInstantValueSensor<double> _rps;


    public TotalWebRequestNode(IDataCollector collector) : base(collector, ClientStatistics.TotalGroup)
    {
        _rps = collector.CreateM1RateSensor(BuildSensorPath(ClientStatistics.TotalGroup, RequestPerSecondNode), "Number of requests that were received.");
    }


    public override void AddRequestData(HttpRequest request)
    {
        _rps.AddValue(1);
        base.AddRequestData(request);
    }
}