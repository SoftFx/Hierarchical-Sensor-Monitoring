using HSMSensorDataObjects;
using System.Net.NetworkInformation;

namespace HSMPingModule;

internal record PingResponse
{
    private const double Milleseconds = 1000;


    public SensorStatus Status { get; init; }

    public double Value { get; init; }

    public string Comment { get; init; }


    public bool IsException { get; }


    public PingResponse(PingReply reply)
    {
        if (reply.Status is IPStatus.Success)
        {
            Status = SensorStatus.Ok;
            Comment = string.Empty;
            Value = reply.RoundtripTime / Milleseconds;
        }
        else
        {
            Status = SensorStatus.Error;
            Comment = reply.Status.ToString();
            Value = reply.RoundtripTime / Milleseconds;
        }
    }

    public PingResponse(Exception exception)
    {
        Status = SensorStatus.Error;
        Comment = exception.Message;
        Value = 0;
        IsException = true;
    }
}