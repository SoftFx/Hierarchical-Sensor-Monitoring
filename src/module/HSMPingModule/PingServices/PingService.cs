using HSMPingModule.Config;
using HSMPingModule.DataCollectorWrapper;
using HSMPingModule.SensorStructure;
using HSMPingModule.VpnManager;
using HSMSensorDataObjects;
using NLog;
using System.Collections.Concurrent;

namespace HSMPingModule.PingServices;

internal class PingService : BackgroundService
{
    private readonly ConcurrentQueue<(ResourceSensor resource, Task<PingResponse> request)> _pingRequests = new();

    private readonly CancellationTokenSource _tokenSource = new();
    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IDataCollectorWrapper _collector;
    private readonly ServiceConfig _config;
    private readonly BaseVpnManager _vpn;
    private readonly ResourceTree _tree;


    private TimeSpan PingDelay => _config.PingSettings.RequestsPeriod;


    public PingService(ResourceTree tree, ServiceConfig config, IDataCollectorWrapper collector, BaseVpnManager vpn)
    {
        _collector = collector;
        _config = config;
        _tree = tree;
        _vpn = vpn;
    }


    public override async Task StartAsync(CancellationToken token)
    {
        await _collector.Start();

        var connect = await _vpn.Connect();

        if (connect != null && !connect.IsOk)
        {
            _collector.AppNode.SendVpnStatus(connect.IsOk, _vpn.VpnDescription, connect.Error);
            _logger.Error($"Connection check is failed! {connect.Error}");
            return;
        }

        var vpnStatus = await _vpn.LoadCountries();

        _collector.AppNode.SendVpnStatus(vpnStatus.IsOk, _vpn.VpnDescription, vpnStatus.Error);

        await base.StartAsync(token);
    }

    public override Task StopAsync(CancellationToken token)
    {
        _tokenSource.Cancel();

        return _collector.Stop().ContinueWith(_ => base.StopAsync(token)).Unwrap();
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        do
        {
            var start = Ceil(DateTime.UtcNow, PingDelay);

            await Task.Delay(start - DateTime.UtcNow, _tokenSource.Token);

            foreach ((var country, var sensors) in _tree.CountrySet)
            {
                try
                {
                    var trySwitch = await _vpn.SwitchCountry(country);

                    if (!trySwitch.IsOk)
                    {
                        _logger.Error($"Cannot switch to {country}. {trySwitch.Error}");
                        continue;
                    }

                    _pingRequests.Clear();

                    foreach (var sensor in sensors)
                        _pingRequests.Enqueue((sensor, sensor.PingAdapter.SendPingRequest()));

                    await Task.WhenAll(_pingRequests.Select(u => u.request));
                    await _vpn.Disconnect();

                    foreach ((var resource, var request) in _pingRequests)
                        _collector.SendPingResult(resource, request.Result);
                }
                catch (Exception ex)
                {
                    var message = $"{country} processing... {ex.Message}";

                    _logger.Info(message);
                    _collector.AppNode.Exceptions.AddValue(message, SensorStatus.Error);
                }
            }
        }
        while (!_tokenSource.IsCancellationRequested);
    }


    private static DateTime Ceil(DateTime time, TimeSpan span)
    {
        var roundTicks = span.Ticks;

        return roundTicks == 0 ? time : new DateTime(time.Ticks / roundTicks * roundTicks + roundTicks);
    }
}