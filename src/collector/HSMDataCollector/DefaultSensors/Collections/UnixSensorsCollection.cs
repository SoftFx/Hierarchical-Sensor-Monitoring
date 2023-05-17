using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors.Unix;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;

namespace HSMDataCollector.DefaultSensors
{
    internal sealed class UnixSensorsCollection : DefaultSensorsCollection, IUnixCollection
    {
        protected override bool IsCorrectOs => !DataCollector.IsWindowsOS;


        internal UnixSensorsCollection(SensorsStorage storage, SensorsPrototype prototype) : base(storage, prototype) { }


        #region Process

        public IUnixCollection AddProcessCpu(BarSensorOptions options)
        {
            return ToUnix(new UnixProcessCpu(_prototype.ProcessMonitoring.Get(options)));
        }

        public IUnixCollection AddProcessMemory(BarSensorOptions options)
        {
            return ToUnix(new UnixProcessMemory(_prototype.ProcessMonitoring.Get(options)));
        }

        public IUnixCollection AddProcessThreadCount(BarSensorOptions options)
        {
            return ToUnix(new UnixProcessThreadCount(_prototype.ProcessMonitoring.Get(options)));
        }

        public IUnixCollection AddProcessMonitoringSensors(BarSensorOptions options)
        {
            options = _prototype.ProcessMonitoring.GetAndFill(options);

            return AddProcessCpu(options).AddProcessMemory(options).AddProcessThreadCount(options);
        }

        #endregion


        #region System

        public IUnixCollection AddTotalCpu(BarSensorOptions options)
        {
            return ToUnix(new UnixTotalCpu(_prototype.SystemMonitoring.Get(options)));
        }

        public IUnixCollection AddFreeRamMemory(BarSensorOptions options)
        {
            return ToUnix(new UnixFreeRamMemory(_prototype.SystemMonitoring.Get(options)));
        }

        public IUnixCollection AddSystemMonitoringSensors(BarSensorOptions options)
        {
            options = _prototype.SystemMonitoring.GetAndFill(options);

            return AddFreeRamMemory(options).AddTotalCpu(options);
        }

        #endregion


        #region Disk

        public IUnixCollection AddFreeDiskSpace(DiskSensorOptions options)
        {
            return ToUnix(new UnixFreeDiskSpace(_prototype.DiskMonitoring.Get(options)));
        }

        public IUnixCollection AddFreeDiskSpacePrediction(DiskSensorOptions options)
        {
            return ToUnix(new UnixFreeDiskSpacePrediction(_prototype.DiskMonitoring.Get(options)));
        }

        public IUnixCollection AddDiskMonitoringSensors(DiskSensorOptions options)
        {
            options = _prototype.DiskMonitoring.GetAndFill(options);

            return AddFreeDiskSpace(options).AddFreeDiskSpacePrediction(options);
        }

        #endregion


        #region Collector

        public IUnixCollection AddCollectorHeartbeat(CollectorMonitoringInfoOptions options) => (IUnixCollection)AddCollectorHeartbeatCommon(options);

        public IUnixCollection AddCollectorVersion(CollectorInfoOptions options) => (IUnixCollection)AddCollectorVersionCommon(options);

        public IUnixCollection AddCollectorStatus(CollectorInfoOptions options) => (IUnixCollection)AddCollectorStatusCommon(options);

        public IUnixCollection AddCollectorMonitoringSensors(CollectorMonitoringInfoOptions options) => (IUnixCollection)AddFullCollectorMonitoringCommon(options);

        #endregion


        public IUnixCollection AddProductVersion(VersionSensorOptions options) => (IUnixCollection)AddProductVersionCommon(options);


        private UnixSensorsCollection ToUnix(SensorBase sensor) => (UnixSensorsCollection)Register(sensor);
    }
}
