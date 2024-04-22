using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using Microsoft.AspNetCore.Http;

namespace HSMServer.BackgroundServices;

public sealed record TotalWebRequestNode : WebRequestNode
{
    private const string RequestPerSecondNode = "Clients requests count";

    private readonly IInstantValueSensor<double> _rps;


    public TotalWebRequestNode(IDataCollector collector) : base(collector, ClientStatisticsSensors.TotalGroup)
    {
        _rps = collector.CreateRateSensor(BuildSensorPath(ClientStatisticsSensors.TotalGroup, RequestPerSecondNode), new RateSensorOptions
        {
            Alerts = [],
            EnableForGrafana = true,
            Description = "Total number of public API client requests."
        });
    }


    public override void AddRequestData(HttpRequest request)
    {
        _rps.AddValue(1);

        base.AddRequestData(request);
    }
}