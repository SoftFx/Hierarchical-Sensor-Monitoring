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
    private const int PingAttemptCount = 10;

    private readonly ConcurrentQueue<(ResourceSensor resource, Task<PingResponse> request)> _pingRequests = new();

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

        await Task.Delay(_collector.PostPeriod * 2, token);

        var isConnected = false;

        _logger.Info("Try find available country");

        for (int i = 0; i < 10; ++i)
        {
            var connect = await _vpn.Connect();
            var message = connect.IsOk ? connect.Result : connect.Error;
            isConnected = connect.IsOk;

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
        return _collector.Stop().ContinueWith(_ => base.StopAsync(token)).Unwrap();
    }


    protected override async Task ExecuteAsync(CancellationToken token)
    {
        Task Delay(TimeSpan time) => Task.Delay(time, token);

        do
        {
            _logger.Info("Await next activation...");

            var start = Ceil(DateTime.UtcNow, PingDelay);

            await Delay(start - DateTime.UtcNow);

            _logger.Info("Ping activation...");

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

                    await Delay(TimeSpan.FromSeconds(2));

                    var masterResult = await MasterPingRound();

                    if (!masterResult)
                    {
                        await _vpn.Disconnect();
                        await Delay(TimeSpan.FromSeconds(2));

                        var message = $"Master ping for {country} is failed. Current ping round skipped";

                        _collector.AppNode.MasterPingFail.AddValue(message);
                        _logger.Error(message);
                        continue;
                    }

                    await Delay(TimeSpan.FromSeconds(2));

                    var results = await RunPingRound(sensors);

                    await _vpn.Disconnect();
                    await Delay(TimeSpan.FromSeconds(2));

                    _logger.Info("Stop ping round. Start sending results...");

                    foreach ((var resource, var responses) in results)
                        _collector.SendPingResult(resource, responses);

                    await Delay(_collector.PostPeriod);
                }
                catch (Exception ex)
                {
                    var message = $"{country} processing... {ex.Message}";

                    _logger.Info(message);
                    _collector.AppNode.Exceptions.AddValue(message, SensorStatus.Error);
                }
            }
        }
        while (!token.IsCancellationRequested);
    }


    private async Task<bool> MasterPingRound()
    {
        _logger.Info($"Run master pinging round");

        bool isOk = false;

        foreach (var (master, ping) in _tree.MasterSites)
        {
            var result = await ping.SendPingRequest();

            isOk |= result.Status == SensorStatus.Ok;
        }

        _logger.Info($"Stop master pinging round");

        return isOk;
    }

    private async Task<List<(ResourceSensor, List<PingResponse>)>> RunPingRound(List<ResourceSensor> sensors)
    {
        var results = new ConcurrentDictionary<string, List<PingResponse>>();

        foreach (var sensor in sensors)
            results.TryAdd(sensor.SensorPath, new List<PingResponse>());

        var cnt = 0;

        _logger.Info($"Run pinging round");

        while (cnt++ < PingAttemptCount)
        {
            _pingRequests.Clear();

            _logger.Info($"Round {cnt}");

            foreach (var sensor in sensors)
                _pingRequests.Enqueue((sensor, sensor.PingAdapter.SendPingRequest()));

            await Task.WhenAll(_pingRequests.Select(u => u.request));

            foreach ((var resource, var request) in _pingRequests)
                results[resource.SensorPath].Add(request.Result);

            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        _logger.Info($"Stop pinging round");

        var ans = new List<(ResourceSensor, List<PingResponse>)>();

        foreach (var (name, pings) in results)
            ans.Add((sensors.FirstOrDefault(u => u.SensorPath == name), pings));

        return ans;
    }


    private static DateTime Ceil(DateTime time, TimeSpan span)
    {
        var roundTicks = span.Ticks;

        return roundTicks == 0 ? time : new DateTime(time.Ticks / roundTicks * roundTicks + roundTicks);
    }
}