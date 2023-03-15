using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors.Other;
using HSMDataCollector.DefaultSensors.Unix;
using HSMDataCollector.DefaultSensors.Windows;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using System;
using System.Runtime.InteropServices;

namespace HSMDataCollector.DefaultSensors
{
    internal sealed class DefaultSensorsCollection : IWindowsCollection, IUnixCollection
    {
        private const string NotSupportedSensor = "Sensor is not supported for current OS";

        private static readonly NotSupportedException _notSupportedException = new NotSupportedException(NotSupportedSensor);

        private readonly SensorsStorage _storage;
        private readonly SensorsDefaultOptions _defaultOptions;


        internal bool IsUnixOS { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                                          RuntimeInformation.IsOSPlatform(OSPlatform.Linux);


        internal DefaultSensorsCollection(SensorsStorage storage, SensorsDefaultOptions sensorsOptions)
        {
            _storage = storage;
            _defaultOptions = sensorsOptions;
        }


        IWindowsCollection IWindowsCollection.AddProcessCpu(BarSensorOptions options)
        {
            return ToWindows(new WindowsProcessCpu(_defaultOptions.ProcessMonitoring.Get(options)));
        }

        IWindowsCollection IWindowsCollection.AddProcessMemory(BarSensorOptions options)
        {
            return ToWindows(new WindowsProcessMemory(_defaultOptions.ProcessMonitoring.Get(options)));
        }

        IWindowsCollection IWindowsCollection.AddProcessThreadCount(BarSensorOptions options)
        {
            return ToWindows(new WindowsProcessThreadCount(_defaultOptions.ProcessMonitoring.Get(options)));
        }

        IWindowsCollection IWindowsCollection.AddProcessMonitoringSensors(BarSensorOptions options)
        {
            options = _defaultOptions.ProcessMonitoring.GetAndFill(options);

            return (this as IWindowsCollection).AddProcessCpu(options)
                                               .AddProcessMemory(options)
                                               .AddProcessThreadCount(options);
        }


        IWindowsCollection IWindowsCollection.AddTotalCpu(BarSensorOptions options)
        {
            return ToWindows(new WindowsTotalCpu(_defaultOptions.SystemMonitoring.Get(options)));
        }

        IWindowsCollection IWindowsCollection.AddFreeRamMemory(BarSensorOptions options)
        {
            return ToWindows(new WindowsFreeRamMemory(_defaultOptions.SystemMonitoring.Get(options)));
        }

        IWindowsCollection IWindowsCollection.AddSystemMonitoringSensors(BarSensorOptions options)
        {
            options = _defaultOptions.SystemMonitoring.GetAndFill(options);

            return (this as IWindowsCollection).AddFreeRamMemory(options)
                                               .AddTotalCpu(options);
        }


        IWindowsCollection IWindowsCollection.AddFreeDiskSpace(DiskSensorOptions options)
        {
            return ToWindows(new WindowsFreeDiskSpace(_defaultOptions.DiskMonitoring.Get(options)));
        }

        IWindowsCollection IWindowsCollection.AddFreeDiskSpacePrediction(DiskSensorOptions options)
        {
            return ToWindows(new WindowsFreeDiskSpacePrediction(_defaultOptions.DiskMonitoring.Get(options)));
        }

        IWindowsCollection IWindowsCollection.AddFreeDisksSpace(DiskSensorOptions options)
        {
            return AddDisksMonitoring(options, o => new WindowsFreeDiskSpace(o));
        }

        IWindowsCollection IWindowsCollection.AddFreeDisksSpacePrediction(DiskSensorOptions options)
        {
            return AddDisksMonitoring(options, o => new WindowsFreeDiskSpacePrediction(o));
        }

        IWindowsCollection IWindowsCollection.AddDiskMonitoringSensors(DiskSensorOptions options)
        {
            options = _defaultOptions.DiskMonitoring.GetAndFill(options);

            return (this as IWindowsCollection).AddFreeDiskSpace(options)
                                               .AddFreeDiskSpacePrediction(options);
        }


        IWindowsCollection IWindowsCollection.AddWindowsNeedUpdate(WindowsSensorOptions options)
        {
            return ToWindows(new WindowsNeedUpdate(_defaultOptions.WindowsInfoMonitoring.Get(options)));
        }

        IWindowsCollection IWindowsCollection.AddWindowsLastUpdate(WindowsSensorOptions options)
        {
            return ToWindows(new WindowsLastUpdate(_defaultOptions.WindowsInfoMonitoring.Get(options)));
        }

        IWindowsCollection IWindowsCollection.AddWindowsLastRestart(WindowsSensorOptions options)
        {
            return ToWindows(new WindowsLastRestart(_defaultOptions.WindowsInfoMonitoring.Get(options)));
        }

        IWindowsCollection IWindowsCollection.AddWindowsInfoMonitoringSensors(WindowsSensorOptions options)
        {
            options = _defaultOptions.WindowsInfoMonitoring.GetAndFill(options);

            return (this as IWindowsCollection).AddWindowsNeedUpdate(options)
                                               .AddWindowsLastUpdate(options)
                                               .AddWindowsLastRestart(options);
        }


        IWindowsCollection IWindowsCollection.AddCollectorHeartbeat(SensorOptions options)
        {
            return Register(new CollectorAlive(_defaultOptions.CollectorAliveMonitoring.Get(options)));
        }

        IWindowsCollection IWindowsCollection.AddProductInfo(VersionSensorOptions options)
        {
            return Register(new ProductInfoSensor(_defaultOptions.ProductInfoMonitoring.GetAndFill(options)));
        }


        IUnixCollection IUnixCollection.AddProcessCpu(BarSensorOptions options)
        {
            return ToUnix(new UnixProcessCpu(_defaultOptions.ProcessMonitoring.Get(options)));
        }

        IUnixCollection IUnixCollection.AddProcessMemory(BarSensorOptions options)
        {
            return ToUnix(new UnixProcessMemory(_defaultOptions.ProcessMonitoring.Get(options)));
        }

        IUnixCollection IUnixCollection.AddProcessThreadCount(BarSensorOptions options)
        {
            return ToUnix(new UnixProcessThreadCount(_defaultOptions.ProcessMonitoring.Get(options)));
        }

        IUnixCollection IUnixCollection.AddProcessMonitoringSensors(BarSensorOptions options)
        {
            options = _defaultOptions.ProcessMonitoring.GetAndFill(options);

            return (this as IUnixCollection).AddProcessCpu(options)
                                            .AddProcessMemory(options)
                                            .AddProcessThreadCount(options);
        }


        IUnixCollection IUnixCollection.AddFreeDiskSpace(DiskSensorOptions options)
        {
            return ToUnix(new UnixFreeDiskSpace(_defaultOptions.DiskMonitoring.Get(options)));
        }

        IUnixCollection IUnixCollection.AddFreeDiskSpacePrediction(DiskSensorOptions options)
        {
            return ToUnix(new UnixFreeDiskSpacePrediction(_defaultOptions.DiskMonitoring.Get(options)));
        }

        IUnixCollection IUnixCollection.AddDiskMonitoringSensors(DiskSensorOptions options)
        {
            options = _defaultOptions.DiskMonitoring.GetAndFill(options);

            return (this as IUnixCollection).AddFreeDiskSpace(options)
                                            .AddFreeDiskSpacePrediction(options);
        }


        IUnixCollection IUnixCollection.AddCollectorHeartbeat(SensorOptions options)
        {
            return Register(new CollectorAlive(_defaultOptions.CollectorAliveMonitoring.Get(options)));
        }
        
        IUnixCollection IUnixCollection.AddProductInfo(VersionSensorOptions options)
        {
            return Register(new ProductInfoSensor(_defaultOptions.ProductInfoMonitoring.GetAndFill(options)));
        }

        private DefaultSensorsCollection AddDisksMonitoring(DiskSensorOptions options, Func<DiskSensorOptions, MonitoringSensorBase> newSensorFunc)
        {
            foreach (var diskOptions in _defaultOptions.DiskMonitoring.GetAllDisksOptions(options))
                ToWindows(newSensorFunc(diskOptions));

            return this;
        }

        private DefaultSensorsCollection ToWindows(MonitoringSensorBase sensor)
        {
            return !IsUnixOS ? Register(sensor) : throw _notSupportedException;
        }

        private DefaultSensorsCollection ToUnix(MonitoringSensorBase sensor)
        {
            return IsUnixOS ? Register(sensor) : throw _notSupportedException;
        }

        private DefaultSensorsCollection Register(MonitoringSensorBase sensor)
        {
            _storage.Register(sensor.SensorPath, sensor);

            return this;
        }
    }
}
