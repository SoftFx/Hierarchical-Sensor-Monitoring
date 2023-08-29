using System.Collections.Concurrent;
using HSMPingModule.Config;
using HSMPingModule.Resourses;
using HSMPingModule.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace HSMPingModule.Services;

internal class PingService : BackgroundService
{
    private readonly ConcurrentDictionary<string, PingAdapter> _pings = new ();
    private readonly IDataCollectorService _collectorService;
    private readonly ServiceConfig _config;

    public PingService(IOptionsMonitor<ServiceConfig> config, IDataCollectorService collectorService)
    {
        _collectorService = collectorService;
        _config = config.CurrentValue;
        _config.OnChange += OnChange;

        foreach (var (hostname, website) in _config.ResourceSettings.WebSites.ToList())
            foreach (var path in website.Countries.Select(country => $"{hostname}/{country}"))
                if (!_pings.TryGetValue(path, out _))
                    _pings.TryAdd(path, new PingAdapter(website, hostname));
    }


    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var (path, ping) in _pings)
            _ = ping.StartPinging(path, _collectorService.PingResultSend);

        return Task.CompletedTask;
    }


    private void OnChange()
    {
        foreach (var (hostname, website) in _config.ResourceSettings.WebSites.ToList())
            foreach (var path in website.Countries.Select(country => $"{hostname}/{country}"))
                if (_pings.TryGetValue(path, out var currentPing) )
                {
                    if (!currentPing.WebSite.Equals(website) && _pings.TryRemove(path, out currentPing))
                    {
                        currentPing.CancelToken();
                        RegisterNewAdapter(website, hostname, path);
                    }
                }
                else
                    RegisterNewAdapter(website, hostname, path);
    }

    private void RegisterNewAdapter(WebSite website, string hostname, string path)
    {
        var newPing = new PingAdapter(website, hostname);

        if (_pings.TryAdd(path, newPing))
            newPing.StartPinging(path, _collectorService.PingResultSend);
    }
}
