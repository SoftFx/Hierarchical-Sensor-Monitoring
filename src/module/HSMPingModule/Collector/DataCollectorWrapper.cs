using System.Reflection;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMPingModule.Config;
using HSMServer.Extensions;

namespace HSMPingModule.Collector;

internal sealed class DataCollectorWrapper
{
    private static PingConfig _config;
    private readonly IDataCollector _collector;


    public DataCollectorWrapper()
    {
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
    
    public static void SetConfig(PingConfig config) => _config = config;
    
    public void Dispose() => _collector?.Dispose();

    internal Task Start() => _collector.Start();

    internal Task Stop() => _collector.Stop();
}