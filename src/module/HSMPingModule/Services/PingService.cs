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
    private const int Delay = 15;

    private readonly DataCollectorWrapper _collectorWrapper;
    private readonly ServiceConfig _config;


    public PingService(IOptionsMonitor<ServiceConfig> config, DataCollectorWrapper collectorWrapper)
    {
        _collectorWrapper = collectorWrapper;
        _config = config.CurrentValue;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(Delay));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var country in _config.ResourceSettings.Countries.ToList().Distinct())
                foreach (var host in _config.ResourceSettings.WebSites.ToList())
                    _ = Ping(host).ContinueWith((reply) => _collectorWrapper.PingResultSend(host, country, reply.Result), stoppingToken);
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
