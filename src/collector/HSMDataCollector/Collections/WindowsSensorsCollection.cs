using System;
using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors.Windows;
using HSMDataCollector.DefaultSensors.Windows.Network;
using HSMDataCollector.DefaultSensors.Windows.Service;
using HSMDataCollector.DefaultSensors.Windows.WindowsInfo;
using HSMDataCollector.Options;
using HSMDataCollector.Prototypes;
using HSMDataCollector.PublicInterface;


namespace HSMDataCollector.DefaultSensors
{
    internal sealed class WindowsSensorsCollection : DefaultSensorsCollection, IWindowsCollection
    {
        protected override bool IsCorrectOs => DataCollector.IsWindowsOS;


        public WindowsSensorsCollection(SensorsStorage storage, PrototypesCollection prototype) : base(storage, prototype) { }


        public IWindowsCollection AddAllComputerSensors() =>
            (this as IWindowsCollection).AddSystemMonitoringSensors().AddAllDisksMonitoringSensors().AddWindowsInfoMonitoringSensors().AddAllNetworkSensors();

        public IWindowsCollection AddAllModuleSensors(Version productVersion)
        {
            var moduleCollection = (this as IWindowsCollection).AddProcessMonitoringSensors()
                                                               .AddCollectorMonitoringSensors()
                                                               .AddAllQueueDiagnosticSensors();

            if (productVersion != null)
            {
                var versionOptions = new VersionSensorOptions(productVersion) { Version = productVersion };

                moduleCollection.AddProductVersion(versionOptions);
            }

            return moduleCollection;
        }

        public IWindowsCollection AddAllDefaultSensors(Version productVersion) => AddAllComputerSensors().AddAllModuleSensors(productVersion);


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

        public IWindowsCollection AddProcessThreadPoolThreadCount(BarSensorOptions options)
        {
            return ToWindows(new ProcessThreadPoolThreadCount(_prototype.ProcessThreadPoolThreadCount.Get(options)));
        }

        public IWindowsCollection AddProcessTimeInGC(BarSensorOptions options)
        {
            return ToWindows(new WindowsProcessTimeInGC(_prototype.ProcessTimeInGC.Get(options)));
        }

        public IWindowsCollection AddProcessMonitoringSensors(BarSensorOptions options) =>
            AddProcessCpu(options).AddProcessMemory(options).AddProcessThreadCount(options).AddProcessTimeInGC(options).AddProcessThreadPoolThreadCount(options);

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

        public IWindowsCollection AddGlobalTimeInGC(BarSensorOptions options)
        {
            return ToWindows(new WindowsTimeInGC(_prototype.TimeInGC.Get(options)));
        }

        public IWindowsCollection AddSystemMonitoringSensors(BarSensorOptions options) =>
            AddFreeRamMemory(options).AddTotalCpu(options).AddGlobalTimeInGC(options);

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

