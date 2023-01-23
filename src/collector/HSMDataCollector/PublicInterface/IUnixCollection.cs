namespace HSMDataCollector.PublicInterface
{
    public interface IUnixCollection
    {
        IUnixCollection AddProcessCpuSensor(string nodePath);

        IUnixCollection AddProcessMemorySensor(string nodePath);

        IUnixCollection AddProcessThreadCountSensor(string nodePath);
    }
}
