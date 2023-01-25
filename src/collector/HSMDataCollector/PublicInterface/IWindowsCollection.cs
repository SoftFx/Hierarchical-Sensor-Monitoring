using HSMDataCollector.Options;

namespace HSMDataCollector.PublicInterface
{
    public interface IWindowsCollection
    {
        IWindowsCollection AddProcessSensors(BarSensorOptions options = null);

        IWindowsCollection AddSystemMonitoringSensors(BarSensorOptions options = null);

        IWindowsCollection AddWindowsSensors(WindowsSensorOptions options = null);


        IWindowsCollection AddProcessCpu(BarSensorOptions options = null);

        IWindowsCollection AddProcessMemory(BarSensorOptions options = null);

        IWindowsCollection AddProcessThreadCount(BarSensorOptions options = null);


        IWindowsCollection AddTotalCpu(BarSensorOptions options = null);

        IWindowsCollection AddFreeRamMemory(BarSensorOptions options = null);


        IWindowsCollection AddWindowsNeedUpdate(WindowsSensorOptions options = null);

        IWindowsCollection AddWindowsLastRestart(WindowsSensorOptions options = null);
    }
}
