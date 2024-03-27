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

    public IInstantValueSensor<double> SentBytes { get; init; }
    
    public IInstantValueSensor<double> ReceiveBytes { get; init; }
    
    public IInstantValueSensor<double> SentSensors { get; init; }
    
    public IInstantValueSensor<double> ReceiveSensors { get; init; }


    public WebRequestNode(IDataCollector collector, string id)
    {
        SentBytes = collector.CreateM1RateSensor($"{ClientNode}/{id}/{SentBytesNode}", "Number of bytes that were sent from server to client");
        ReceiveBytes = collector.CreateM1RateSensor($"{ClientNode}/{id}/{RecvBytesNode}", "Number of bytes that were received from client");
        SentSensors = collector.CreateM1RateSensor($"{ClientNode}/{id}/{SentSensorsNode}", "Number of sensors that were sent from server to client");
        ReceiveSensors = collector.CreateM1RateSensor($"{ClientNode}/{id}/{RecvSensorsNode}", "Number of sensors that were received from client");
    }


    public virtual void AddRequestData(HttpRequest request)
    {
        ReceiveBytes.AddValue((request.ContentLength ?? 0) / KbDivisor);
    }

    public void AddResponseResult(HttpResponse response)
    {
        SentBytes.AddValue((response.ContentLength ?? 0) / KbDivisor);
    }

    public void AddReceiveData(int count)
    {
        if (count != 0)
            ReceiveSensors.AddValue(count);
    }
}