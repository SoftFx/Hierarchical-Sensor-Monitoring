using System.Net.NetworkInformation;
using System.Net.Sockets;
using HSMPingModule.Resourses;

namespace HSMPingModule.Services;

internal sealed class PingAdapter : Ping
{
    private readonly byte[] _buffer = new byte[32];
    private readonly PingOptions _options = new();
    private readonly CancellationTokenSource _token = new ();


    public WebSite WebSite { get; }

    public string HostName { get; }



    public PingAdapter(WebSite webSite, string host) : base()
    {
        WebSite = webSite;
        HostName = host;
    }


    public async Task<PingResponse> SendPingRequest()
    {
        try
        {
            return new PingResponse(await SendPingAsync(HostName, WebSite.PingTimeoutValue.Value, _buffer, _options));
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


    public Task StartPinging(string path, Func<WebSite, string, Task<PingResponse>, Task> callBackFunc) => Task.Run(async () =>
    {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(WebSite.PingDelay.Value));

        while (await timer.WaitForNextTickAsync(_token.Token))
            _ = SendPingRequest().ContinueWith((reply) => callBackFunc(WebSite, path, reply), _token.Token).Unwrap();
    }, _token.Token);


    internal void CancelToken() => _token.Cancel();
}