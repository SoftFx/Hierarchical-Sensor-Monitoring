using HSMDataCollector.Options.DefaultOptions;
using HSMDataCollector.Prototypes;

namespace HSMDataCollector.Options
{
    internal sealed class SensorsPrototype
    {
        internal WindowsInfoMonitoringPrototype WindowsInfo { get; } /*= new WindowsInfoMonitoringPrototype();*/


        internal ProcessMonitoringPrototype ProcessMonitoring { get; } = new ProcessMonitoringPrototype();


        internal FreeSpaceOnDiskPrototype FreeSpaceOnDisk { get; } = new FreeSpaceOnDiskPrototype();

        internal FreeSpaceOnDiskPredictionPrototype FreeSpaceOnDiskPrediction { get; } = new FreeSpaceOnDiskPredictionPrototype();


        internal ProductVersionPrototype ProductVersion { get; } = new ProductVersionPrototype();


        internal CollectorVersionPrototype CollectorVersion { get; } = new CollectorVersionPrototype();

        internal CollectorStatusPrototype CollectorStatus { get; } = new CollectorStatusPrototype();

        internal CollectorAlivePrototype CollectorAlive { get; } = new CollectorAlivePrototype();
    }
}