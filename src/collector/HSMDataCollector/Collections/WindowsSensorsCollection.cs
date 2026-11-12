using System;
using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors.Windows;
using HSMDataCollector.DefaultSensors.Windows.Network;
using HSMDataCollector.DefaultSensors.Windows.Process;
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
            (this as IWindowsCollection).AddSystemMonitoringSensors().AddAllDisksMonitoringSensors().AddWindowsInfoMonitoringSensors().AddAllNetworkSensors().AddTopCpuProcessesSensors();

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

        public IWindowsCollection AddProcessMonitoringSensors(BarSensorOptions options)
        {
            SensorOptions.ForSeveralSensors(options, nameof(AddProcessMonitoringSensors));

            return AddProcessCpu(options).AddProcessMemory(options).AddProcessThreadCount(options).AddProcessTimeInGC(options).AddProcessThreadPoolThreadCount(options);
        }

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

        public IWindowsCollection AddSystemMonitoringSensors(BarSensorOptions options)
        {
            SensorOptions.ForSeveralSensors(options, nameof(AddSystemMonitoringSensors));

            return AddFreeRamMemory(options).AddTotalCpu(options).AddGlobalTimeInGC(options);
        }

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

        public IWindowsCollection AddDiskMonitoringSensors(DiskSensorOptions options = null, DiskBarSensorOptions diskBarOptions = null)
        {
            SensorOptions.ForSeveralSensors(options, nameof(AddDiskMonitoringSensors));
            SensorOptions.ForSeveralSensors(diskBarOptions, nameof(AddDiskMonitoringSensors));

            return AddFreeDiskSpace(options).AddFreeDiskSpacePrediction(options).AddActiveDiskTime(diskBarOptions).AddDiskQueueLength(diskBarOptions)
                .AddDiskAverageWriteSpeed(diskBarOptions);
        }

        public IWindowsCollection AddAllDisksMonitoringSensors(DiskSensorOptions options = null, DiskBarSensorOptions diskBarOptions = null)
        {
            // Both up front: the per-disk check runs lazily, after earlier sensors were added.
            SensorOptions.ForSeveralSensors(options, nameof(AddAllDisksMonitoringSensors));
            SensorOptions.ForSeveralSensors(diskBarOptions, nameof(AddAllDisksMonitoringSensors));

            return AddFreeDisksSpace(options).AddFreeDisksSpacePrediction(options).AddActiveDisksTime(diskBarOptions).AddDisksQueueLength(diskBarOptions)
                .AddDisksAverageWriteSpeed(diskBarOptions);
        }

        #endregion


        #region Windows

        public IWindowsCollection AddWindowsLastUpdate(WindowsInfoSensorOptions options)
        {
            return ToWindows(new WindowsLastUpdate(_prototype.WindowsLastUpdate.Get(options)));
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

        public IWindowsCollection AddWindowsInfoMonitoringSensors(WindowsInfoSensorOptions infoOptions, InstantSensorOptions logsOptions)
        {
            SensorOptions.ForSeveralSensors(infoOptions, nameof(AddWindowsInfoMonitoringSensors));
            SensorOptions.ForSeveralSensors(logsOptions, nameof(AddWindowsInfoMonitoringSensors));

            return AddWindowsInstallDate(infoOptions).AddWindowsLastUpdate(infoOptions).AddWindowsLastRestart(infoOptions).AddWindowsVersion(infoOptions).AddAllWindowsLogs(logsOptions);
        }

        public IWindowsCollection AddWindowsApplicationErrorLogs(InstantSensorOptions options = null)
        {
            return ToWindows(new WindowsApplicationErrorLogs(_prototype.WindowsApplicationErrorLogsPrototype.Get(options)));
        }

        public IWindowsCollection AddWindowsSystemErrorLogs(InstantSensorOptions options = null)
        {
            return ToWindows(new WindowsSystemErrorLogs(_prototype.WindowsSystemErrorLogsPrototype.Get(options)));
        }

        public IWindowsCollection AddErrorWindowsLogs(InstantSensorOptions options = null)
        {
            SensorOptions.ForSeveralSensors(options, nameof(AddErrorWindowsLogs));

            return AddWindowsApplicationErrorLogs(options).AddWindowsSystemErrorLogs(options);
        }

        public IWindowsCollection AddWindowsApplicationWarningLogs(InstantSensorOptions options = null)
        {
            return ToWindows(new WindowsApplicationWarningLogs(_prototype.WindowsApplicationWarningLogsPrototype.Get(options)));
        }

        public IWindowsCollection AddWindowsSystemWarningLogs(InstantSensorOptions options = null)
        {
            return ToWindows(new WindowsSystemWarningLogs(_prototype.WindowsSystemWarningLogsPrototype.Get(options)));
        }

        public IWindowsCollection AddWarningWindowsLogs(InstantSensorOptions options = null)
        {
            SensorOptions.ForSeveralSensors(options, nameof(AddWarningWindowsLogs));

            return AddWindowsApplicationWarningLogs(options).AddWindowsSystemWarningLogs(options);
        }

        public IWindowsCollection AddAllWindowsLogs(InstantSensorOptions options)
        {
            SensorOptions.ForSeveralSensors(options, nameof(AddAllWindowsLogs));

            return AddErrorWindowsLogs(options).AddWarningWindowsLogs(options);
        }

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

        public IWindowsCollection AddAllQueueDiagnosticSensors(BarSensorOptions options = null)
        {
            SensorOptions.ForSeveralSensors(options, nameof(AddAllQueueDiagnosticSensors));

            return AddQueueOverflow(options).AddQueuePackageValuesCount(options).AddQueuePackageContentSize(options).AddQueuePackageProcessTime(options);
        }

        #endregion


        #region Network

        public IWindowsCollection AddNetworkConnectionsEstablished(NetworkSensorOptions options = null) => ToWindows(new ConnectionsEstablishedCountSensor(_prototype.ConnectionsEstablishedCount.Get(options)));

        public IWindowsCollection AddNetworkConnectionFailures(NetworkSensorOptions options = null) => ToWindows(new ConnectionFailuresCountSensor(_prototype.ConnectionsFailuresCount.Get(options)));

        public IWindowsCollection AddNetworkConnectionsReset(NetworkSensorOptions options = null) => ToWindows(new ConnectionsResetCountSensor(_prototype.ConnectionsResetCount.Get(options)));

        public IWindowsCollection AddNetworkInterfacesSpeed() => ToWindows(new WindowsNetworkInterfaceSpeedMonitor(_storage));

        private readonly object _tcpFailureRateGate = new object();

        public IWindowsCollection AddNetworkConnectionFailuresRate(TimeSpan? period = null)
        {
            // Before the monitor is built: building it puts the rate sensor into the storage, and
            // a Windows-only sensor must not be left there by a call that then throws.
            EnsureCorrectOs();

            lock (_tcpFailureRateGate)
            {
                // Asked for once per collector: a second request is not a second monitor. The
                // storage is the record of what was taken: a monitor it refused (the collector
                // was stopping) is not there, so the next call tries again.
                //
                // The period of the first request stays: the rate sensor was created with it.
                // The native enable call differs here (the last period given before Start is
                // the one used), so a second request with another period is reported, not
                // swallowed: the host gets a rate it did not ask for last.
                var asked = period ?? TimeSpan.FromMinutes(1);

                if (_storage.TryGetValue(WindowsTcpFailureRateMonitor.MonitorPath, out var existing))
                {
                    if (existing is WindowsTcpFailureRateMonitor monitor && monitor.Period != asked)
                        _storage.ReportError($"The TCP connection failure rate sensor was already added with a period of {monitor.Period}; the period {asked} of the second request is not applied.");

                    return this;
                }

                return ToWindows(new WindowsTcpFailureRateMonitor(_storage, asked));
            }
        }

        public IWindowsCollection AddAllNetworkSensors(NetworkSensorOptions options = null)
        {
            SensorOptions.ForSeveralSensors(options, nameof(AddAllNetworkSensors));

            return AddNetworkConnectionFailures(options).AddNetworkConnectionsEstablished(options).AddNetworkConnectionsReset(options).AddNetworkInterfacesSpeed();
        }

        #endregion


        public IWindowsCollection AddTopCpuProcessesSensors(int count = 10, double minPercent = 1.0, TimeSpan? period = null)
        {
            return ToWindows(new WindowsTopCpuMonitor(_storage, count, minPercent, period ?? TimeSpan.FromMinutes(1)));
        }


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

        private WindowsSensorsCollection ToWindows(ISensor sensor) => (WindowsSensorsCollection)Register(sensor);
    }
}