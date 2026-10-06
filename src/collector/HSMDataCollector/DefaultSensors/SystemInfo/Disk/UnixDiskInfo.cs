using System;
using System.IO;
using HSMDataCollector.Extensions;

namespace HSMDataCollector.DefaultSensors.SystemInfo
{
    internal sealed class UnixDiskInfo : IDiskInfo
    {
        private const string RootMount = "/";

        private readonly Func<long> _availableBytes;


        internal UnixDiskInfo() : this(() => new DriveInfo(RootMount).AvailableFreeSpace) { }

        // Test seam: the read of the root filesystem's available bytes.
        internal UnixDiskInfo(Func<long> availableBytes)
        {
            _availableBytes = availableBytes;
        }


        // Available space on the root filesystem in BYTES, the unit WindowsDiskInfo reports (#1466):
        // the prediction sensor's comment divides by 1 MiB, so the kB figure this used to return made a
        // Unix comment print GiB as "Mbytes". Uses the managed DriveInfo (statvfs under the hood)
        // instead of shelling out to `df` — no external process, no locale-dependent text parsing.
        public long FreeSpace => _availableBytes();

        // Whole megabytes: the same number the old kB path floored to, (bytes / 1024) / 1024.
        public long FreeSpaceMb => FreeSpace.BytesToMegabytes();


        public string DiskLetter { get; }
    }
}
