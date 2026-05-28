using System;
using System.IO;
using HSMDataCollector.Extensions;

namespace HSMDataCollector.DefaultSensors.SystemInfo
{
    internal sealed class UnixDiskInfo : IDiskInfo
    {
        private const string RootMount = "/";


        // Available space on the root filesystem in kB. Uses the managed DriveInfo (statvfs under the
        // hood) instead of shelling out to `df` — no external process, no locale-dependent text parsing.
        public long FreeSpace
        {
            get
            {
                try
                {
                    return new DriveInfo(RootMount).AvailableFreeSpace / 1024L;
                }
                catch
                {
                    return 0L;
                }
            }
        }

        public long FreeSpaceMb => FreeSpace.KilobytesToMegabytes();


        public string DiskLetter { get; }
    }
}
