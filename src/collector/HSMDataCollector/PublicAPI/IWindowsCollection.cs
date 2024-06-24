using System;
using HSMDataCollector.Options;


namespace HSMDataCollector.PublicInterface
{
    public interface IWindowsCollection : IDisposable
    {
        IWindowsCollection AddAllComputerSensors();

        IWindowsCollection AddAllModuleSensors(Version productVersion = null);

        IWindowsCollection AddAllDefaultSensors(Version productVersion = null);


        IWindowsCollection AddProcessCpu(BarSensorOptions options = null);

        IWindowsCollection AddProcessMemory(BarSensorOptions options = null);

        IWindowsCollection AddProcessTimeInGC(BarSensorOptions options = null);

        IWindowsCollection AddProcessThreadCount(BarSensorOptions options = null);

        IWindowsCollection AddProcessMonitoringSensors(BarSensorOptions options = null);


        IWindowsCollection AddTotalCpu(BarSensorOptions options = null);

        IWindowsCollection AddFreeRamMemory(BarSensorOptions options = null);

        IWindowsCollection AddGlobalTimeInGC(BarSensorOptions options = null);

        IWindowsCollection AddSystemMonitoringSensors(BarSensorOptions options = null);


        IWindowsCollection AddFreeDiskSpace(DiskSensorOptions options = null);

        IWindowsCollection AddFreeDiskSpacePrediction(DiskSensorOptions options = null);

        IWindowsCollection AddFreeDisksSpace(DiskSensorOptions options = null);

        IWindowsCollection AddFreeDisksSpacePrediction(DiskSensorOptions options = null);

        IWindowsCollection AddActiveDiskTime(DiskBarSensorOptions options = null);

        IWindowsCollection AddActiveDisksTime(DiskBarSensorOptions options = null);

        IWindowsCollection AddDiskQueueLength(DiskBarSensorOptions options = null);

        IWindowsCollection AddDisksQueueLength(DiskBarSensorOptions options = null);

        IWindowsCollection AddDiskAverageWriteSpeed(DiskBarSensorOptions options = null);

        IWindowsCollection AddDisksAverageWriteSpeed(DiskBarSensorOptions options = null);

        IWindowsCollection AddDiskMonitoringSensors(DiskSensorOptions options = null, DiskBarSensorOptions diskBarOptions = null);

        IWindowsCollection AddAllDisksMonitoringSensors(DiskSensorOptions options = null, DiskBarSensorOptions diskBarOptions = null);


        IWindowsCollection AddWindowsLastUpdate(WindowsInfoSensorOptions options = null);

        IWindowsCollection AddWindowsLastRestart(WindowsInfoSensorOptions options = null);

        IWindowsCollection AddWindowsVersion(WindowsInfoSensorOptions options = null);

        IWindowsCollection AddWindowsApplicationErrorLogs(InstantSensorOptions options = null);

        IWindowsCollection AddWindowsSystemErrorLogs(InstantSensorOptions options = null);

        IWindowsCollection AddErrorWindowsLogs(InstantSensorOptions options = null);

        IWindowsCollection AddWindowsApplicationWarningLogs(InstantSensorOptions options = null);

        IWindowsCollection AddWindowsSystemWarningLogs(InstantSensorOptions options = null);

        IWindowsCollection AddWarningWindowsLogs(InstantSensorOptions options = null);

        IWindowsCollection AddAllWindowsLogs(InstantSensorOptions options = null);

        IWindowsCollection AddWindowsInfoMonitoringSensors(WindowsInfoSensorOptions infoOptions = null, InstantSensorOptions logsOptions = null);


        IWindowsCollection AddCollectorAlive(CollectorMonitoringInfoOptions options = null);

        IWindowsCollection AddCollectorVersion();

        IWindowsCollection AddCollectorErrors();

        IWindowsCollection AddCollectorMonitoringSensors(CollectorMonitoringInfoOptions options = null);


        IWindowsCollection AddProductVersion(VersionSensorOptions options);


        IWindowsCollection AddQueuePackageContentSize(BarSensorOptions options = null);

        IWindowsCollection AddQueuePackageProcessTime(BarSensorOptions options = null);

        IWindowsCollection AddQueuePackageValuesCount(BarSensorOptions options = null);

        IWindowsCollection AddQueueOverflow(BarSensorOptions options = null);

        IWindowsCollection AddAllQueueDiagnosticSensors(BarSensorOptions barOptions = null);


        IWindowsCollection AddNetworkConnectionsEstablished(NetworkSensorOptions options = null);

        IWindowsCollection AddNetworkConnectionFailures(NetworkSensorOptions options = null);

        IWindowsCollection AddNetworkConnectionsReset(NetworkSensorOptions options = null);

        IWindowsCollection AddAllNetworkSensors(NetworkSensorOptions options = null);


        IWindowsCollection SubscribeToWindowsServiceStatus(string serviceName);

        IWindowsCollection SubscribeToWindowsServiceStatus(ServiceSensorOptions options);
    }
}