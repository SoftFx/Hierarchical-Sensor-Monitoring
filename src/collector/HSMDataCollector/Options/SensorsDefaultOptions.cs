namespace HSMDataCollector.Options
{
    internal sealed class SensorsDefaultOptions
    {
        internal SystemMonitoringOptions SystemMonitoring { get; } = new SystemMonitoringOptions();

        internal ProcessMonitoringOptions ProcessMonitoring { get; } = new ProcessMonitoringOptions();

        internal DiskMonitoringOptions DiskMonitoring { get; } = new DiskMonitoringOptions();

        internal WindowsInfoMonitoringOptions WindowsInfoMonitoring { get; } = new WindowsInfoMonitoringOptions();

        internal CollectorAliveOptions CollectorAliveMonitoring { get; } = new CollectorAliveOptions();

        internal ProductVersionOptions ProductVersionMonitoring { get; } = new ProductVersionOptions();
    }
}
