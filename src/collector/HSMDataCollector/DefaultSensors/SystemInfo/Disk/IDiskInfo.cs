namespace HSMDataCollector.DefaultSensors.SystemInfo
{
    internal interface IDiskInfo
    {
        long FreeSpaceMb { get; }

        long FreeSpace { get; }

        string DiskLetter { get; }
    }
}