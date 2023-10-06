using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMPingModule.Config;
using HSMPingModule.PingServices;
using HSMPingModule.SensorStructure;
using HSMPingModule.Settings;
using HSMSensorDataObjects;
using NLog;
using System.Collections.Concurrent;

namespace HSMPingModule.DataCollectorWrapper;


internal sealed class DataCollectorWrapper : IDataCollectorWrapper
{
    private readonly ConcurrentDictionary<string, IInstantValueSensor<double>> _sensors = new();

    private readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly CollectorSettings _config;
    private readonly IDataCollector _collector;


    public ApplicationNode AppNode { get; }

    public TimeSpan PostPeriod { get; } = TimeSpan.FromSeconds(10);


    public DataCollectorWrapper(ServiceConfig config)
    {
        _config = config.HSMDataCollectorSettings;

        var versionOptions = new VersionSensorOptions(ServiceConfig.Version);

        var options = new CollectorOptions()
        {
            ServerAddress = _config.ServerAddress,
            AccessKey = _config.Key,
            Port = _config.Port,

            PackageCollectPeriod = PostPeriod,
        };

        _logger.Info("Product version: {version}", versionOptions.Version);
        _logger.Info("Access key: {key}", options.AccessKey);
        _logger.Info("Server address: {uri}", options.ServerAddress);
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

        AppNode = new ApplicationNode(_collector);
    }


    public Task Stop() => _collector.Stop();

    public Task Start() => _collector.Start().ContinueWith(_ => _logger.Info("Collector started"));


    public void SendPingResult(ResourceSensor resource, List<PingResponse> results)
    {
        var sensorPath = resource.SensorPath;

        if (!_sensors.TryGetValue(sensorPath, out var sensor))
        {
            sensor = _collector.CreateDoubleSensor(sensorPath, resource.SensorOptions);

            _sensors.TryAdd(sensorPath, sensor);

            _logger.Info("New sensor has been added: {path}", sensorPath);
        }

        var comments = string.Join('-', results.Select(u => u.Comment));

        if (results.Any(u => u.Status == SensorStatus.Ok))
        {
            var avr = results.Where(u => !double.IsNaN(u.Value)).Average(u => u.Value);

            sensor.AddValue(avr, SensorStatus.Ok, comments);
        }
        else
            sensor.AddValue(0, SensorStatus.Error, $"All {results.Count} requests have been failded: {comments}");

        _logger.Info("New sensor value has been sent: {path} -> {value}", sensorPath, comments);
    }
}