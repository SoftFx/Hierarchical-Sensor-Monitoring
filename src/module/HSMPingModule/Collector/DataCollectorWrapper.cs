using System.Collections.Concurrent;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMPingModule.Config;
using HSMPingModule.Resourses;
using Microsoft.Extensions.Options;

namespace HSMPingModule.Collector;

internal sealed class DataCollectorWrapper : IDisposable
{
    private readonly ConcurrentDictionary<string, IInstantValueSensor<int>> _sensors = new (){};
    private readonly IInstantValueSensor<string> _exceptionSensor;
    private readonly IDataCollector _collector;
    private readonly ServiceConfig _config;


    public DataCollectorWrapper(IOptionsMonitor<ServiceConfig> config)
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


    internal async Task PingResultSend(WebSite webSite, string path, Task<PingResponse> taskReply)
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

    public void Dispose() => _collector?.Dispose();

    internal Task Start() => _collector.Start();

    internal Task Stop() => _collector.Stop();
}