namespace HSMDataCollector.PublicInterface
{
    public interface IWindowsCollection
    {
        IWindowsCollection AddProcessCpuSensor(string nodePath);

        IWindowsCollection AddProcessMemorySensor(string nodePath);

        IWindowsCollection AddProcessThreadCountSensor(string nodePath);


        IWindowsCollection AddTotalCpuSensor(string nodePath);

        IWindowsCollection AddFreeRamMemorySensor(string nodePath);
    }
}
