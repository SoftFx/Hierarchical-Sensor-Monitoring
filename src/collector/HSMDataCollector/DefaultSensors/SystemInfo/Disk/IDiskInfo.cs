namespace HSMDataCollector.DefaultSensors.SystemInfo
{
    internal interface IDiskInfo
    {
        string Name { get; }

        long FreeSpace { get; }

        long FreeSpaceMb { get; }
    }
}
