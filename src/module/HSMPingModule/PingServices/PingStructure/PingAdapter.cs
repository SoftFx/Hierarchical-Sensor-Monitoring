using HSMPingModule.Settings;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace HSMPingModule.PingServices;

internal sealed class PingAdapter : Ping
{
    private readonly byte[] _buffer = new byte[32];

    private readonly CancellationTokenSource _token = new();
    private readonly PingOptions _options = new();

    private readonly NodeSettings _webSite;
    private readonly string _hostName;
    private readonly string _country;

    public event Func<NodeSettings, string, string, Task<PingResponse>, Task> SendResult;


    internal string SensorPath { get; }


    public PingAdapter(NodeSettings webSite, string host, string country) : base()
    {
        _webSite = webSite;
        _hostName = host;
        _country = country;

        SensorPath = $"{_hostName}/{_country}";
    }


    public Task Ping() => SendPingRequest().ContinueWith(reply => SendResult?.Invoke(_webSite, _country, _hostName, reply), _token.Token).Unwrap();


    private async Task<PingResponse> SendPingRequest()
    {
        try
        {
            return new PingResponse(await SendPingAsync(_hostName, _webSite.PingThresholdValue.Value, _buffer, _options));
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