using HSMDataCollector.Options;
using System;

namespace HSMDataCollector.PublicInterface
{
    public interface IWindowsCollection
    {
        IWindowsCollection AddAllComputer();

        IWindowsCollection AddAllModule(Version productVersion);

        IWindowsCollection AddAllCollection(Version productVersion);


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

        IWindowsCollection AddActiveDiskTime(DiskBarSensorOptions options = null);

        IWindowsCollection AddActiveDisksTime(DiskBarSensorOptions options = null);

        IWindowsCollection AddDiskQueueLength(DiskBarSensorOptions options = null);

        IWindowsCollection AddDisksQueueLength(DiskBarSensorOptions options = null);

        IWindowsCollection AddDiskMonitoringSensors(DiskSensorOptions options = null, DiskBarSensorOptions diskBarOptions = null);

        IWindowsCollection AddAllDisksMonitoringSensors(DiskSensorOptions options = null, DiskBarSensorOptions diskBarOptions = null);


        IWindowsCollection AddWindowsLastUpdate(WindowsInfoSensorOptions options = null);

        IWindowsCollection AddWindowsLastRestart(WindowsInfoSensorOptions options = null);

        IWindowsCollection AddAllWindowsLogs(InstantSensorOptions options = null);

        IWindowsCollection AddErrorWindowsLogs(InstantSensorOptions options = null);

        IWindowsCollection AddWarningWindowsLogs(InstantSensorOptions options = null);

        IWindowsCollection AddWindowsInfoMonitoringSensors(WindowsInfoSensorOptions infoOptions = null, InstantSensorOptions logsOptions = null);


        IWindowsCollection AddCollectorAlive(CollectorMonitoringInfoOptions options = null);

        IWindowsCollection AddCollectorVersion();

        IWindowsCollection AddCollectorMonitoringSensors(CollectorMonitoringInfoOptions options = null);


        IWindowsCollection AddProductVersion(VersionSensorOptions options);


        IWindowsCollection SubscribeToWindowsServiceStatus(string serviceName);

        IWindowsCollection SubscribeToWindowsServiceStatus(ServiceSensorOptions options);
    }
}
