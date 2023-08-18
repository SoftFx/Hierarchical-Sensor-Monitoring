using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors.Unix;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;

namespace HSMDataCollector.DefaultSensors
{
    internal sealed class UnixSensorsCollection : DefaultSensorsCollection, IUnixCollection
    {
        protected override bool IsCorrectOs => !DataCollector.IsWindowsOS;


        internal UnixSensorsCollection(SensorsStorage storage, PrototypesCollection prototype) : base(storage, prototype) { }


        #region Process

        public IUnixCollection AddProcessCpu(BarSensorOptions options)
        {
            return ToUnix(new UnixProcessCpu(_prototype.ProcessCpu.Get(options)));
        }

        public IUnixCollection AddProcessMemory(BarSensorOptions options)
        {
            return ToUnix(new UnixProcessMemory(_prototype.ProcessMemory.Get(options)));
        }

        public IUnixCollection AddProcessThreadCount(BarSensorOptions options)
        {
            return ToUnix(new UnixProcessThreadCount(_prototype.ProcessThreadCount.Get(options)));
        }

        public IUnixCollection AddProcessMonitoringSensors(BarSensorOptions options) =>
            AddProcessCpu(options).AddProcessMemory(options).AddProcessThreadCount(options);

        #endregion


        #region System

        public IUnixCollection AddTotalCpu(BarSensorOptions options)
        {
            return ToUnix(new UnixTotalCpu(_prototype.TotalCPU.Get(options)));
        }

        public IUnixCollection AddFreeRamMemory(BarSensorOptions options)
        {
            return ToUnix(new UnixFreeRamMemory(_prototype.FreeRam.Get(options)));
        }

        public IUnixCollection AddSystemMonitoringSensors(BarSensorOptions options) =>
            AddFreeRamMemory(options).AddTotalCpu(options);

        #endregion


        #region Disk

        public IUnixCollection AddFreeDiskSpace(DiskSensorOptions options)
        {
            return ToUnix(new UnixFreeDiskSpace(_prototype.FreeSpaceOnDisk.Get(options)));
        }

        public IUnixCollection AddFreeDiskSpacePrediction(DiskSensorOptions options)
        {
            return ToUnix(new UnixFreeDiskSpacePrediction(_prototype.FreeSpaceOnDiskPrediction.Get(options)));
        }

        public IUnixCollection AddDiskMonitoringSensors(DiskSensorOptions options) =>
            AddFreeDiskSpace(options).AddFreeDiskSpacePrediction(options);

        #endregion


        #region Collector

        public IUnixCollection AddCollectorAlive(CollectorMonitoringInfoOptions options) => (IUnixCollection)AddCollectorAliveCommon(options);

        public IUnixCollection AddCollectorVersion(CollectorInfoOptions options) => (IUnixCollection)AddCollectorVersionCommon(options);

        public IUnixCollection AddCollectorMonitoringSensors(CollectorMonitoringInfoOptions options) => (IUnixCollection)AddFullCollectorMonitoringCommon(options);

        #endregion


        public IUnixCollection AddProductVersion(VersionSensorOptions options) => (IUnixCollection)AddProductVersionCommon(options);


        private UnixSensorsCollection ToUnix(SensorBase sensor) => (UnixSensorsCollection)Register(sensor);
    }
}
