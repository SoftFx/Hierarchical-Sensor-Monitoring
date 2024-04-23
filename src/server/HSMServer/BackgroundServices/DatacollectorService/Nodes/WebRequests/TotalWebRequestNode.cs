using HSMDataCollector.Core;

namespace HSMServer.BackgroundServices;

public sealed record TotalWebRequestNode : WebRequestNode
{
    public TotalWebRequestNode(IDataCollector collector) : base(collector, ClientStatisticsSensors.TotalGroup)
    {
    }
}