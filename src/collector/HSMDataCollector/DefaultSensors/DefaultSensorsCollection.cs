using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors.Unix;
using HSMDataCollector.DefaultSensors.Windows;
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

        private static readonly NotSupportedException _notSupportedException = new NotSupportedException(NotSupportedSensor);

        private readonly SensorsStorage _storage;


        internal bool IsUnixOS { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                                          RuntimeInformation.IsOSPlatform(OSPlatform.Linux);


        internal DefaultSensorsCollection(SensorsStorage storage)
        {
            _storage = storage;
        }


        public IEnumerator<MonitoringSensorBase> GetEnumerator() => _storage.Values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();


        internal bool IsSensorExists(string path) => _storage.ContainsKey(path);


        IWindowsCollection IWindowsCollection.AddProcessCpuSensor(string nodePath)
        {
            return !IsUnixOS ? Register(new WindowsProcessCpu(nodePath)) : throw _notSupportedException;
        }

        IWindowsCollection IWindowsCollection.AddProcessMemorySensor(string nodePath)
        {
            return !IsUnixOS ? Register(new WindowsProcessMemory(nodePath)) : throw _notSupportedException;
        }

        IWindowsCollection IWindowsCollection.AddProcessThreadCountSensor(string nodePath)
        {
            return !IsUnixOS ? Register(new WindowsProcessThreadCount(nodePath)) : throw _notSupportedException;
        }

        IWindowsCollection IWindowsCollection.AddTotalCpuSensor(string nodePath)
        {
            return !IsUnixOS ? Register(new WindowsTotalCpu(nodePath)) : throw _notSupportedException;
        }

        IWindowsCollection IWindowsCollection.AddFreeRamMemorySensor(string nodePath)
        {
            return !IsUnixOS ? Register(new WindowsFreeRamMemory(nodePath)) : throw _notSupportedException;
        }


        IUnixCollection IUnixCollection.AddProcessCpuSensor(string nodePath)
        {
            return IsUnixOS ? Register(new UnixProcessCpu(nodePath)) : throw _notSupportedException;
        }

        IUnixCollection IUnixCollection.AddProcessMemorySensor(string nodePath)
        {
            return IsUnixOS ? Register(new UnixProcessMemory(nodePath)) : throw _notSupportedException;
        }

        IUnixCollection IUnixCollection.AddProcessThreadCountSensor(string nodePath)
        {
            return IsUnixOS ? Register(new UnixProcessThreadCount(nodePath)) : throw _notSupportedException;
        }


        private DefaultSensorsCollection Register(MonitoringSensorBase sensor)
        {
            _storage.Register(sensor.SensorPath, sensor);

            return this;
        }
    }
}
