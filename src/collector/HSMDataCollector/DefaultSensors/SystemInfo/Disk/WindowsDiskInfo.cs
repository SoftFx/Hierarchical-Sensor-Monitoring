using System.IO;

namespace HSMDataCollector.DefaultSensors.SystemInfo
{
    internal class WindowsDiskInfo : IDiskInfo
    {
        private readonly DriveInfo _driveInfo;


        public string Name { get; }

        public long FreeSpace => _driveInfo.AvailableFreeSpace;


        internal WindowsDiskInfo(string targetPath)
        {
            _driveInfo = new DriveInfo(targetPath);

            Name = $" {_driveInfo.Name[0]}";
        }
    }
}
