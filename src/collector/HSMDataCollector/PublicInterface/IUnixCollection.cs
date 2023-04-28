using HSMDataCollector.Options;

namespace HSMDataCollector.PublicInterface
{
    public interface IUnixCollection
    {
        IUnixCollection AddProcessCpu(BarSensorOptions options = null);

        IUnixCollection AddProcessMemory(BarSensorOptions options = null);

        IUnixCollection AddProcessThreadCount(BarSensorOptions options = null);

        IUnixCollection AddProcessMonitoringSensors(BarSensorOptions options = null);


        IUnixCollection AddTotalCpu(BarSensorOptions options = null);

        IUnixCollection AddFreeRamMemory(BarSensorOptions options = null);

        IUnixCollection AddSystemMonitoringSensors(BarSensorOptions options = null);


        IUnixCollection AddFreeDiskSpace(DiskSensorOptions options = null);

        IUnixCollection AddFreeDiskSpacePrediction(DiskSensorOptions options = null);

        IUnixCollection AddDiskMonitoringSensors(DiskSensorOptions options = null);


        IUnixCollection AddCollectorHeartbeat(CollectorMonitoringInfoOptions options = null);

        IUnixCollection AddCollectorVersion(CollectorInfoOptions options = null);

        IUnixCollection AddCollectorStatus(CollectorInfoOptions options = null);

        IUnixCollection AddCollectorMonitoringSensors(CollectorMonitoringInfoOptions options = null);


        IUnixCollection AddProductVersion(VersionSensorOptions options = null);
    }
}
