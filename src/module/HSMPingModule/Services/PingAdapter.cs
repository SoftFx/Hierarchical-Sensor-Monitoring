using System.Net;
using System.Net.NetworkInformation;
using HSMPingModule.Resourses;

namespace HSMPingModule.Services;

internal class PingAdapter : Ping
{
    private readonly byte[] _buffer = new byte[32];
    private readonly IPAddress[] _ips;
    private readonly PingResponse _dnsException;
    private readonly PingOptions _options = new ();
    
    
    public WebSite WebSite { get; }
    public string HostName { get; }


    public PingAdapter(WebSite webSite, string host) : base()
    {
        try
        {
            WebSite = webSite;
            HostName = host;
            _ips = Dns.GetHostAddressesAsync(HostName).Result;
        }
        catch (Exception ex)
        {
            _dnsException = new PingResponse(ex);
        }
    }


    public async Task<PingResponse> SendRequest()
    {
        if (_dnsException is not null)
            return _dnsException;

        return await Ping();
    }

    private async Task<PingResponse> Ping()
    {
        try
        {
            return new PingResponse(await SendPingAsync(_ips[0], WebSite.PingTimeoutValue.Value, _buffer, _options));
        }
        catch (Exception ex)
        {
            return new PingResponse(ex);
        }
    }
}