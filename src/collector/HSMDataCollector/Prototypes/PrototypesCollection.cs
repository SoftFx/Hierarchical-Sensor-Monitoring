﻿using HSMDataCollector.Core;
using HSMDataCollector.Prototypes;
using HSMDataCollector.Prototypes.Collections;
using HSMDataCollector.Prototypes.Collections.Disks;
using HSMDataCollector.Prototypes.Collections.Network;

namespace HSMDataCollector.Options
{
    internal sealed class PrototypesCollection
    {
        #region System

        internal ProcessThreadCountPrototype ProcessThreadCount { get; }

        internal ProcessTimeInGCPrototype ProcessTimeInGC { get; }

        internal ProcessMemoryPrototype ProcessMemory { get; }

        internal ProcessCpuPrototype ProcessCpu { get; }


        internal FreeRamMemoryPrototype FreeRam { get; }

        internal TotalCPUPrototype TotalCPU { get; }

        internal TimeInGCPrototype TimeInGC { get; }

        #endregion


        #region Disks

        internal WindowsFreeSpaceOnDiskPredictionPrototype WindowsFreeSpaceOnDiskPrediction { get; }

        internal WindowsDiskWriteSpeedPrototype WindowsAverageDiskWriteSpeed { get; }

        internal WindowsDiskQueueLengthPrototype WindowsDiskQueueLength { get; }

        internal WindowsFreeSpaceOnDiskPrototype WindowsFreeSpaceOnDisk { get; }

        internal WindowsActiveTimeDiskPrototype WindowsActiveTimeDisk { get; }


        internal UnixFreeSpaceOnDiskPredictionPrototype UnixFreeSpaceOnDiskPrediction { get; }

        internal UnixFreeSpaceOnDiskPrototype UnixFreeSpaceOnDisk { get; }

        #endregion


        #region Windows Os Info

        internal WindowsLastRestartPrototype WindowsLastRestart { get; }

        internal WindowsLastUpdatePrototype WindowsLastUpdate { get; }

        internal WindowsVersionPrototype WindowsVersion { get; }


        internal WindowsApplicationErrorLogsPrototype WindowsApplicationErrorLogsPrototype { get; }

        internal WindowsSystemErrorLogsPrototype WindowsSystemErrorLogsPrototype { get; }


        internal WindowsApplicationWarningLogsPrototype WindowsApplicationWarningLogsPrototype { get; }

        internal WindowsSystemWarningLogsPrototype WindowsSystemWarningLogsPrototype { get; }

        #endregion


        #region Product Info

        internal CollectorVersionPrototype CollectorVersion { get; } = new CollectorVersionPrototype();

        internal CollectorErrorsPrototype CollectorErrors { get; } = new CollectorErrorsPrototype();

        internal ServiceAlivePrototype CollectorAlive { get; } = new ServiceAlivePrototype();



        internal ProductVersionPrototype ProductVersion { get; } = new ProductVersionPrototype();


        internal ServiceCommandsPrototype ServiceCommands { get; } = new ServiceCommandsPrototype();

        internal ServiceStatusPrototype ServiceStatus { get; } = new ServiceStatusPrototype();

        #endregion

        #region Queue diagnostic info

        internal QueueOverflowPrototype QueueOverflow { get; } = new QueueOverflowPrototype();

        internal PackageContentSizePrototype PackageContentSize { get; } = new PackageContentSizePrototype();

        internal PackageProcessTimePrototype PackageProcessTime { get; } = new PackageProcessTimePrototype();

        internal PackageValuesCountPrototype PackageValuesCount { get; } = new PackageValuesCountPrototype();

        #endregion


        #region Network

        public ConnectionsEstablishedCountPrototype ConnectionsEstablishedCount { get; set; }

        public ConnectionsFailuresCountPrototype ConnectionsFailuresCount { get; set; }

        public ConnectionsResetCountPrototype ConnectionsResetCount { get; set; }


        #endregion


        internal PrototypesCollection(CollectorOptions options)
        {
            T Register<T>() where T : SensorOptions, new() => new T()
            {
                ComputerName = options.ComputerName,
                Module = options.Module,
            };


            ProcessThreadCount = Register<ProcessThreadCountPrototype>();
            ProcessTimeInGC = Register<ProcessTimeInGCPrototype>();
            ProcessMemory = Register<ProcessMemoryPrototype>();
            ProcessCpu = Register<ProcessCpuPrototype>();
            FreeRam = Register<FreeRamMemoryPrototype>();
            TotalCPU = Register<TotalCPUPrototype>();
            TimeInGC = Register<TimeInGCPrototype>();

            WindowsFreeSpaceOnDiskPrediction = Register<WindowsFreeSpaceOnDiskPredictionPrototype>();
            WindowsFreeSpaceOnDisk = Register<WindowsFreeSpaceOnDiskPrototype>();
            WindowsActiveTimeDisk = Register<WindowsActiveTimeDiskPrototype>();
            WindowsDiskQueueLength = Register<WindowsDiskQueueLengthPrototype>();
            WindowsAverageDiskWriteSpeed = Register<WindowsDiskWriteSpeedPrototype>();

            UnixFreeSpaceOnDiskPrediction = Register<UnixFreeSpaceOnDiskPredictionPrototype>();
            UnixFreeSpaceOnDisk = Register<UnixFreeSpaceOnDiskPrototype>();

            WindowsLastRestart = Register<WindowsLastRestartPrototype>();
            WindowsLastUpdate = Register<WindowsLastUpdatePrototype>();
            WindowsVersion = Register<WindowsVersionPrototype>();

            WindowsApplicationWarningLogsPrototype = Register<WindowsApplicationWarningLogsPrototype>();
            WindowsSystemWarningLogsPrototype = Register<WindowsSystemWarningLogsPrototype>();

            WindowsApplicationErrorLogsPrototype = Register<WindowsApplicationErrorLogsPrototype>();
            WindowsSystemErrorLogsPrototype = Register<WindowsSystemErrorLogsPrototype>();

            CollectorVersion = Register<CollectorVersionPrototype>();
            CollectorErrors = Register<CollectorErrorsPrototype>();
            CollectorAlive = Register<ServiceAlivePrototype>();

            ServiceCommands = Register<ServiceCommandsPrototype>();
            ProductVersion = Register<ProductVersionPrototype>();
            ServiceStatus = Register<ServiceStatusPrototype>();

            PackageValuesCount = Register<PackageValuesCountPrototype>().ApplyOptions(options);
            PackageProcessTime = Register<PackageProcessTimePrototype>().ApplyOptions(options);
            PackageContentSize = Register<PackageContentSizePrototype>();
            QueueOverflow = Register<QueueOverflowPrototype>().ApplyOptions(options);

            ConnectionsEstablishedCount = Register<ConnectionsEstablishedCountPrototype>();
            ConnectionsFailuresCount = Register<ConnectionsFailuresCountPrototype>();
            ConnectionsResetCount = Register<ConnectionsResetCountPrototype>();
        }
    }
}