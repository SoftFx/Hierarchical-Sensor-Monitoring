using System.Net.NetworkInformation;
using System.Net.Sockets;
using HSMPingModule.Resourses;

namespace HSMPingModule.Services;

internal class PingAdapter : Ping
{
    private readonly byte[] _buffer = new byte[32];
    private readonly PingOptions _options = new ();
    
    
    public WebSite WebSite { get; }
    public string HostName { get; }


    public PingAdapter(WebSite webSite, string host) : base()
    {
        WebSite = webSite;
        HostName = host;
    }


    public async Task<PingResponse> SendRequest()
    {
        return await Ping();
    }

    private async Task<PingResponse> Ping()
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
            };;
        }
    }
}