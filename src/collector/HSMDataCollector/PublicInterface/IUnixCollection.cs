namespace HSMDataCollector.PublicInterface
{
    public interface IUnixCollection
    {
        IUnixCollection AddProcessCpuSensor(string nodePath = null);

        IUnixCollection AddProcessMemorySensor(string nodePath = null);

        IUnixCollection AddProcessThreadCountSensor(string nodePath = null);
    }
}
