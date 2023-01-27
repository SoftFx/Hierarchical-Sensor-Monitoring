using HSMDataCollector.Options;
using System.IO;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsFreeDiskSpace : MonitoringSensorBase<double>
    {
        private readonly DriveInfo _driveInfo;
        private readonly char _driveName;


        protected override string SensorName => $"Free space on disk {_driveName} MB";


        public WindowsFreeDiskSpace(DiskSensorOptions options) : base(options)
        {
            _driveInfo = new DriveInfo(options.TargetPath);
            _driveName = _driveInfo.Name[0];
        }


        protected override double GetValue() => _driveInfo.AvailableFreeSpace / MbDivisor;
    }
}
