using System.Collections.Concurrent;
using System.Reflection;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMPingModule.Config;
using HSMServer.Extensions;
using Microsoft.Extensions.Options;

namespace HSMPingModule.Collector;

internal sealed class DataCollectorWrapper : IDisposable
{
    private readonly PingConfig _config;

    private readonly IDataCollector _collector;

    private readonly ConcurrentDictionary<string, IInstantValueSensor<bool>> _sensors = new ();


    public DataCollectorWrapper(IOptions<PingConfig> config)
    {
        _config = config.Value;
        
        var productInfoOptions = new VersionSensorOptions()
        {
            Version = Assembly.GetEntryAssembly()?.GetName().GetVersion(),
        };

        var collectorOptions = new CollectorOptions()
        {
            AccessKey = _config.CollectorSettings.Key,
            ServerAddress = _config.CollectorSettings.ServerAddress
        };

        var collectorInfoOptions = new CollectorMonitoringInfoOptions();


        _collector = new DataCollector(collectorOptions).AddNLog();

        if (OperatingSystem.IsWindows())
        {
            _collector.Windows.AddCollectorAlive(collectorInfoOptions)
                              .AddCollectorVersion()
                              .AddProductVersion(productInfoOptions);
        }
        else
        {
            _collector.Unix.AddCollectorAlive(collectorInfoOptions)
                           .AddCollectorVersion()
                           .AddProductVersion(productInfoOptions);
        }
    }


    internal Task PingResultSend(string hostname, string country, PingResponse reply)
    {
        IInstantValueSensor<bool> sensor;
        var path = $"{country}/{hostname}";

        if (_sensors.TryGetValue(path, out sensor))
        {
            sensor.AddValue(reply.Value, reply.Status, reply.Comment);
        }
        else
        {
            sensor = _collector.CreateBoolSensor(path);
            sensor.AddValue(reply.Value, reply.Status, reply.Comment);
            _sensors.TryAdd(path, sensor);
        }

        return Task.CompletedTask;
    }

    public void Dispose() => _collector?.Dispose();

    internal Task Start() => _collector.Start();

    internal Task Stop() => _collector.Stop();
}