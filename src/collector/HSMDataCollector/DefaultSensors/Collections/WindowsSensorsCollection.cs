using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors.Windows;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using System;

namespace HSMDataCollector.DefaultSensors
{
    internal sealed class WindowsSensorsCollection : DefaultSensorsCollection, IWindowsCollection
    {
        protected override bool IsCorrectOs => DataCollector.IsWindowsOS;


        public WindowsSensorsCollection(SensorsStorage storage, SensorsPrototype prototype) : base(storage, prototype) { }


        #region Process

        public IWindowsCollection AddProcessCpu(BarSensorOptions options)
        {
            return ToWindows(new WindowsProcessCpu(_prototype.ProcessMonitoring.Get(options)));
        }

        public IWindowsCollection AddProcessMemory(BarSensorOptions options)
        {
            return ToWindows(new WindowsProcessMemory(_prototype.ProcessMonitoring.Get(options)));
        }

        public IWindowsCollection AddProcessThreadCount(BarSensorOptions options)
        {
            return ToWindows(new WindowsProcessThreadCount(_prototype.ProcessMonitoring.Get(options)));
        }

        public IWindowsCollection AddProcessMonitoringSensors(BarSensorOptions options)
        {
            options = _prototype.ProcessMonitoring.GetAndFill(options);

            return AddProcessCpu(options).AddProcessMemory(options).AddProcessThreadCount(options);
        }

        #endregion


        #region System

        public IWindowsCollection AddTotalCpu(BarSensorOptions options)
        {
            return ToWindows(new WindowsTotalCpu(_prototype.SystemMonitoring.Get(options)));
        }

        public IWindowsCollection AddFreeRamMemory(BarSensorOptions options)
        {
            return ToWindows(new WindowsFreeRamMemory(_prototype.SystemMonitoring.Get(options)));
        }

        public IWindowsCollection AddSystemMonitoringSensors(BarSensorOptions options)
        {
            options = _prototype.SystemMonitoring.GetAndFill(options);

            return AddFreeRamMemory(options).AddTotalCpu(options);
        }

        #endregion


        #region Disk

        public IWindowsCollection AddFreeDiskSpace(DiskSensorOptions options)
        {
            return ToWindows(new WindowsFreeDiskSpace(_prototype.DiskMonitoring.Get(options)));
        }

        public IWindowsCollection AddFreeDiskSpacePrediction(DiskSensorOptions options)
        {
            return ToWindows(new WindowsFreeDiskSpacePrediction(_prototype.DiskMonitoring.Get(options)));
        }

        public IWindowsCollection AddFreeDisksSpace(DiskSensorOptions options)
        {
            return AddDisksMonitoring(options, o => new WindowsFreeDiskSpace(o));
        }

        public IWindowsCollection AddFreeDisksSpacePrediction(DiskSensorOptions options)
        {
            return AddDisksMonitoring(options, o => new WindowsFreeDiskSpacePrediction(o));
        }

        public IWindowsCollection AddDiskMonitoringSensors(DiskSensorOptions options)
        {
            options = _prototype.DiskMonitoring.GetAndFill(options);

            return AddFreeDiskSpace(options).AddFreeDiskSpacePrediction(options);
        }

        private IWindowsCollection AddDisksMonitoring(DiskSensorOptions options, Func<DiskSensorOptions, SensorBase> newSensorFunc)
        {
            foreach (var diskOptions in _prototype.DiskMonitoring.GetAllDisksOptions(options))
                ToWindows(newSensorFunc(diskOptions));

            return this;
        }

        #endregion


        #region Windows

        public IWindowsCollection AddWindowsNeedUpdate(WindowsSensorOptions options)
        {
            return ToWindows(new WindowsNeedUpdate(_prototype.WindowsInfo.Get(options)));
        }

        public IWindowsCollection AddWindowsLastUpdate(WindowsSensorOptions options)
        {
            return ToWindows(new WindowsLastUpdate(_prototype.WindowsInfo.Get(options)));
        }

        public IWindowsCollection AddWindowsLastRestart(WindowsSensorOptions options)
        {
            return ToWindows(new WindowsLastRestart(_prototype.WindowsInfo.Get(options)));
        }

        public IWindowsCollection AddWindowsInfoMonitoringSensors(WindowsSensorOptions options)
        {
            options = _prototype.WindowsInfo.GetAndFill(options);

            return AddWindowsNeedUpdate(options).AddWindowsLastUpdate(options).AddWindowsLastRestart(options);
        }

        #endregion


        #region Collector

        public IWindowsCollection AddCollectorAlive(CollectorMonitoringInfoOptions options) => (IWindowsCollection)AddCollectorAliveCommon(options);

        public IWindowsCollection AddCollectorVersion(CollectorInfoOptions options) => (IWindowsCollection)AddCollectorVersionCommon(options);

        public IWindowsCollection AddCollectorStatus(CollectorInfoOptions options) => (IWindowsCollection)AddCollectorStatusCommon(options);

        public IWindowsCollection AddCollectorMonitoringSensors(CollectorMonitoringInfoOptions options) => (IWindowsCollection)AddFullCollectorMonitoringCommon(options);

        #endregion


        public IWindowsCollection AddProductVersion(VersionSensorOptions options) => (IWindowsCollection)AddProductVersionCommon(options);


        private WindowsSensorsCollection ToWindows(SensorBase sensor) => (WindowsSensorsCollection)Register(sensor);
    }
}
