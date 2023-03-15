using HSMDataCollector.Options;

namespace HSMDataCollector.PublicInterface
{
    public interface IWindowsCollection
    {
        IWindowsCollection AddProcessCpu(BarSensorOptions options = null);

        IWindowsCollection AddProcessMemory(BarSensorOptions options = null);

        IWindowsCollection AddProcessThreadCount(BarSensorOptions options = null);

        IWindowsCollection AddProcessMonitoringSensors(BarSensorOptions options = null);


        IWindowsCollection AddTotalCpu(BarSensorOptions options = null);

        IWindowsCollection AddFreeRamMemory(BarSensorOptions options = null);

        IWindowsCollection AddSystemMonitoringSensors(BarSensorOptions options = null);


        IWindowsCollection AddFreeDiskSpace(DiskSensorOptions options = null);

        IWindowsCollection AddFreeDiskSpacePrediction(DiskSensorOptions options = null);

        IWindowsCollection AddFreeDisksSpace(DiskSensorOptions options = null);

        IWindowsCollection AddFreeDisksSpacePrediction(DiskSensorOptions options = null);

        IWindowsCollection AddDiskMonitoringSensors(DiskSensorOptions options = null);


        IWindowsCollection AddWindowsNeedUpdate(WindowsSensorOptions options = null);

        IWindowsCollection AddWindowsLastUpdate(WindowsSensorOptions options = null);

        IWindowsCollection AddWindowsLastRestart(WindowsSensorOptions options = null);

        IWindowsCollection AddWindowsInfoMonitoringSensors(WindowsSensorOptions options = null);


        IWindowsCollection AddCollectorHeartbeat(SensorOptions options = null);
        
        IWindowsCollection AddProductInfo(ProductInfoOptions options = null);
    }
}
