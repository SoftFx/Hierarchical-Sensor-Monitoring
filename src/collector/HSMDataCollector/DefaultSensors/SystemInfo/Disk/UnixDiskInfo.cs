using HSMDataCollector.Extensions;

namespace HSMDataCollector.DefaultSensors.SystemInfo
{
    internal sealed class UnixDiskInfo : IDiskInfo
    {
        private const string AvailableSpaceCommand = @"df --output=avail / | sed 1d"; // get available space without header in /


        public long FreeSpace => long.TryParse(AvailableSpaceCommand.BashExecute(), out var availableSpace) ? availableSpace : 0L;

        public long FreeSpaceMb => FreeSpace.KilobytesToMegabytes();


        public string DiskLetter { get; }
    }
}