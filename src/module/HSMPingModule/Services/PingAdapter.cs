using System.Net.NetworkInformation;
using System.Net.Sockets;
using HSMPingModule.Resourses;

namespace HSMPingModule.Services;

internal class PingAdapter : Ping
{
    private readonly byte[] _buffer = new byte[32];
    private readonly PingOptions _options = new();


    public WebSite WebSite { get; }

    public string HostName { get; }

    public CancellationTokenSource Token { get; set; }


    public PingAdapter(WebSite webSite, string host) : base()
    {
        WebSite = webSite;
        HostName = host;
        Token = new CancellationTokenSource();
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
        while (await timer.WaitForNextTickAsync(Token.Token))
        {
            _ = SendPingRequest().ContinueWith((reply) => callBackFunc(WebSite, path, reply), Token.Token);
        }
    }, Token.Token);


    public void CancelToken() => Token.Cancel();
}