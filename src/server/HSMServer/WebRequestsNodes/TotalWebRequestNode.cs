using HSMDataCollector.PublicInterface;
using Microsoft.AspNetCore.Http;

namespace HSMServer.WebRequestsNodes;

public record TotalWebRequestNode : WebRequestNode
{
    public required IInstantValueSensor<double> RPS { get; init; }

    public override void AddRequestData(HttpRequest request)
    {
        RPS?.AddValue(1);
        base.AddRequestData(request);
    }
}