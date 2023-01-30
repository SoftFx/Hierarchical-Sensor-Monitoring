using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors.Common;
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


        IWindowsCollection IWindowsCollection.AddProcessSensors(BarSensorOptions options)
        {
            options = _defaultOptions.GetProcessOptions(options);

            if (options.NodePath == null)
                options.NodePath = SensorsDefaultOptions.CurrentProcessNodeName;

            return (this as IWindowsCollection).AddProcessCpu(options)
                                               .AddProcessMemory(options)
                                               .AddProcessThreadCount(options);
        }

        IWindowsCollection IWindowsCollection.AddSystemMonitoringSensors(BarSensorOptions options)
        {
            options = _defaultOptions.GetSystemMonitoringOptions(options);

            if (options.NodePath == null)
                options.NodePath = SensorsDefaultOptions.SystemMonitoringNodeName;

            return (this as IWindowsCollection).AddFreeRamMemory(options)
                                               .AddTotalCpu(options);
        }

        IWindowsCollection IWindowsCollection.AddDiskMonitoringSensors(DiskSensorOptions options)
        {
            options = _defaultOptions.GetDiskMonitoringOptions(options);

            if (options.NodePath == null)
                options.NodePath = SensorsDefaultOptions.DiskMonitoringNodeName;

            return (this as IWindowsCollection).AddFreeDiskSpace(options)
                                               .AddFreeDiskSpacePredictor(options);
        }

        IWindowsCollection IWindowsCollection.AddWindowsInfoSensors(WindowsSensorOptions options)
        {
            options = _defaultOptions.GetWindowsOptions(options);

            if (options.NodePath == null)
                options.NodePath = SensorsDefaultOptions.WindowsInfoNodeName;

            return (this as IWindowsCollection).AddWindowsNeedUpdate(options)
                                               .AddWindowsLastUpdate(options)
                                               .AddWindowsLastRestart(options);
        }


        IWindowsCollection IWindowsCollection.AddProcessCpu(BarSensorOptions options)
        {
            return ToWindows(new WindowsProcessCpu(_defaultOptions.GetProcessOptions(options)));
        }

        IWindowsCollection IWindowsCollection.AddProcessMemory(BarSensorOptions options)
        {
            return ToWindows(new WindowsProcessMemory(_defaultOptions.GetProcessOptions(options)));
        }

        IWindowsCollection IWindowsCollection.AddProcessThreadCount(BarSensorOptions options)
        {
            return ToWindows(new WindowsProcessThreadCount(_defaultOptions.GetProcessOptions(options)));
        }

        IWindowsCollection IWindowsCollection.AddTotalCpu(BarSensorOptions options)
        {
            return ToWindows(new WindowsTotalCpu(_defaultOptions.GetSystemMonitoringOptions(options)));
        }

        IWindowsCollection IWindowsCollection.AddFreeRamMemory(BarSensorOptions options)
        {
            return ToWindows(new WindowsFreeRamMemory(_defaultOptions.GetSystemMonitoringOptions(options)));
        }

        IWindowsCollection IWindowsCollection.AddFreeDiskSpace(DiskSensorOptions options)
        {
            return ToWindows(new WindowsFreeDiskSpace(_defaultOptions.GetDiskMonitoringOptions(options)));
        }

        IWindowsCollection IWindowsCollection.AddFreeDiskSpacePredictor(DiskSensorOptions options)
        {
            return ToWindows(new WindowsFreeDiskSpacePredictor(_defaultOptions.GetDiskMonitoringOptions(options)));
        }

        IWindowsCollection IWindowsCollection.AddWindowsNeedUpdate(WindowsSensorOptions options)
        {
            return ToWindows(new WindowsNeedUpdate(_defaultOptions.GetWindowsOptions(options)));
        }

        IWindowsCollection IWindowsCollection.AddWindowsLastUpdate(WindowsSensorOptions options)
        {
            return ToWindows(new WindowsLastUpdate(_defaultOptions.GetWindowsOptions(options)));
        }

        IWindowsCollection IWindowsCollection.AddWindowsLastRestart(WindowsSensorOptions options)
        {
            return ToWindows(new WindowsLastRestart(_defaultOptions.GetWindowsOptions(options)));
        }

        IWindowsCollection IWindowsCollection.AddCollectorAlive(SensorOptions options)
        {
            return Register(new CollectorAlive(_defaultOptions.GetCollectorAliveOptions(options)));
        }


        IUnixCollection IUnixCollection.AddProcessSensors(BarSensorOptions options)
        {
            options = _defaultOptions.GetProcessOptions(options);

            if (options.NodePath == null)
                options.NodePath = SensorsDefaultOptions.CurrentProcessNodeName;

            return (this as IUnixCollection).AddProcessCpu(options)
                                            .AddProcessMemory(options)
                                            .AddProcessThreadCount(options);
        }


        IUnixCollection IUnixCollection.AddProcessCpu(BarSensorOptions options)
        {
            return ToUnix(new UnixProcessCpu(_defaultOptions.GetProcessOptions(options)));
        }

        IUnixCollection IUnixCollection.AddProcessMemory(BarSensorOptions options)
        {
            return ToUnix(new UnixProcessMemory(_defaultOptions.GetProcessOptions(options)));
        }

        IUnixCollection IUnixCollection.AddProcessThreadCount(BarSensorOptions options)
        {
            return ToUnix(new UnixProcessThreadCount(_defaultOptions.GetProcessOptions(options)));
        }

        IUnixCollection IUnixCollection.AddCollectorAlive(SensorOptions options)
        {
            return Register(new CollectorAlive(_defaultOptions.GetCollectorAliveOptions(options)));
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
