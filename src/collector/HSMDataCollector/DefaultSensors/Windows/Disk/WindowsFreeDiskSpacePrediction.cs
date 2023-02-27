using HSMDataCollector.Options;
using HSMSensorDataObjects;
using System;
using System.IO;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsFreeDiskSpacePrediction : MonitoringSensorBase<TimeSpan>
    {
        private readonly DriveInfo _driveInfo;
        private readonly char _driveName;
        private readonly int _calibrationRequests;

        private DateTime _startTime;
        private long _startAvailableSpace;
        private int _requestsCount;


        private bool IsCalibration => _requestsCount <= _calibrationRequests;

        protected override string SensorName => $"Free space on disk {_driveName} prediction";


        public WindowsFreeDiskSpacePrediction(DiskSensorOptions options) : base(options)
        {
            _driveInfo = new DriveInfo(options.TargetPath);
            _driveName = _driveInfo.Name[0];
            _calibrationRequests = options.CalibrationRequests;

            InitStartingPoint();
        }


        protected override string GetComment() => IsCalibration
            ? $"Calibration request ({_requestsCount}/{_calibrationRequests})"
            : base.GetComment();

        protected override SensorStatus GetStatus() => IsCalibration ? SensorStatus.OffTime : base.GetStatus();


        protected override TimeSpan GetValue()
        {
            if (IsCalibration)
                _requestsCount++;

            var currentAvailableSpace = _driveInfo.AvailableFreeSpace;
            var deltaSpace = _startAvailableSpace - currentAvailableSpace;
            var deltaTime = DateTime.UtcNow - _startTime;

            NeedSendValue = deltaSpace > 0;

            if (NeedSendValue)
                return new TimeSpan(currentAvailableSpace / deltaSpace * deltaTime.Ticks);

            if (deltaSpace < 0)
                InitStartingPoint(currentAvailableSpace);

            return TimeSpan.Zero;
        }

        private void InitStartingPoint(long? availableSpace = null)
        {
            _startAvailableSpace = availableSpace ?? _driveInfo.AvailableFreeSpace;
            _startTime = DateTime.UtcNow;
        }
    }
}
