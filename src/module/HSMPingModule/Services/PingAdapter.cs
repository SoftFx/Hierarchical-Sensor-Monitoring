using HSMPingModule.Models;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace HSMPingModule.Services;

internal sealed class PingAdapter : Ping
{
    private readonly byte[] _buffer = new byte[32];

    private readonly CancellationTokenSource _token = new();
    private readonly PingOptions _options = new();

    private readonly WebSite _webSite;
    private readonly string _hostName;

    public event Func<WebSite, string, Task<PingResponse>, Task> SendResult;


    internal string SensorPath { get; }


    public PingAdapter(WebSite webSite, string host, string country) : base()
    {
        _webSite = webSite;
        _hostName = host;

        SensorPath = $"{_hostName}/{country}";
    }


    public async Task<PingResponse> SendPingRequest()
    {
        try
        {
            return new PingResponse(await SendPingAsync(_hostName, _webSite.PingTimeoutValue.Value, _buffer, _options));
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


    public async Task StartPinging()
    {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(_webSite.PingDelay.Value));

        while (await timer.WaitForNextTickAsync(_token.Token))
            _ = SendPingRequest().ContinueWith(reply => SendResult?.Invoke(_webSite, SensorPath, reply), _token.Token).Unwrap();
    }


    internal void CancelToken() => _token.Cancel();
}