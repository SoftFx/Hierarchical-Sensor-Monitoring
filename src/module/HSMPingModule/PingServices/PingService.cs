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
        _logger.Info($"{nameof(PingService)} is starting...");

        await _vpn.Disconnect(); // protection from external VPN
        await _collector.Start();

        _logger.Info($"Collector started");

        await Task.Delay(_collector.PostPeriod * 2, token);

        var isConnected = false;

        _logger.Info("Try find available country");

        for (int i = 0; i < 10; ++i)
        {
            var connect = await _vpn.Connect();
            var message = connect.IsOk ? connect.Result : connect.Error;

            _collector.AppNode.SendVpnStatus(connect.IsOk, _vpn.VpnDescription, $"Attempt #{i + 1}: {message}");

            if (connect.IsOk)
            {
                _logger.Info($"Successful connect! {connect.Result}");
                break;
            }

            _logger.Error($"Connection check is failed! {connect.Error}");
        }

        if (!isConnected)
            return;

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
            _logger.Info("Start ping round...");

            var start = Ceil(DateTime.UtcNow, PingDelay);

            await Task.Delay(start - DateTime.UtcNow, _tokenSource.Token);

            foreach (var (country, sensors) in _tree.CountrySet)
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

                    _logger.Info("Stop ping round. Start sending results...");

                    foreach ((var resource, var request) in _pingRequests)
                        _collector.SendPingResult(resource, request.Result);

                    await Task.Delay(_collector.PostPeriod, _tokenSource.Token);
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