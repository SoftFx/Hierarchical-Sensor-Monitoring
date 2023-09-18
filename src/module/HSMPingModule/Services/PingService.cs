using System.Collections.Concurrent;
using HSMPingModule.Config;
using HSMPingModule.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace HSMPingModule.Services;

internal class PingService : BackgroundService
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, PingAdapter>> _newPings = new();
    private readonly CancellationTokenSource _tokenSource = new ();
    private readonly IDataCollectorService _collectorService;
    private readonly ILogger<PingService> _logger;
    private readonly VpnService _service;
    private readonly ServiceConfig _config;


    public PingService(IOptionsMonitor<ServiceConfig> config, IDataCollectorService collectorService, ILogger<PingService> logger, VpnService service)
    {
        _logger = logger;
        _service = service;

        _collectorService = collectorService;
        _config = config.CurrentValue;
        _config.OnChange += RebuildPings;

        InitPings();
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _collectorService.StartAsync();

        _ = StartPinging();
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _collectorService.StopAsync();
        return base.StopAsync(cancellationToken);
    }


    private void RebuildPings()
    { 
        _logger.LogInformation("Settings file was updated, starting reloading");
        
        CancelAndResetToken();

        InitPings();
    }

    private async Task StartPinging()
    {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(_config.ResourceSettings.DefaultSiteNodeSettings.PingRequestDelaySec.Value));
        var sendingPings = new List<Task>();

        while (await timer.WaitForNextTickAsync(_tokenSource.Token))
            foreach (var (country, pings) in _newPings)
            {
                if (await _service.ChangeCountry(country, out var result))
                {
                    foreach (var (_, ping) in pings)
                        sendingPings.Add(Task.Run(() => ping.Ping()));

                    await Task.WhenAll(sendingPings).ContinueWith(_ => sendingPings.Clear());
                }
                else
                {
                    _logger.LogInformation("Couldn't change country to {0}", country);
                    _collectorService.AddApplicationException($"Error occured during country change to {country}. Error message: {result}");
                }
            }
    }

    private void InitPings()
    {
        foreach (var (_, pingAdapter) in _newPings.SelectMany(x => x.Value))
            pingAdapter.SendResult -= _collectorService.PingResultSend;

        _newPings.Clear();

        foreach (var (hostname, website) in _config.ResourceSettings.WebSites)
            foreach (var country in website.Countries)
            {
                var ping = new PingAdapter(website, hostname, country);

                if (_newPings.TryGetValue(country, out var dict))
                    dict.TryAdd(hostname, ping);
                else
                    _newPings.TryAdd(country, new ConcurrentDictionary<string, PingAdapter>
                    {
                        [hostname] = ping
                    });

                ping.SendResult += _collectorService.PingResultSend;

                _logger.LogInformation("New pinging sensor added at {path}", _newPings[country][hostname].SensorPath);
            }
    }

    private void CancelAndResetToken()
    {
        _tokenSource.Cancel();
        _tokenSource.TryReset();
    }
}
