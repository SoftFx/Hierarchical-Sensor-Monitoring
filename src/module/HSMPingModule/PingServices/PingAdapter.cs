using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace HSMPingModule.PingServices;

internal sealed class PingAdapter : Ping
{
    private static readonly PingOptions _options = new();

    private readonly byte[] _buffer = new byte[32];

    private readonly string _hostName;
    private readonly int _timeoutMsc;


    internal string SensorPath { get; }


    public PingAdapter(string host, int timeoutMsc) : base()
    {
        _timeoutMsc = timeoutMsc;
        _hostName = host;
    }


    internal async Task<PingResponse> SendPingRequest()
    {
        try
        {
            return new PingResponse(await SendPingAsync(_hostName, _timeoutMsc, _buffer, _options));
        }
        catch (Exception ex)
        {
            return ex.InnerException switch
            {
                SocketException socketException => new PingResponse(socketException),
                _ => new PingResponse(ex)
            };
        }
    }
}