using HSMDataCollector.Core;
using HSMDataCollector.DefaultSensors.Unix;
using HSMDataCollector.DefaultSensors.Windows;
using HSMDataCollector.PublicInterface;
using System;
using System.Runtime.InteropServices;

namespace HSMDataCollector.DefaultSensors
{
    internal sealed class DefaultSensorsCollection : IWindowsCollection, IUnixCollection
    {
        private const string NotSupportedSensor = "Sensor is not supported for current OS";

        private readonly SensorsStorage _storage;


        internal bool IsUnixOS { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                                          RuntimeInformation.IsOSPlatform(OSPlatform.Linux);


        internal DefaultSensorsCollection(SensorsStorage storage)
        {
            _storage = storage;
        }


        internal bool IsSensorExists(string path) => _storage.ContainsKey(path);


        IWindowsCollection IWindowsCollection.AddProcessCPUSensor(string nodePath)
        {
            if (IsUnixOS)
                throw new NotSupportedException(NotSupportedSensor);

            return Register(new WindowsProcessCPU(nodePath));
        }

        IWindowsCollection IWindowsCollection.AddProcessMemorySensor(string nodePath)
        {
            if (IsUnixOS)
                throw new NotSupportedException(NotSupportedSensor);

            return Register(new WindowsProcessMemory(nodePath));
        }

        IWindowsCollection IWindowsCollection.AddProcessThreadCountSensor(string nodePath)
        {
            if (IsUnixOS)
                throw new NotSupportedException(NotSupportedSensor);

            return Register(new WindowsProcessThreadCount(nodePath));
        }

        IWindowsCollection IWindowsCollection.AddTotalCpuSensor(string nodePath)
        {
            if (IsUnixOS)
                throw new NotSupportedException(NotSupportedSensor);

            return Register(new WindowsTotalCpu(nodePath));
        }

        IWindowsCollection IWindowsCollection.AddFreeRamMemorySensor(string nodePath)
        {
            if (IsUnixOS)
                throw new NotSupportedException(NotSupportedSensor);

            return Register(new WindowsFreeRamMemory(nodePath));
        }


        IUnixCollection IUnixCollection.AddProcessCPUSensor(string nodePath)
        {
            if (!IsUnixOS)
                throw new NotSupportedException(NotSupportedSensor);

            return Register(new UnixProcessCPU(nodePath));
        }

        IUnixCollection IUnixCollection.AddProcessMemorySensor(string nodePath)
        {
            if (!IsUnixOS)
                throw new NotSupportedException(NotSupportedSensor);

            return Register(new UnixProcessMemory(nodePath));
        }

        IUnixCollection IUnixCollection.AddProcessThreadCountSensor(string nodePath)
        {
            if (!IsUnixOS)
                throw new NotSupportedException(NotSupportedSensor);

            return Register(new UnixProcessThreadCount(nodePath));
        }

        IUnixCollection IUnixCollection.AddFreeRamMemorySensor(string nodePath)
        {
            if (!IsUnixOS)
                throw new NotSupportedException(NotSupportedSensor);

            return Register(new UnixFreeRamMemory(nodePath));
        }


        private DefaultSensorsCollection Register(MonitoringSensorBase sensor)
        {
            _storage.Register(sensor.SensorPath, sensor);

            return this;
        }
    }
}
