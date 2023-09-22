using HSMSensorDataObjects;
using System.Net.NetworkInformation;
using System.Text;

namespace HSMPingModule.PingServices;

internal record PingResponse
{
    private const double Milleseconds = 1000;

    private readonly string _str;


    public SensorStatus Status { get; init; }

    public double Value { get; init; }

    public string Comment { get; init; }


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

        _str = BuildStrState();
    }

    public PingResponse(Exception exception)
    {
        Status = SensorStatus.Error;
        Comment = exception.Message;
        Value = 0;

        _str = BuildStrState();
    }


    public override string ToString() => _str;


    private string BuildStrState()
    {
        var sb = new StringBuilder(1 << 5);

        sb.Append($"{nameof(Status)}={Status}, ")
          .Append($"{nameof(Value)}={Value}, ")
          .Append($"{nameof(Comment)}={Comment}");

        return sb.ToString();
    }
}
