using HSMDataCollector.Options;
using System.IO;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsFreeDriveSpace : MonitoringSensorBase<double>
    {
        private const double MbDivisor = 1 << 20;

        private readonly char _driveName;
        private readonly DriveInfo _driveInfo;

        protected override string SensorName => $"Free space on disk {_driveName} MB";


        public WindowsFreeDriveSpace(DriveSensorOptions options) : base(options)
        {
            _driveInfo = new DriveInfo(options.DriveName);
            _driveName = _driveInfo.Name[0];
        }


        protected override double GetValue() => _driveInfo.AvailableFreeSpace / MbDivisor;
    }
}
