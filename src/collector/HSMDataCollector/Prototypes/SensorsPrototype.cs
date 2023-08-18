using HSMDataCollector.Prototypes;

namespace HSMDataCollector.Options
{
    internal sealed class SensorsPrototype
    {
        internal WindowsInfoMonitoringPrototype WindowsInfo { get; } /*= new WindowsInfoMonitoringPrototype();*/


        internal FreeSpaceOnDiskPrototype FreeSpaceOnDisk { get; } = new FreeSpaceOnDiskPrototype();

        internal FreeSpaceOnDiskPredictionPrototype FreeSpaceOnDiskPrediction { get; } = new FreeSpaceOnDiskPredictionPrototype();


        internal ProductVersionPrototype ProductVersion { get; } = new ProductVersionPrototype();


        internal CollectorVersionPrototype CollectorVersion { get; } = new CollectorVersionPrototype();

        internal ServiceAlivePrototype CollectorAlive { get; } = new ServiceAlivePrototype();
    }
}