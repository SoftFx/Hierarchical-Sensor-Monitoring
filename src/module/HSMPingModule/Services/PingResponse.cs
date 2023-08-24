using System.Net.NetworkInformation;
using HSMSensorDataObjects;

namespace HSMPingModule;

internal record PingResponse
{
    public int Value { get; init; }

    public SensorStatus Status { get; init; }

    public string Comment { get; init; }


    public PingResponse(PingReply reply)
    {
        if (reply.Status is IPStatus.Success)
        {
            Status = SensorStatus.Ok;
            Comment = string.Empty;
            Value = (int)reply.RoundtripTime;
        }
        else
        {
            Status = SensorStatus.Error;
            Comment = reply.Status.ToString();
            Value = (int)reply.RoundtripTime;
        }
    }

    public PingResponse(Exception exception)
    {
        Status = SensorStatus.Error;
        Comment = exception.Message;
        Value = 0;
    }
}
