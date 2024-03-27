using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using Microsoft.AspNetCore.Http;

namespace HSMServer.WebRequestsNodes;

public record TotalWebRequestNode : WebRequestNode
{
    private const string RequestPerSecondNode = "RPS";

    private readonly IInstantValueSensor<double> _rps;

    public override void AddRequestData(HttpRequest request)
    {
        _rps?.AddValue(1);
        base.AddRequestData(request);
    }

    public TotalWebRequestNode(IDataCollector collector, string id) : base(collector, id)
    {
        _rps = collector.CreateM1RateSensor($"{ClientNode}/{id}/{RequestPerSecondNode}", "Number of requests that were receive");
    }
}