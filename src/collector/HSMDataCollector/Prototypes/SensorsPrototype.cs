using HSMDataCollector.Options.DefaultOptions;

namespace HSMDataCollector.Options
{
    internal sealed class SensorsPrototype
    {
        internal WindowsInfoMonitoringPrototype WindowsInfo { get; } = new WindowsInfoMonitoringPrototype();


        internal ProcessMonitoringPrototype ProcessMonitoring { get; } = new ProcessMonitoringPrototype();

        internal SystemMonitoringPrototype SystemMonitoring { get; } = new SystemMonitoringPrototype();

        internal DiskMonitoringPrototype DiskMonitoring { get; } = new DiskMonitoringPrototype();


        internal ProductVersionPrototype ProductVersion { get; } = new ProductVersionPrototype();


        internal CollectorStatusPrototype CollectorInfo { get; } = new CollectorStatusPrototype();

        internal CollectorAlivePrototype CollectorAlive { get; } = new CollectorAlivePrototype();
    }
}