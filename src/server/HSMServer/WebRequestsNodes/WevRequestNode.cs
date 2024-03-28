using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using Microsoft.AspNetCore.Http;

namespace HSMServer.WebRequestsNodes;

public record WebRequestNode
{
    private const double KbDivisor = 1 << 10;
    private const string RecvBytesNode = "Recv Bytes";
    private const string SentBytesNode = "Sent Bytes";
    private const string RecvSensorsNode = "Recv Sensors";
    private const string SentSensorsNode = "Sent Sensors";

    private protected const string ClientNode = "Clients";

    private readonly IInstantValueSensor<double> _sentBytes;
    private readonly IInstantValueSensor<double> _receiveBytes;
    private readonly IInstantValueSensor<double> _sentSensors;
    private readonly IInstantValueSensor<double> _receiveSensors;


    public WebRequestNode(IDataCollector collector, string id)
    {
        _sentBytes = collector.CreateM1RateSensor($"{ClientNode}/{id}/{SentBytesNode}", "Number of bytes that were sent from server to client");
        _receiveBytes = collector.CreateM1RateSensor($"{ClientNode}/{id}/{RecvBytesNode}", "Number of bytes that were received from client");
        _sentSensors = collector.CreateM1RateSensor($"{ClientNode}/{id}/{SentSensorsNode}", "Number of sensors that were sent from server to client");
        _receiveSensors = collector.CreateM1RateSensor($"{ClientNode}/{id}/{RecvSensorsNode}", "Number of sensors that were received from client");
    }


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