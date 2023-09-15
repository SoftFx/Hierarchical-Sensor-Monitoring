using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMPingModule.Config;
using HSMPingModule.Models;
using HSMPingModule.Services.Interfaces;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace HSMPingModule.Collector;

internal sealed class DataCollectorService : IDataCollectorService, IDisposable
{
    private readonly ConcurrentDictionary<string, IInstantValueSensor<double>> _sensors = new();

    private readonly IInstantValueSensor<string> _exceptionSensor;
    private readonly ILogger<DataCollectorService> _logger;

    private readonly IDataCollector _collector;
    private readonly ServiceConfig _config;


    public DataCollectorService(IOptionsMonitor<ServiceConfig> config, ILogger<DataCollectorService> logger)
    {
        _logger = logger;
        _config = config.CurrentValue;

        var productInfoOptions = new VersionSensorOptions(ServiceConfig.Version);

        _logger.LogInformation("Product version: {version}", productInfoOptions.Version);

        var collectorOptions = new CollectorOptions()
        {
            AccessKey = _config.CollectorSettings.Key,
            ServerAddress = _config.CollectorSettings.ServerAddress,
            Port = _config.CollectorSettings.Port
        };

        _logger.LogInformation("Access key: {key}", collectorOptions.AccessKey);
        _logger.LogInformation("Server address: {key}", collectorOptions.ServerAddress);
        _logger.LogInformation("Server port: {port}", collectorOptions.Port);

        _collector = new DataCollector(collectorOptions).AddNLog();

        if (OperatingSystem.IsWindows())
        {
            _collector.Windows.AddProductVersion(productInfoOptions)
                              .AddCollectorMonitoringSensors();
        }
        else
        {
            _collector.Unix.AddProductVersion(productInfoOptions)
                           .AddCollectorMonitoringSensors();
        }

        _exceptionSensor = _collector.CreateStringSensor("Infrastructure/AppException");
    }


    public Task StopAsync() => _collector.Stop();

    public Task StartAsync() => _collector.Start().ContinueWith(_ => _logger.LogInformation("Collector started"));


    public async Task PingResultSend(WebSite webSite, string country, string hostname, Task<PingResponse> taskReply)
    {
        var reply = await taskReply;
        var path = $"{hostname}/{country}";

        if (reply.IsException)
        {
            _exceptionSensor.AddValue(reply.Comment, reply.Status, $"Path: {path}");
            return;
        }

        if (!_sensors.TryGetValue(path, out var sensor))
        {
            sensor = _collector.CreateDoubleSensor(path, webSite.GetOptions(country, hostname, _config.ResourceSettings.DefaultSiteNodeSettings.PingDelay.Value));

            _sensors.TryAdd(path, sensor);
        }

        sensor.AddValue(reply.Value, reply.Status, reply.Comment);
    }

    public void Dispose()
    {
        _collector?.Dispose();
    }
}