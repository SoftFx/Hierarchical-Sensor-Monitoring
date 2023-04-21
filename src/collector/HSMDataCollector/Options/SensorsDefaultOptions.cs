using HSMDataCollector.Options.DefaultOptions;

namespace HSMDataCollector.Options
{
    internal sealed class SensorsDefaultOptions
    {
        internal WindowsInfoMonitoringOptions WindowsInfo { get; } = new WindowsInfoMonitoringOptions();


        internal ProcessMonitoringOptions ProcessMonitoring { get; } = new ProcessMonitoringOptions();

        internal SystemMonitoringOptions SystemMonitoring { get; } = new SystemMonitoringOptions();

        internal DiskMonitoringOptions DiskMonitoring { get; } = new DiskMonitoringOptions();


        internal CollectorStatusesOptions CollectorInfo { get; } = new CollectorStatusesOptions();

        internal CollectorAliveOptions CollectorAlive { get; } = new CollectorAliveOptions();

        internal ProductVersionOptions ProductInfo { get; } = new ProductVersionOptions();
    }
}