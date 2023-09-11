using System.Collections.Concurrent;
using HSMPingModule.Config;
using HSMPingModule.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace HSMPingModule.Services;

internal class PingService : BackgroundService
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, PingAdapter>> _newPings = new();
    private readonly IDataCollectorService _collectorService;
    private readonly ILogger<PingService> _logger;
    private readonly ServiceConfig _config;


    public PingService(IOptionsMonitor<ServiceConfig> config, IDataCollectorService collectorService, ILogger<PingService> logger)
    {
        _logger = logger;
        
        _collectorService = collectorService;
        _config = config.CurrentValue;
        _config.OnChange += RebuildPings;

        foreach (var (hostname, website) in _config.ResourceSettings.WebSites)
            foreach (var country in website.Countries)
                if (_newPings.TryGetValue(country, out var dict))
                    dict.TryAdd(hostname, new(website, hostname, country));
                else
                    _newPings.TryAdd(country, new ConcurrentDictionary<string, PingAdapter>()
                    {
                        [hostname] = new (website, hostname, country)
                    });
    }


    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _collectorService.StartAsync();

        foreach (var (country, pings) in _newPings)
            foreach (var (_, ping) in pings)
            {
                ping.SendResult += _collectorService.PingResultSend;
                _ = ping.StartPinging();
            }

        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _collectorService.StopAsync();
        return base.StopAsync(cancellationToken);
    }


    private void RebuildPings()
    { 
        _logger.LogInformation("Settings file was updated, starting reloading");

        foreach (var (_, pingAdapter) in _newPings.SelectMany(x => x.Value))
        {
            pingAdapter.CancelToken();
            pingAdapter.SendResult -= _collectorService.PingResultSend;
        }

        _newPings.Clear();

        foreach (var (hostname, website) in _config.ResourceSettings.WebSites)
            foreach (var country in website.Countries)
            {
                var ping = new PingAdapter(website, hostname, country);
                ping.SendResult += _collectorService.PingResultSend;

                if (_newPings.TryGetValue(country, out var dict))
                    dict.TryAdd(hostname, ping);
                else
                    _newPings.TryAdd(country, new ConcurrentDictionary<string, PingAdapter>
                    {
                        [hostname] = ping
                    });

                _ = ping.StartPinging();
                _logger.LogInformation("New pinging sensor added at {path}", _newPings[country][hostname].SensorPath);
            }
    }
}
