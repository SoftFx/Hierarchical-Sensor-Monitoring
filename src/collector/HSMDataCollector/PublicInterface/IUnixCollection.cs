namespace HSMDataCollector.PublicInterface
{
    public interface IUnixCollection
    {
        IUnixCollection AddProcessCPUSensor(string nodePath);

        IUnixCollection AddProcessMemorySensor(string nodePath);

        IUnixCollection AddProcessThreadCountSensor(string nodePath);


        IUnixCollection AddFreeRamMemorySensor(string nodePath);
    }
}
