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


        private DefaultSensorsCollection Register(MonitoringSensorBase sensor)
        {
            _storage.Register(sensor.SensorPath, sensor);

            return this;
        }
    }
}
