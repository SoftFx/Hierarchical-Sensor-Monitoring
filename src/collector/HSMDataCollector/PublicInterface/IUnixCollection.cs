using HSMDataCollector.Options;

namespace HSMDataCollector.PublicInterface
{
    public interface IUnixCollection
    {
        IUnixCollection AddProcessCpu(BarSensorOptions options = null);

        IUnixCollection AddProcessMemory(BarSensorOptions options = null);

        IUnixCollection AddProcessThreadCount(BarSensorOptions options = null);

        IUnixCollection AddProcessMonitoringSensors(BarSensorOptions options = null);


        IUnixCollection AddFreeDiskSpace(DiskSensorOptions options = null);

        IUnixCollection AddFreeDiskSpacePrediction(DiskSensorOptions options = null);

        IUnixCollection AddDiskMonitoringSensors(DiskSensorOptions options = null);


        IUnixCollection AddCollectorHeartbeat(SensorOptions options = null);
        
        IUnixCollection AddProductInfo(VersionSensorOptions options = null);
    }
}
