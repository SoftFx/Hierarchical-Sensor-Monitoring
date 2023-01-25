using HSMDataCollector.Options;

namespace HSMDataCollector.PublicInterface
{
    public interface IUnixCollection
    {
        IUnixCollection AddCurrentProcessSensors(BarSensorOptions options = null);


        IUnixCollection AddProcessCpu(BarSensorOptions options = null);

        IUnixCollection AddProcessMemory(BarSensorOptions options = null);

        IUnixCollection AddProcessThreadCount(BarSensorOptions options = null);
    }
}