        public IWindowsCollection AddDiskAverageWriteSpeed(DiskBarSensorOptions options)
        {
            return ToWindows(new WindowsDiskWriteSpeed(_prototype.WindowsAverageDiskWriteSpeed.Get(options)));
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

        public IWindowsCollection AddDisksAverageWriteSpeed(DiskBarSensorOptions options = null)
        {
            foreach (var diskOptions in _prototype.WindowsAverageDiskWriteSpeed.GetAllDisksOptions(options))
                ToWindows(new WindowsDiskWriteSpeed(diskOptions));

            return this;
        }

        public IWindowsCollection AddDiskMonitoringSensors(DiskSensorOptions options = null, DiskBarSensorOptions diskBarOptions = null) =>
            AddFreeDiskSpace(options).AddFreeDiskSpacePrediction(options).AddActiveDiskTime(diskBarOptions).AddDiskQueueLength(diskBarOptions)
            .AddDiskAverageWriteSpeed(diskBarOptions);

        public IWindowsCollection AddAllDisksMonitoringSensors(DiskSensorOptions options = null, DiskBarSensorOptions diskBarOptions = null) =>
            AddFreeDisksSpace(options).AddFreeDisksSpacePrediction(options).AddActiveDisksTime(diskBarOptions).AddDisksQueueLength(diskBarOptions)
            .AddDisksAverageWriteSpeed(diskBarOptions);

        #endregion


        #region Windows

        public IWindowsCollection AddWindowsLastUpdate(WindowsInfoSensorOptions options)
        {
            // return ToWindows(new WindowsLastUpdate(_prototype.WindowsLastUpdate.Get(options)));
            return this;
        }

        public IWindowsCollection AddWindowsLastRestart(WindowsInfoSensorOptions options)
        {
            return ToWindows(new WindowsLastRestart(_prototype.WindowsLastRestart.Get(options)));
        }

        public IWindowsCollection AddWindowsInstallDate(WindowsInfoSensorOptions options)
        {
            return ToWindows(new WindowsInstallDate(_prototype.WindowsInstallDate.Get(options)));
        }

        public IWindowsCollection AddWindowsVersion(WindowsInfoSensorOptions options)
        {
            return ToWindows(new WindowsVersion(_prototype.WindowsVersion.Get(options)));
        }

        public IWindowsCollection AddWindowsInfoMonitoringSensors(WindowsInfoSensorOptions infoOptions, InstantSensorOptions logsOptions) =>
            AddWindowsInstallDate(infoOptions).AddWindowsLastRestart(infoOptions).AddWindowsVersion(infoOptions).AddAllWindowsLogs(logsOptions);

        public IWindowsCollection AddWindowsApplicationErrorLogs(InstantSensorOptions options = null)
        {
            return ToWindows(new WindowsApplicationErrorLogs(_prototype.WindowsApplicationErrorLogsPrototype.Get(options)));
        }

        public IWindowsCollection AddWindowsSystemErrorLogs(InstantSensorOptions options = null)
        {
            return ToWindows(new WindowsSystemErrorLogs(_prototype.WindowsSystemErrorLogsPrototype.Get(options)));
        }

        public IWindowsCollection AddErrorWindowsLogs(InstantSensorOptions options = null) =>
            AddWindowsApplicationErrorLogs(options).AddWindowsSystemErrorLogs(options);

        public IWindowsCollection AddWindowsApplicationWarningLogs(InstantSensorOptions options = null)
        {
            return ToWindows(new WindowsApplicationWarningLogs(_prototype.WindowsApplicationWarningLogsPrototype.Get(options)));
        }

        public IWindowsCollection AddWindowsSystemWarningLogs(InstantSensorOptions options = null)
        {
            return ToWindows(new WindowsSystemWarningLogs(_prototype.WindowsSystemWarningLogsPrototype.Get(options)));
        }

        public IWindowsCollection AddWarningWindowsLogs(InstantSensorOptions options = null) =>
            AddWindowsApplicationWarningLogs(options).AddWindowsSystemWarningLogs(options);

        public IWindowsCollection AddAllWindowsLogs(InstantSensorOptions options) =>
            AddErrorWindowsLogs(options).AddWarningWindowsLogs(options);

        #endregion


        #region Collector

        public IWindowsCollection AddCollectorAlive(CollectorMonitoringInfoOptions options) => (IWindowsCollection)AddCollectorAliveCommon(options);

        public IWindowsCollection AddCollectorVersion() => (IWindowsCollection)AddCollectorVersionCommon();

        public IWindowsCollection AddCollectorErrors() => (IWindowsCollection)AddCollectorErrorsCommon();

        public IWindowsCollection AddCollectorMonitoringSensors(CollectorMonitoringInfoOptions options) => (IWindowsCollection)AddFullCollectorMonitoringCommon(options);

        #endregion


        #region Diagnostic

        public IWindowsCollection AddQueuePackageProcessTime(BarSensorOptions options = null) => (IWindowsCollection)AddPackageProcessTimeCommon(options);

        public IWindowsCollection AddQueuePackageValuesCount(BarSensorOptions options = null) => (IWindowsCollection)AddPackageValuesCountCommon(options);

        public IWindowsCollection AddQueuePackageContentSize(BarSensorOptions options = null) => (IWindowsCollection)AddPackageContentSizeCommon(options);

        public IWindowsCollection AddQueueOverflow(BarSensorOptions options = null) => (IWindowsCollection)AddQueueOverflowCommon(options);

        public IWindowsCollection AddAllQueueDiagnosticSensors(BarSensorOptions options = null) =>
            AddQueueOverflow(options).AddQueuePackageValuesCount(options).AddQueuePackageContentSize(options).AddQueuePackageProcessTime(options);

        #endregion


        #region Network

        public IWindowsCollection AddNetworkConnectionsEstablished(NetworkSensorOptions options = null) => ToWindows(new ConnectionsEstablishedCountSensor(_prototype.ConnectionsEstablishedCount.Get(options)));

        public IWindowsCollection AddNetworkConnectionFailures(NetworkSensorOptions options = null) => ToWindows(new ConnectionFailuresCountSensor(_prototype.ConnectionsFailuresCount.Get(options)));

        public IWindowsCollection AddNetworkConnectionsReset(NetworkSensorOptions options = null) => ToWindows(new ConnectionsResetCountSensor(_prototype.ConnectionsResetCount.Get(options)));

        public IWindowsCollection AddAllNetworkSensors(NetworkSensorOptions options = null) => AddNetworkConnectionFailures(options).AddNetworkConnectionsEstablished(options).AddNetworkConnectionsReset(options);

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


        public bool UnsubscribeWindowsServiceStatus(ServiceSensorOptions options)
        {
            var opt = _prototype.ServiceStatus.Get(options);

            return Unregister(opt.Path);
        }

        private WindowsSensorsCollection ToWindows(SensorBase sensor) => (WindowsSensorsCollection)Register(sensor);
    }
}