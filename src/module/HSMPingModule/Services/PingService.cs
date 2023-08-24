using System.Collections.Concurrent;
using HSMPingModule.Collector;
using HSMPingModule.Config;
using Microsoft.Extensions.Options;

namespace HSMPingModule.Services;

internal class PingService : BackgroundService
{
    public const int PingTimout = int.MaxValue;
    public const int SensorPingTimout = 30_000; // 30sec
    private const int Delay = 15;

    private readonly ConcurrentDictionary<string, PingAdapter> _pings = new ();
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
            foreach (var website in _config.ResourceSettings.WebSites.ToList().Distinct())
                foreach (var country in website.Countries)
                {
                    PingAdapter ping;
                    var path = $"{website.HostName}/{country}";
                    if (!_pings.TryGetValue(path, out ping))
                    {
                        ping = new PingAdapter(website.HostName);
                        _pings.TryAdd(path, ping);
                    }
                    
                    _ = ping.SendRequest().ContinueWith((reply) => _collectorWrapper.PingResultSend(website, country, reply), stoppingToken);
                }
        }
    }
}
