namespace HSMDataCollector.PublicInterface
{
    public interface IWindowsCollection
    {
        IWindowsCollection AddProcessCPUSensor(string nodePath);

        IWindowsCollection AddProcessMemorySensor(string nodePath);

        IWindowsCollection AddProcessThreadCountSensor(string nodePath);
    }
}
