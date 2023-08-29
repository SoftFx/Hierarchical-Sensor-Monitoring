using System.Collections.Concurrent;
using HSMPingModule.Config;
using HSMPingModule.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace HSMPingModule.Services;

internal class PingService : BackgroundService
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, PingAdapter>> _newPings = new();
    private readonly IDataCollectorService _collectorService;
    private readonly ServiceConfig _config;
    private readonly ILogger<PingService> _logger;

    public PingService(IOptionsMonitor<ServiceConfig> config, IDataCollectorService collectorService, ILogger<PingService> logger)
    {
        _logger = logger;
        
        _collectorService = collectorService;
        _config = config.CurrentValue;
        _config.OnChange += RebuildPings;

        PingAdapter.SendResult += _collectorService.PingResultSend;

        foreach (var (hostname, website) in _config.ResourceSettings.WebSites)
            foreach (var country in website.Countries)
                if (_newPings.TryGetValue(country, out var dict))
                    dict.TryAdd(hostname, new(website, hostname));
                else
                    _newPings.TryAdd(country, new ConcurrentDictionary<string, PingAdapter>()
                    {
                        [hostname] = new (website, hostname)
                    });
    }


    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var (country, pings) in _newPings)
            foreach (var (hostname, ping) in pings)
                _ = ping.StartPinging($"{hostname}/{country}");

        return Task.CompletedTask;
    }


    private void RebuildPings()
    { 
        _logger.LogInformation("Settings file was updated, starting reloading");

        foreach (var (_, pingAdapter) in _newPings.SelectMany(x => x.Value))
            pingAdapter.CancelToken();

        _newPings.Clear();

        foreach (var (hostname, website) in _config.ResourceSettings.WebSites)
            foreach (var country in website.Countries)
            {
                var ping = new PingAdapter(website, hostname);

                if (_newPings.TryGetValue(country, out var dict))
                    dict.TryAdd(hostname, ping);
                else
                    _newPings.TryAdd(country, new ConcurrentDictionary<string, PingAdapter>
                    {
                        [hostname] = ping
                    });

                ping.StartPinging($"{hostname}/{country}");
            }
    }
}
