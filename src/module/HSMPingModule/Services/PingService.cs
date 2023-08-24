using System.Collections.Concurrent;
using HSMPingModule.Collector;
using HSMPingModule.Config;
using Microsoft.Extensions.Options;

namespace HSMPingModule.Services;

internal class PingService : BackgroundService
{
    public const int PingTimout = int.MaxValue;
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
            foreach (var country in _config.ResourceSettings.Countries.ToList().Distinct())
                foreach (var host in _config.ResourceSettings.WebSites.ToList())
                {
                    PingAdapter ping;
                    
                    if (!_pings.TryGetValue($"{country}/{host}", out ping))
                    {
                        var path = $"{country}/{host}";
                        ping = new PingAdapter(host);
                        _pings.TryAdd(path, ping);
                    }
                    
                    _ = ping.SendRequest().ContinueWith((reply) => _collectorWrapper.PingResultSend(host, country, reply), stoppingToken);
                }
        }
    }
}
