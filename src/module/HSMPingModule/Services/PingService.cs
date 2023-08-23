using System.Net;
using System.Net.NetworkInformation;
using HSMPingModule.Collector;
using HSMPingModule.Config;
using HSMSensorDataObjects;
using Microsoft.Extensions.Options;

namespace HSMPingModule.Services;

internal class PingService : BackgroundService
{
    private const int PingTimout = 1000;


    private readonly DataCollectorWrapper _collectorWrapper;
    private readonly PingConfig _config;
    private readonly List<string> _webSites;
    private readonly List<string> _contries;
    private readonly int _delay = 15;


    public PingService(IOptions<PingConfig> config, DataCollectorWrapper collectorWrapper)
    {
        _collectorWrapper = collectorWrapper;
        _config = config.Value;
        _contries = _config.VpnSettings.Countries;
        _webSites = _config.VpnSettings.WebSites;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(_delay));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var country in _contries.Distinct())
                foreach (var host in _webSites)
                    Ping(host).ContinueWith((reply) => _collectorWrapper.PingResultSend(host, country, reply.Result));
        }
    }


    private async Task<PingResponse> Ping(string host)
    {
        var myPing = new Ping();
        var buffer = new byte[32];
        var pingOptions = new PingOptions();

        try
        {
            var ips = await Dns.GetHostAddressesAsync(host);
            
            return new PingResponse(await myPing.SendPingAsync(ips[0], PingTimout, buffer, pingOptions));
        }
        catch (Exception ex)
        {
            return new PingResponse(false, SensorStatus.Error, ex.Message);
        }
    }
}
