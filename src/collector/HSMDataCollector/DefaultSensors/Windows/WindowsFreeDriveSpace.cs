using HSMDataCollector.Options;
using System.IO;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsFreeDriveSpace : MonitoringSensorBase<double>
    {
        private const double MbDivisor = 1 << 20;

        private readonly string _driveName;
        private readonly DriveInfo _driveInfo;

        protected override string SensorName => $"Free drive {_driveName} space MB";


        public WindowsFreeDriveSpace(DriveSensorOptions options) : base(options)
        {
            _driveName = Path.GetPathRoot(options.DriveName);
            _driveInfo = new DriveInfo(_driveName);
        }


        protected override double GetValue() => _driveInfo.AvailableFreeSpace / MbDivisor;
    }
}
