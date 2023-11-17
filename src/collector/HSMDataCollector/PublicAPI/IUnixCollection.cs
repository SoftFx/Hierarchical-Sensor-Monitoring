using HSMDataCollector.Options;
using System;

namespace HSMDataCollector.PublicInterface
{
    public interface IUnixCollection : IDisposable
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


        IUnixCollection AddCollectorAlive(CollectorMonitoringInfoOptions options = null);

        IUnixCollection AddCollectorVersion();

        IUnixCollection AddCollectorErrors();

        IUnixCollection AddCollectorMonitoringSensors(CollectorMonitoringInfoOptions options = null);


        IUnixCollection AddProductVersion(VersionSensorOptions options = null);


        IUnixCollection AddQueuePackageProcessTime(BarSensorOptions options = null);

        IUnixCollection AddQueuePackageContentSize(BarSensorOptions options = null);

        IUnixCollection AddQueuePackageValues(BarSensorOptions options = null);

        IUnixCollection AddQueueOverflow(BarSensorOptions options = null);

        IUnixCollection AddAllQueueDiagnosticSensors(BarSensorOptions options = null);
    }
}