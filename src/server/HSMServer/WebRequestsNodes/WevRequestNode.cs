using HSMDataCollector.PublicInterface;
using Microsoft.AspNetCore.Http;

namespace HSMServer.WebRequestsNodes;

public record WebRequestNode
{
    private const double KbDivisor = 1 << 10;


    public required IInstantValueSensor<double> SentBytes { get; init; }
    
    public required IInstantValueSensor<double> ReceiveBytes { get; init; }
    
    public required IInstantValueSensor<double> SentSensors { get; init; }
    
    public required IInstantValueSensor<double> ReceiveSensors { get; init; }


    public WebRequestNode() { }


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