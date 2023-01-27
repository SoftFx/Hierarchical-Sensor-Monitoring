using HSMDataCollector.Options;
using System;
using System.IO;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsFreeDiskSpacePredictor : MonitoringSensorBase<TimeSpan>
    {
        private readonly DriveInfo _driveInfo;
        private readonly char _driveName;

        private DateTime _startTime;
        private long _startAvailableSpace;


        protected override string SensorName => $"Free space on disk {_driveName} predictor";


        public WindowsFreeDiskSpacePredictor(DiskSensorOptions options) : base(options)
        {
            _driveInfo = new DriveInfo(options.TargetPath);
            _driveName = _driveInfo.Name[0];

            InitStartingPoint();
        }


        protected override TimeSpan GetValue()
        {
            var currentAvailableSpace = _driveInfo.AvailableFreeSpace;
            var deltaSpace = _startAvailableSpace - currentAvailableSpace;
            var deltaTime = DateTime.UtcNow - _startTime;

            if (deltaSpace < 0)
            {
                InitStartingPoint(currentAvailableSpace);

                CanSendValue = false;
                return TimeSpan.Zero;
            }
            else
            {
                CanSendValue = true;
                return new TimeSpan(currentAvailableSpace / deltaSpace * deltaTime.Ticks);
            }
        }

        private void InitStartingPoint(long? availableSpace = null)
        {
            _startAvailableSpace = availableSpace ?? _driveInfo.AvailableFreeSpace;
            _startTime = DateTime.UtcNow;
        }
    }
}
