using System.Collections.Concurrent;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMPingModule.Config;
using HSMPingModule.Resourses;
using HSMPingModule.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace HSMPingModule.Collector;

internal sealed class DataCollectorService : BackgroundService, IDisposable, IDataCollectorService
{
    private readonly ConcurrentDictionary<string, IInstantValueSensor<int>> _sensors = new ();
    private readonly IInstantValueSensor<string> _exceptionSensor;
    private readonly IDataCollector _collector;
    private readonly ServiceConfig _config;


    public DataCollectorService(IOptionsMonitor<ServiceConfig> config)
    {
        _config = config.CurrentValue;

        var productInfoOptions = new VersionSensorOptions()
        {
            Version = ServiceConfig.Version,
        };

        var collectorOptions = new CollectorOptions()
        {
            AccessKey = _config.CollectorSettings.Key,
            ServerAddress = _config.CollectorSettings.ServerAddress,
            Port = _config.CollectorSettings.Port
        };

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

        _exceptionSensor = _collector.CreateStringSensor("Exception");
    }


    public override Task StopAsync(CancellationToken _) => _collector.Stop();
    
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _collector.Start();
        return base.StartAsync(cancellationToken);
    }


    protected override Task ExecuteAsync(CancellationToken token) => Task.CompletedTask;


    public async Task PingResultSend(WebSite webSite, string path, Task<PingResponse> taskReply)
    {
        var reply = await taskReply;

        if (reply.IsException)
        {
            _exceptionSensor.AddValue(reply.Comment, reply.Status, $"Path: {path}");
            return;
        }

        var sensor = _collector.CreateIntSensor(path, webSite.GetOptions);

        sensor.AddValue(reply.Value, reply.Status, reply.Comment);

        if (!_sensors.TryGetValue(path, out _))
            _sensors.TryAdd(path, sensor);
    }
    
    public override void Dispose()
    {
        _collector?.Dispose();
        base.Dispose();
    }
}