using HSMDataCollector.Prototypes;

namespace HSMDataCollector.Options
{
    internal sealed class PrototypesCollection
    {
        #region System

        internal ProcessThreadCountPrototype ProcessThreadCount { get; } = new ProcessThreadCountPrototype();

        internal ProcessMemoryPrototype ProcessMemory { get; } = new ProcessMemoryPrototype();

        internal ProcessCpuPrototype ProcessCpu { get; } = new ProcessCpuPrototype();


        internal FreeRamMemoryPrototype FreeRam { get; } = new FreeRamMemoryPrototype();

        internal TotalCPUPrototype TotalCPU { get; } = new TotalCPUPrototype();

        #endregion


        #region Disks

        internal FreeSpaceOnDiskPredictionPrototype FreeSpaceOnDiskPrediction { get; } = new FreeSpaceOnDiskPredictionPrototype();

        internal FreeSpaceOnDiskPrototype FreeSpaceOnDisk { get; } = new FreeSpaceOnDiskPrototype();

        #endregion



        internal WindowsInfoMonitoringPrototype WindowsInfo { get; } /*= new WindowsInfoMonitoringPrototype();*/


        internal ProductVersionPrototype ProductVersion { get; } = new ProductVersionPrototype();


        internal CollectorVersionPrototype CollectorVersion { get; } = new CollectorVersionPrototype();

        internal ServiceAlivePrototype CollectorAlive { get; } = new ServiceAlivePrototype();
    }
}