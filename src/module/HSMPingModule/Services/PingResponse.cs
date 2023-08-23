using System.Net.NetworkInformation;
using HSMSensorDataObjects;

namespace HSMPingModule;

internal record PingResponse
{
    public bool Value { get; init; }
    
    public SensorStatus Status { get; init; }
    
    public string Comment { get; init; }
    
    
    public PingResponse(bool value, SensorStatus status, string comment)
    {
        Value = value;
        Status = status;
        Comment = comment;
    }
    
    public PingResponse(PingReply reply)
    {
        if (reply.Status is IPStatus.Success)
        {
            Status = SensorStatus.Ok;
            Comment = string.Empty;
            Value = true;
        }
        else
        {
            Status = SensorStatus.Error;
            Comment = reply.Status.ToString();
            Value = false;
        }
    }
}
