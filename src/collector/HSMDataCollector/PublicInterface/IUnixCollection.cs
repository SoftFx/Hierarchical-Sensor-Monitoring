using HSMDataCollector.Options;

namespace HSMDataCollector.PublicInterface
{
    public interface IUnixCollection
    {
        IUnixCollection AddProcessCpu(BarSensorOptions options = null);

        IUnixCollection AddProcessMemory(BarSensorOptions options = null);

        IUnixCollection AddProcessThreadCount(BarSensorOptions options = null);

        IUnixCollection AddProcessSensors(BarSensorOptions options = null);


        IUnixCollection AddCollectorAlive(SensorOptions options = null);
    }
}
