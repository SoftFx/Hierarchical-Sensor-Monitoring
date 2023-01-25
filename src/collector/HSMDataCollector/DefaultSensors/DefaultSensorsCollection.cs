using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors.Unix;
using HSMDataCollector.DefaultSensors.Windows;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace HSMDataCollector.DefaultSensors
{
    internal sealed class DefaultSensorsCollection : IEnumerable<MonitoringSensorBase>, IWindowsCollection, IUnixCollection
    {
        private const string NotSupportedSensor = "Sensor is not supported for current OS";

        internal const string CurrentProcessNodeName = "CurrentProcess";
        internal const string SystemMonitoringNodeName = "System monitoring";

        private static readonly NotSupportedException _notSupportedException = new NotSupportedException(NotSupportedSensor);

        private readonly SensorsStorage _storage;

        private readonly BarSensorOptions _processOptions = new BarSensorOptions(CurrentProcessNodeName);
        private readonly BarSensorOptions _monitoringOptions = new BarSensorOptions(SystemMonitoringNodeName);
        private readonly WindowsSensorOptions _windowsOptions =
            new WindowsSensorOptions(SystemMonitoringNodeName)
            {
                PostDataPeriod = TimeSpan.FromHours(24)
            };


        internal bool IsUnixOS { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                                          RuntimeInformation.IsOSPlatform(OSPlatform.Linux);


        internal DefaultSensorsCollection(SensorsStorage storage)
        {
            _storage = storage;
        }


        public IEnumerator<MonitoringSensorBase> GetEnumerator() => _storage.Values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();


        internal bool IsSensorExists(string path) => _storage.ContainsKey(path);


        IWindowsCollection IWindowsCollection.AddProcessSensors(BarSensorOptions options)
        {
            options = GetProcessOptions(options);

            if (options.NodePath == null)
                options.NodePath = CurrentProcessNodeName;

            return (this as IWindowsCollection).AddProcessCpu(options)
                                               .AddProcessMemory(options)
                                               .AddProcessThreadCount(options);
        }

        public IWindowsCollection AddSystemMonitoringSensors(BarSensorOptions options = null)
        {
            options = GetSystemMonitoringOptions(options);

            if (options.NodePath == null)
                options.NodePath = SystemMonitoringNodeName;

            return (this as IWindowsCollection).AddFreeRamMemory(options)
                                               .AddTotalCpu(options);
        }

        public IWindowsCollection AddWindowsSensors(WindowsSensorOptions options = null)
        {
            options = GetWindowsOptions(options);

            if (options.NodePath == null)
                options.NodePath = SystemMonitoringNodeName;

            return (this as IWindowsCollection).AddWindowsNeedUpdate(options)
                                               .AddWindowsLastRestart(options);
        }


        IWindowsCollection IWindowsCollection.AddProcessCpu(BarSensorOptions options)
        {
            return !IsUnixOS
                ? Register(new WindowsProcessCpu(GetProcessOptions(options)))
                : throw _notSupportedException;
        }

        IWindowsCollection IWindowsCollection.AddProcessMemory(BarSensorOptions options)
        {
            return !IsUnixOS
                ? Register(new WindowsProcessMemory(GetProcessOptions(options)))
                : throw _notSupportedException;
        }

        IWindowsCollection IWindowsCollection.AddProcessThreadCount(BarSensorOptions options)
        {
            return !IsUnixOS
                ? Register(new WindowsProcessThreadCount(GetProcessOptions(options)))
                : throw _notSupportedException;
        }

        IWindowsCollection IWindowsCollection.AddTotalCpu(BarSensorOptions options)
        {
            return !IsUnixOS
                ? Register(new WindowsTotalCpu(GetSystemMonitoringOptions(options)))
                : throw _notSupportedException;
        }

        IWindowsCollection IWindowsCollection.AddFreeRamMemory(BarSensorOptions options)
        {
            return !IsUnixOS
                ? Register(new WindowsFreeRamMemory(GetSystemMonitoringOptions(options)))
                : throw _notSupportedException;
        }

        IWindowsCollection IWindowsCollection.AddWindowsNeedUpdate(WindowsSensorOptions options)
        {
            return !IsUnixOS
                ? Register(new WindowsNeedUpdate(GetWindowsOptions(options)))
                : throw _notSupportedException;
        }

        IWindowsCollection IWindowsCollection.AddWindowsLastRestart(WindowsSensorOptions options)
        {
            return !IsUnixOS
                ? Register(new WindowsLastRestart(GetWindowsOptions(options)))
                : throw _notSupportedException;
        }


        public IUnixCollection AddCurrentProcessSensors(BarSensorOptions options = null)
        {
            options = GetProcessOptions(options);

            if (options.NodePath == null)
                options.NodePath = CurrentProcessNodeName;

            return (this as IUnixCollection).AddProcessCpu(options)
                                            .AddProcessMemory(options)
                                            .AddProcessThreadCount(options);
        }


        IUnixCollection IUnixCollection.AddProcessCpu(BarSensorOptions options)
        {
            return IsUnixOS
                ? Register(new UnixProcessCpu(GetProcessOptions(options)))
                : throw _notSupportedException;
        }

        IUnixCollection IUnixCollection.AddProcessMemory(BarSensorOptions options)
        {
            return IsUnixOS
                ? Register(new UnixProcessMemory(GetProcessOptions(options)))
                : throw _notSupportedException;
        }

        IUnixCollection IUnixCollection.AddProcessThreadCount(BarSensorOptions options)
        {
            return IsUnixOS
                ? Register(new UnixProcessThreadCount(GetProcessOptions(options)))
                : throw _notSupportedException;
        }


        private DefaultSensorsCollection Register(MonitoringSensorBase sensor)
        {
            _storage.Register(sensor.SensorPath, sensor);

            return this;
        }

        private BarSensorOptions GetProcessOptions(BarSensorOptions options) => options ?? _processOptions;

        private BarSensorOptions GetSystemMonitoringOptions(BarSensorOptions options) => options ?? _monitoringOptions;

        private WindowsSensorOptions GetWindowsOptions(WindowsSensorOptions options) => options ?? _windowsOptions;
    }
}
