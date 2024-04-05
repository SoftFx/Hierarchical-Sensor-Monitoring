using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects.SensorRequests;
using Microsoft.AspNetCore.Http;
using System;

namespace HSMServer.WebRequestsNodes;

public record WebRequestNode
{
    private const int OneMinute = 60000;
    private const double KbDivisor = 1 << 10;

    private const string RecvSensorsNode = "Recv Sensors";
    private const string RecvBytesNode = "Received";
    private const string SentBytesNode = "Sent";
    private const string ClientNode = "Clients";

    private readonly IInstantValueSensor<double> _receiveSensors;
    private readonly IInstantValueSensor<double> _receiveBytes;
    private readonly IInstantValueSensor<double> _sentBytes;

    private readonly TimeSpan _postDataPeriod = TimeSpan.FromMilliseconds(OneMinute);


    public WebRequestNode(IDataCollector collector, string id)
    {
        _receiveSensors = collector.CreateRateSensor(BuildSensorPath(id, RecvSensorsNode), new RateSensorOptions
        {
            PostDataPeriod = _postDataPeriod,
            Description = "Number of sensors that were received from client."
        });

        _receiveBytes = collector.CreateRateSensor(BuildSensorPath(id, RecvBytesNode), new RateSensorOptions
        {
            SensorUnit = Unit.KBytes_sec,
            PostDataPeriod = _postDataPeriod,
            Description = "Number of KB that were received from client."
        });

        _sentBytes = collector.CreateRateSensor(BuildSensorPath(id, SentBytesNode), new RateSensorOptions
        {
            SensorUnit = Unit.KBytes_sec,
            PostDataPeriod = _postDataPeriod,
            Description = "Number of KB that were sent from server to client."
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