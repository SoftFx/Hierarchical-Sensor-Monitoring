using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors.Windows;
using HSMDataCollector.DefaultSensors.Windows.Service;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using System;

namespace HSMDataCollector.DefaultSensors
{
    internal sealed class WindowsSensorsCollection : DefaultSensorsCollection, IWindowsCollection
    {
        protected override bool IsCorrectOs => DataCollector.IsWindowsOS;


        public WindowsSensorsCollection(SensorsStorage storage, PrototypesCollection prototype) : base(storage, prototype) { }


        #region Process

        public IWindowsCollection AddProcessCpu(BarSensorOptions options)
        {
            return ToWindows(new WindowsProcessCpu(_prototype.ProcessCpu.Get(options)));
        }

        public IWindowsCollection AddProcessMemory(BarSensorOptions options)
        {
            return ToWindows(new WindowsProcessMemory(_prototype.ProcessMemory.Get(options)));
        }

        public IWindowsCollection AddProcessThreadCount(BarSensorOptions options)
        {
            return ToWindows(new WindowsProcessThreadCount(_prototype.ProcessThreadCount.Get(options)));
        }

        public IWindowsCollection AddProcessMonitoringSensors(BarSensorOptions options) =>
            AddProcessCpu(options).AddProcessMemory(options).AddProcessThreadCount(options);

        #endregion


        #region System

        public IWindowsCollection AddTotalCpu(BarSensorOptions options)
        {
            return ToWindows(new WindowsTotalCpu(_prototype.TotalCPU.Get(options)));
        }

        public IWindowsCollection AddFreeRamMemory(BarSensorOptions options)
        {
            return ToWindows(new WindowsFreeRamMemory(_prototype.FreeRam.Get(options)));
        }

        public IWindowsCollection AddSystemMonitoringSensors(BarSensorOptions options) =>
            AddFreeRamMemory(options).AddTotalCpu(options);

        #endregion


        #region Disk

        public IWindowsCollection AddFreeDiskSpace(DiskSensorOptions options)
        {
            return ToWindows(new WindowsFreeDiskSpace(_prototype.WindowsFreeSpaceOnDisk.Get(options)));
        }

        public IWindowsCollection AddFreeDiskSpacePrediction(DiskSensorOptions options)
        {
            return ToWindows(new WindowsFreeDiskSpacePrediction(_prototype.WindowsFreeSpaceOnDiskPrediction.Get(options)));
        }

        public IWindowsCollection AddActiveDiskTime(DiskBarSensorOptions options)
        {
            return ToWindows(new WindowsActiveTimeDisk(_prototype.WindowsActiveTimeDisk.Get(options)));
        }
        
        public IWindowsCollection AddDiskQueueLength(DiskBarSensorOptions options)
        {
            return ToWindows(new WindowsDiskQueueLength(_prototype.WindowsDiskQueueLength.Get(options)));
        }

        public IWindowsCollection AddFreeDisksSpace(DiskSensorOptions options)
        {
            foreach (var diskOptions in _prototype.WindowsFreeSpaceOnDisk.GetAllDisksOptions(options))
                ToWindows(new WindowsFreeDiskSpace(diskOptions));

            return this;
        }

        public IWindowsCollection AddFreeDisksSpacePrediction(DiskSensorOptions options)
        {
            foreach (var diskOptions in _prototype.WindowsFreeSpaceOnDiskPrediction.GetAllDisksOptions(options))
                ToWindows(new WindowsFreeDiskSpacePrediction(diskOptions));

            return this;
        }

        public IWindowsCollection AddActiveDisksTime(DiskBarSensorOptions options = null)
        {
            foreach (var diskOptions in _prototype.WindowsActiveTimeDisk.GetAllDisksOptions(options))
                ToWindows(new WindowsActiveTimeDisk(diskOptions));

            return this;
        }
        
        public IWindowsCollection AddDisksQueueLength(DiskBarSensorOptions options = null)
        {
            foreach (var diskOptions in _prototype.WindowsDiskQueueLength.GetAllDisksOptions(options))
                ToWindows(new WindowsDiskQueueLength(diskOptions));

            return this;
        }

