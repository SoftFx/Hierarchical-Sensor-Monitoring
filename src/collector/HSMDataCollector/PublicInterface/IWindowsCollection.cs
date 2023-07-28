﻿using HSMDataCollector.Options;

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


        IWindowsCollection AddCollectorAlive(CollectorMonitoringInfoOptions options = null);

        IWindowsCollection AddCollectorVersion(CollectorInfoOptions options = null);

        IWindowsCollection AddCollectorStatus(CollectorInfoOptions options = null);

        IWindowsCollection AddCollectorMonitoringSensors(CollectorMonitoringInfoOptions options = null);


        IWindowsCollection AddProductVersion(VersionSensorOptions options = null);


        IWindowsCollection SubscribeToServiceStatus(ServiceSensorOptions options);
    }
}
