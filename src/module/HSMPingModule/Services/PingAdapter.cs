using System.Net;
using System.Net.NetworkInformation;

namespace HSMPingModule.Services;

internal class PingAdapter : Ping
{
    private readonly byte[] _buffer = new byte[32];
    private readonly IPAddress[] _ips;
    private readonly PingResponse _dnsException;
    private readonly PingOptions _options = new ();


    public PingAdapter(string host) : base()
    {
        try
        {
            _ips = Dns.GetHostAddressesAsync(host).Result;
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
            return new PingResponse(await SendPingAsync(_ips[0], PingService.PingTimout, _buffer, _options));
        }
        catch (Exception ex)
        {
            return new PingResponse(ex);
        }
    }
}