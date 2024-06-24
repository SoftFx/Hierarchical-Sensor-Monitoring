using System.IO;
using HSMDataCollector.Extensions;


namespace HSMDataCollector.DefaultSensors.SystemInfo
{
    internal class WindowsDiskInfo : IDiskInfo
    {
        private readonly DriveInfo _driveInfo;


        public long FreeSpace => _driveInfo.AvailableFreeSpace;

        public long FreeSpaceMb => FreeSpace.BytesToMegabytes();


        public string DiskLetter { get; }


        internal WindowsDiskInfo(string targetPath)
        {
            _driveInfo = new DriveInfo(targetPath);

            DiskLetter = $"{_driveInfo.Name[0]}";
        }
    }
}