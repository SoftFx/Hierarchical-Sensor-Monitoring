using System.Collections.Concurrent;
using HSMPingModule.Config;
using HSMPingModule.DataCollectorWrapper;
using HSMPingModule.VpnManager;
using Microsoft.Extensions.Options;

namespace HSMPingModule.Services;

internal class PingService : BackgroundService
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, PingAdapter>> _newPings = new();

    private readonly CancellationTokenSource _tokenSource = new ();
    private readonly IDataCollectorWrapper _collector;
    private readonly ILogger<PingService> _logger;
    private readonly BaseVpnManager _vpn;
    private readonly ServiceConfig _config;


    public PingService(IOptionsMonitor<ServiceConfig> config, IDataCollectorWrapper collector, ILogger<PingService> logger, BaseVpnManager vpn)
    {
        _logger = logger;
        _vpn = vpn;

        _collector = collector;
        _config = config.CurrentValue;
        _config.OnChanged += RebuildPings;

        InitPings();
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _collector.Start();

        _ = StartPinging();
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _collector.Stop();
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
                var answer = await _vpn.SwitchCountry(country);

                if (answer.IsOk)
                {
                    foreach (var (_, ping) in pings)
                        sendingPings.Add(Task.Run(() => ping.Ping()));

                    await Task.WhenAll(sendingPings).ContinueWith(_ => sendingPings.Clear());
                }
                else
                {
                    _logger.LogInformation("Couldn't change country to {0}", country);
                    _collector.AddApplicationException($"Error occured during country change to {country}. Error message: {answer.Error}");
                }
            }
    }

    private void InitPings()
    {
        foreach (var (_, pingAdapter) in _newPings.SelectMany(x => x.Value))
            pingAdapter.SendResult -= _collector.PingResultSend;

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

                ping.SendResult += _collector.PingResultSend;

                _logger.LogInformation("New pinging sensor added at {path}", _newPings[country][hostname].SensorPath);
            }
    }

    private void CancelAndResetToken()
    {
        _tokenSource.Cancel();
        _tokenSource.TryReset();
    }
}
