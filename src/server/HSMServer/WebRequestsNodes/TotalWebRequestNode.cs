using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using Microsoft.AspNetCore.Http;

namespace HSMServer.WebRequestsNodes;

public record TotalWebRequestNode : WebRequestNode
{
    private const string RequestPerSecondNode = "RPS";
    
    public IInstantValueSensor<double> RPS { get; init; }

    public override void AddRequestData(HttpRequest request)
    {
        RPS?.AddValue(1);
        base.AddRequestData(request);
    }

    public TotalWebRequestNode(IDataCollector collector, string id) : base(collector, id)
    {
        RPS = collector.CreateM1RateSensor($"{ClientNode}/{id}/{RequestPerSecondNode}",
            "Number of requests that were receive");
    }
}