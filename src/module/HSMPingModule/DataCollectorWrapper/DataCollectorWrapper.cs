using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMPingModule.Config;
using HSMPingModule.Models;
using HSMPingModule.Settings;
using HSMSensorDataObjects;
using NLog;
using System.Collections.Concurrent;

namespace HSMPingModule.DataCollectorWrapper;

internal sealed class DataCollectorWrapper : IDataCollectorWrapper, IDisposable
{
    private readonly ConcurrentDictionary<string, IInstantValueSensor<double>> _sensors = new();

    private readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private readonly IInstantValueSensor<string> _exceptionSensor;

    private readonly CollectorSettings _config;
    private readonly IDataCollector _collector;


    public DataCollectorWrapper(ServiceConfig config)
    {
        _config = config.HSMDataCollectorSettings;

        var versionOptions = new VersionSensorOptions(ServiceConfig.Version);

        var options = new CollectorOptions()
        {
            ServerAddress = _config.ServerAddress,
            AccessKey = _config.Key,
            Port = _config.Port
        };

        _logger.Info("Product version: {version}", versionOptions.Version);
        _logger.Info("Access key: {key}", options.AccessKey);
        _logger.Info("Server address: {key}", options.ServerAddress);
        _logger.Info("Server port: {port}", options.Port);

        _collector = new DataCollector(options).AddNLog();

        if (OperatingSystem.IsWindows())
        {
            _collector.Windows.AddProductVersion(versionOptions)
                              .AddCollectorMonitoringSensors();
        }
        else
        {
            _collector.Unix.AddProductVersion(versionOptions)
                           .AddCollectorMonitoringSensors();
        }

        _exceptionSensor = _collector.CreateStringSensor("Infrastructure/AppException");
    }


    public Task Stop() => _collector.Stop();

    public Task Start() => _collector.Start().ContinueWith(_ => _logger.Info("Collector started"));


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
            sensor = _collector.CreateDoubleSensor(path, webSite.GetOptions(country, hostname, 60));

            _sensors.TryAdd(path, sensor);
        }

        sensor.AddValue(reply.Value, reply.Status, reply.Comment);
        _logger.Info("Added new value to the sensor {path}, Value: {value}", path, reply.Value);
    }

    public void AddApplicationException(string exceptionMessage) => _exceptionSensor.AddValue(exceptionMessage, SensorStatus.Ok);

    public void Dispose()
    {
        _collector?.Dispose();
    }
}