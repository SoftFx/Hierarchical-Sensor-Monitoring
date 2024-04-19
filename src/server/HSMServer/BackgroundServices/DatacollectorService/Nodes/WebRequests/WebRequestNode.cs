using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects.SensorRequests;
using Microsoft.AspNetCore.Http;

namespace HSMServer.BackgroundServices;

public record WebRequestNode
{
    private const double KbDivisor = 1 << 10;

    private const string RecvSensorsNode = "Sensors updates";
    private const string SentBytesNode = "Traffic Out";
    private const string RecvBytesNode = "Traffic In";
    private const string ClientNode = "Clients";

    private readonly IInstantValueSensor<double> _receiveSensors;
    private readonly IInstantValueSensor<double> _receiveBytes;
    private readonly IInstantValueSensor<double> _sentBytes;


    public WebRequestNode(IDataCollector collector, string id)
    {
        _receiveSensors = collector.CreateRateSensor(BuildSensorPath(id, RecvSensorsNode), new RateSensorOptions
        {
            Alerts = [],
            EnableForGrafana = true,
            Description = "Number of sensors that were updated from client."
        });

        _receiveBytes = collector.CreateRateSensor(BuildSensorPath(id, RecvBytesNode), new RateSensorOptions
        {
            Alerts = [],
            SensorUnit = Unit.KBytes_sec,
            EnableForGrafana = true,
            Description = "Number of KB that were received from client via server public API."
        });

        _sentBytes = collector.CreateRateSensor(BuildSensorPath(id, SentBytesNode), new RateSensorOptions
        {
            Alerts = [],
            SensorUnit = Unit.KBytes_sec,
            EnableForGrafana = true,
            Description = "Number of KB that were sent from server to client via server public API."
        });
    }


    private protected static string BuildSensorPath(string id, string sensorName) => $"{ClientNode}/{id}/{sensorName}";


    public virtual void AddRequestData(HttpRequest request)
    {
        _receiveBytes.AddValue((request.ContentLength ?? 0) / KbDivisor);
    }

    public void AddResponseResult(HttpResponse response)
    {
        _sentBytes.AddValue((response.ContentLength ?? 0) / KbDivisor);
    }

    public void AddReceiveData(int count)
    {
        if (count != 0)
            _receiveSensors.AddValue(count);
    }
}