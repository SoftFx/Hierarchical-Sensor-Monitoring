﻿using HSMDataCollector.Core;
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
        private readonly SensorsDefaultOptions _default;


        internal bool IsUnixOS { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                                          RuntimeInformation.IsOSPlatform(OSPlatform.Linux);


        internal DefaultSensorsCollection(SensorsStorage storage, SensorsDefaultOptions sensorsOptions)
        {
            _storage = storage;
            _default = sensorsOptions;
        }


        IWindowsCollection IWindowsCollection.AddProcessCpu(BarSensorOptions options)
        {
            return ToWindows(new WindowsProcessCpu(_default.GetProcessOptions(options)));
        }

        IWindowsCollection IWindowsCollection.AddProcessMemory(BarSensorOptions options)
        {
            return ToWindows(new WindowsProcessMemory(_default.GetProcessOptions(options)));
        }

        IWindowsCollection IWindowsCollection.AddProcessThreadCount(BarSensorOptions options)
        {
            return ToWindows(new WindowsProcessThreadCount(_default.GetProcessOptions(options)));
        }

        IWindowsCollection IWindowsCollection.AddProcessMonitoringSensors(BarSensorOptions options)
        {
            options = _default.GetProcessOptions(options);

            if (options.NodePath == null)
                options.NodePath = SensorsDefaultOptions.CurrentProcessNodeName;

            return (this as IWindowsCollection).AddProcessCpu(options)
                                               .AddProcessMemory(options)
                                               .AddProcessThreadCount(options);
        }


        IWindowsCollection IWindowsCollection.AddTotalCpu(BarSensorOptions options)
        {
            return ToWindows(new WindowsTotalCpu(_default.GetSystemMonitoringOptions(options)));
        }

        IWindowsCollection IWindowsCollection.AddFreeRamMemory(BarSensorOptions options)
        {
            return ToWindows(new WindowsFreeRamMemory(_default.GetSystemMonitoringOptions(options)));
        }

        IWindowsCollection IWindowsCollection.AddSystemMonitoringSensors(BarSensorOptions options)
        {
            options = _default.GetSystemMonitoringOptions(options);

            if (options.NodePath == null)
                options.NodePath = SensorsDefaultOptions.SystemMonitoringNodeName;

            return (this as IWindowsCollection).AddFreeRamMemory(options)
                                               .AddTotalCpu(options);
        }


        IWindowsCollection IWindowsCollection.AddFreeDiskSpace(DiskSensorOptions options)
        {
            return ToWindows(new WindowsFreeDiskSpace(_default.GetDiskMonitoringOptions(options)));
        }

        IWindowsCollection IWindowsCollection.AddFreeDiskSpacePredictor(DiskSensorOptions options)
        {
            return ToWindows(new WindowsFreeDiskSpacePredictor(_default.GetDiskMonitoringOptions(options)));
        }

        IWindowsCollection IWindowsCollection.AddDiskMonitoringSensors(DiskSensorOptions options)
        {
            options = _default.GetDiskMonitoringOptions(options);

            if (options.NodePath == null)
                options.NodePath = SensorsDefaultOptions.DiskMonitoringNodeName;

            return (this as IWindowsCollection).AddFreeDiskSpace(options)
                                               .AddFreeDiskSpacePredictor(options);
        }


        IWindowsCollection IWindowsCollection.AddWindowsNeedUpdate(WindowsSensorOptions options)
        {
            return ToWindows(new WindowsNeedUpdate(_default.GetWindowsOptions(options)));
        }

        IWindowsCollection IWindowsCollection.AddWindowsLastUpdate(WindowsSensorOptions options)
        {
            return ToWindows(new WindowsLastUpdate(_default.GetWindowsOptions(options)));
        }

        IWindowsCollection IWindowsCollection.AddWindowsLastRestart(WindowsSensorOptions options)
        {
            return ToWindows(new WindowsLastRestart(_default.GetWindowsOptions(options)));
        }

        IWindowsCollection IWindowsCollection.AddWindowsInfoMonitoringSensors(WindowsSensorOptions options)
        {
            options = _default.GetWindowsOptions(options);

            if (options.NodePath == null)
                options.NodePath = SensorsDefaultOptions.WindowsInfoNodeName;

            return (this as IWindowsCollection).AddWindowsNeedUpdate(options)
                                               .AddWindowsLastUpdate(options)
                                               .AddWindowsLastRestart(options);
        }


        IWindowsCollection IWindowsCollection.AddCollectorAlive(SensorOptions options)
        {
            return Register(new CollectorAlive(_default.GetCollectorAliveOptions(options)));
        }


        IUnixCollection IUnixCollection.AddProcessCpu(BarSensorOptions options)
        {
            return ToUnix(new UnixProcessCpu(_default.GetProcessOptions(options)));
        }

        IUnixCollection IUnixCollection.AddProcessMemory(BarSensorOptions options)
        {
            return ToUnix(new UnixProcessMemory(_default.GetProcessOptions(options)));
        }

        IUnixCollection IUnixCollection.AddProcessThreadCount(BarSensorOptions options)
        {
            return ToUnix(new UnixProcessThreadCount(_default.GetProcessOptions(options)));
        }

        IUnixCollection IUnixCollection.AddProcessMonitoringSensors(BarSensorOptions options)
        {
            options = _default.GetProcessOptions(options);

            if (options.NodePath == null)
                options.NodePath = SensorsDefaultOptions.CurrentProcessNodeName;

            return (this as IUnixCollection).AddProcessCpu(options)
                                            .AddProcessMemory(options)
                                            .AddProcessThreadCount(options);
        }


        IUnixCollection IUnixCollection.AddCollectorAlive(SensorOptions options)
        {
            return Register(new CollectorAlive(_default.GetCollectorAliveOptions(options)));
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