        public IWindowsCollection AddDiskMonitoringSensors(DiskSensorOptions options = null, DiskBarSensorOptions diskBarOptions = null) =>
            AddFreeDiskSpace(options).AddFreeDiskSpacePrediction(options).AddActiveDiskTime(diskBarOptions).AddDiskQueueLength(diskBarOptions);

        public IWindowsCollection AddAllDisksMonitoringSensors(DiskSensorOptions options = null, DiskBarSensorOptions diskBarOptions = null) =>
            AddFreeDisksSpace(options).AddFreeDisksSpacePrediction(options).AddActiveDisksTime(diskBarOptions).AddDisksQueueLength(diskBarOptions);

        #endregion


        #region Windows

        public IWindowsCollection AddWindowsLastUpdate(WindowsInfoSensorOptions options)
        {
            return ToWindows(new WindowsLastUpdate(_prototype.WindowsLastRestart.Get(options)));
        }

        public IWindowsCollection AddWindowsLastRestart(WindowsInfoSensorOptions options)
        {
            return ToWindows(new WindowsLastRestart(_prototype.WindowsLastUpdate.Get(options)));
        }

        public IWindowsCollection AddWindowsInfoMonitoringSensors(WindowsInfoSensorOptions infoOptions, InstantSensorOptions logsOptions) =>
            AddWindowsLastUpdate(infoOptions).AddWindowsLastRestart(infoOptions).AddAllWindowsLogs(logsOptions);

        public IWindowsCollection AddAllWindowsLogs(InstantSensorOptions options) => AddErrorWindowsLogs(_prototype.WindowsErrorLogsPrototype.Get(options)).AddWarningWindowsLogs(_prototype.WindowsWarningLogsPrototype.Get(options));

        public IWindowsCollection AddErrorWindowsLogs(InstantSensorOptions options = null) => ToWindows(new WindowsErrorLogs(_prototype.WindowsErrorLogsPrototype.Get(options)));

        public IWindowsCollection AddWarningWindowsLogs(InstantSensorOptions options = null) => ToWindows(new WindowsWarningLogs(_prototype.WindowsWarningLogsPrototype.Get(options)));

        #endregion


        #region Collector

        public IWindowsCollection AddCollectorAlive(CollectorMonitoringInfoOptions options) => (IWindowsCollection)AddCollectorAliveCommon(options);

        public IWindowsCollection AddCollectorVersion() => (IWindowsCollection)AddCollectorVersionCommon();

        public IWindowsCollection AddCollectorErrors() => (IWindowsCollection)AddCollectorErrorsCommon();

        public IWindowsCollection AddCollectorMonitoringSensors(CollectorMonitoringInfoOptions options) => (IWindowsCollection)AddFullCollectorMonitoringCommon(options);

        #endregion


        #region Diagnostic

        public IWindowsCollection AddQueuePackageProcessTime(BarSensorOptions options = null)
        {
            throw new System.NotImplementedException();
        }

        public IWindowsCollection AddQueuePackageContentSize(BarSensorOptions options = null)
        {
            throw new System.NotImplementedException();
        }

        public IWindowsCollection AddQueuePackageValues(BarSensorOptions options = null)
        {
            throw new System.NotImplementedException();
        }

        public IWindowsCollection AddQueueOverflow(BarSensorOptions options = null) => (IWindowsCollection)AddQueueOverflowCommon(options);

        public IWindowsCollection AddAllQueueDiagnosticSensors(BarSensorOptions options = null) => AddQueueOverflow(options);

        #endregion


        public IWindowsCollection AddProductVersion(VersionSensorOptions options) => (IWindowsCollection)AddProductVersionCommon(options);


        public IWindowsCollection SubscribeToWindowsServiceStatus(string serviceName) =>
            SubscribeToWindowsServiceStatus(new ServiceSensorOptions()
            {
                ServiceName = serviceName,
            });


        public IWindowsCollection SubscribeToWindowsServiceStatus(ServiceSensorOptions options)
        {
            return options != null ? ToWindows(new WindowsServiceStatusSensor(_prototype.ServiceStatus.Get(options)))
                                   : throw new ArgumentNullException(nameof(options));
        }


        private WindowsSensorsCollection ToWindows(SensorBase sensor) => (WindowsSensorsCollection)Register(sensor);
    }
}