using System;

namespace HSMDataCollector.PublicInterface
{
    public interface IWindowsCollection
    {
        IWindowsCollection AddProcessCpuSensor(string nodePath = null);

        IWindowsCollection AddProcessMemorySensor(string nodePath = null);

        IWindowsCollection AddProcessThreadCountSensor(string nodePath = null);


        IWindowsCollection AddTotalCpuSensor(string nodePath = null);

        IWindowsCollection AddFreeRamMemorySensor(string nodePath = null);


        IWindowsCollection AddWindowsNeedUpdateSensor(string nodePath = null, TimeSpan? recevedDataPeriod = null, TimeSpan? updateInterval = null);

        IWindowsCollection AddWindowsSystemLastRestartSensor(string nodePath = null);
    }
}
