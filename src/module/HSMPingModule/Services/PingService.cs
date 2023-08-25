using System.Collections.Concurrent;
using HSMPingModule.Collector;
using HSMPingModule.Config;
using Microsoft.Extensions.Options;

namespace HSMPingModule.Services;

internal class PingService : BackgroundService
{
    private readonly ConcurrentDictionary<string, PingAdapter> _pings = new ();
    private readonly DataCollectorWrapper _collectorWrapper;
    private readonly ServiceConfig _config;


    public PingService(IOptionsMonitor<ServiceConfig> config, DataCollectorWrapper collectorWrapper)
    {
        _collectorWrapper = collectorWrapper;
        _config = config.CurrentValue;
        
        foreach (var (hostname, website) in _config.ResourceSettings.WebSites.ToList())
            foreach (var path in website.Countries.Select(country => $"{hostname}/{country}"))
                if (!_pings.TryGetValue(path, out _))
                    _pings.TryAdd(path, new PingAdapter(website, hostname));
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var (path, ping) in _pings)
        {
            _ = Task.Run(async () =>
            {
                var timer = new PeriodicTimer(TimeSpan.FromSeconds(ping.WebSite.PingDelay.Value));
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    _ = ping.SendRequest().ContinueWith((reply) => _collectorWrapper.PingResultSend(ping.WebSite, path, reply), stoppingToken);
                }
            }, stoppingToken);
        }
    }
}
