namespace HSMDataCollector.DefaultSensors.SystemInfo
{
    internal interface IDiskInfo
    {
        long FreeSpaceMb { get; }

        /// <summary>Available free space in BYTES, on every platform (#1466).</summary>
        long FreeSpace { get; }

        string DiskLetter { get; }
    }
}