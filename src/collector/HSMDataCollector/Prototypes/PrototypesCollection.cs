using HSMDataCollector.Prototypes;
using HSMDataCollector.Prototypes.Collections.Disks;

namespace HSMDataCollector.Options
{
    internal sealed class PrototypesCollection
    {
        #region System

        internal ProcessThreadCountPrototype ProcessThreadCount { get; }

        internal ProcessMemoryPrototype ProcessMemory { get; }

        internal ProcessCpuPrototype ProcessCpu { get; }


        internal FreeRamMemoryPrototype FreeRam { get; }

        internal TotalCPUPrototype TotalCPU { get; }

        #endregion


        #region Disks

        internal WindowsFreeSpaceOnDiskPredictionPrototype WindowsFreeSpaceOnDiskPrediction { get; }

        internal WindowsFreeSpaceOnDiskPrototype WindowsFreeSpaceOnDisk { get; }

        internal WindowsActiveTimeDiskPrototype WindowsActiveTimeDisk { get; }


        internal UnixFreeSpaceOnDiskPredictionPrototype UnixFreeSpaceOnDiskPrediction { get; }

        internal UnixFreeSpaceOnDiskPrototype UnixFreeSpaceOnDisk { get; }

        #endregion


        #region Windows Os Info

        internal WindowsLastRestartPrototype WindowsLastRestart { get; }

        internal WindowsLastUpdatePrototype WindowsLastUpdate { get; }

        #endregion


        #region Product Info

        internal CollectorVersionPrototype CollectorVersion { get; } = new CollectorVersionPrototype();

        internal ServiceAlivePrototype CollectorAlive { get; } = new ServiceAlivePrototype();


        internal ProductVersionPrototype ProductVersion { get; } = new ProductVersionPrototype();


        internal ServiceCommandsPrototype ServiceCommands { get; } = new ServiceCommandsPrototype();

        internal ServiceStatusPrototype ServiceStatus { get; } = new ServiceStatusPrototype();

        #endregion


        internal PrototypesCollection(string module)
        {
            T Register<T>() where T : SensorOptions, new() => new T()
            {
                Module = module
            };


            ProcessThreadCount = Register<ProcessThreadCountPrototype>();
            ProcessMemory = Register<ProcessMemoryPrototype>();
            ProcessCpu = Register<ProcessCpuPrototype>();
            FreeRam = Register<FreeRamMemoryPrototype>();
            TotalCPU = Register<TotalCPUPrototype>();

            WindowsFreeSpaceOnDiskPrediction = Register<WindowsFreeSpaceOnDiskPredictionPrototype>();
            WindowsFreeSpaceOnDisk = Register<WindowsFreeSpaceOnDiskPrototype>();
            WindowsActiveTimeDisk = Register<WindowsActiveTimeDiskPrototype>();

            UnixFreeSpaceOnDiskPrediction = Register<UnixFreeSpaceOnDiskPredictionPrototype>();
            UnixFreeSpaceOnDisk = Register<UnixFreeSpaceOnDiskPrototype>();

            WindowsLastRestart = Register<WindowsLastRestartPrototype>();
            WindowsLastUpdate = Register<WindowsLastUpdatePrototype>();

            CollectorVersion = Register<CollectorVersionPrototype>();
            CollectorAlive = Register<ServiceAlivePrototype>();

            ServiceCommands = Register<ServiceCommandsPrototype>();
            ProductVersion = Register<ProductVersionPrototype>();
            ServiceStatus = Register<ServiceStatusPrototype>();
        }
    }
}