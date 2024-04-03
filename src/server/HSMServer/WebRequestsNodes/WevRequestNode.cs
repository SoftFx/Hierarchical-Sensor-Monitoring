using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using Microsoft.AspNetCore.Http;

namespace HSMServer.WebRequestsNodes;

public record WebRequestNode
{
    private const double KbDivisor = 1 << 10;

    private const string RecvSensorsNode = "Recv Sensors";
    private const string RecvBytesNode = "Recv Bytes";
    private const string SentBytesNode = "Sent Bytes";
    private const string ClientNode = "Clients";

    private readonly IInstantValueSensor<double> _receiveSensors;
    private readonly IInstantValueSensor<double> _receiveBytes;
    private readonly IInstantValueSensor<double> _sentBytes;


    public WebRequestNode(IDataCollector collector, string id)
    {
        _receiveSensors = collector.CreateM1RateSensor(BuildSensorPath(id, RecvSensorsNode), "Number of sensors that were received from client.");
        _receiveBytes = collector.CreateM1RateSensor(BuildSensorPath(id, RecvBytesNode), "Number of bytes that were received from client.");
        _sentBytes = collector.CreateM1RateSensor(BuildSensorPath(id, SentBytesNode), "Number of bytes that were sent from server to client.");
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