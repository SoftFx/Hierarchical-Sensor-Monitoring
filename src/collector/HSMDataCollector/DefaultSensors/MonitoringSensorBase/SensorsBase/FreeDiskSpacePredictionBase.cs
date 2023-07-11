using HSMDataCollector.DefaultSensors.SystemInfo;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using System;

namespace HSMDataCollector.DefaultSensors
{
    internal abstract class FreeDiskSpacePredictionBase : MonitoringSensorBase<TimeSpan>
    {
        private readonly IDiskInfo _diskInfo;
        private readonly int _calibrationRequests;

        private DateTime _startTime;
        private long _startAvailableSpace;
        private int _requestsCount;


        private bool IsCalibration => _requestsCount <= _calibrationRequests;

        protected override string SensorName => $"Free space on disk{_diskInfo.Name} prediction";


        public FreeDiskSpacePredictionBase(DiskSensorOptions options, IDiskInfo diskInfo) : base(options)
        {
            _diskInfo = diskInfo;
            _calibrationRequests = options.CalibrationRequests;

            InitStartingPoint();
        }


        protected sealed override string GetComment() => IsCalibration
            ? $"Calibration request ({_requestsCount}/{_calibrationRequests})"
            : base.GetComment();

        protected sealed override SensorStatus GetStatus() => IsCalibration ? SensorStatus.OffTime : base.GetStatus();


        protected sealed override TimeSpan GetValue()
        {
            if (IsCalibration)
                _requestsCount++;

            var currentAvailableSpace = _diskInfo.FreeSpace;
            var deltaSpace = _startAvailableSpace - currentAvailableSpace;
            var deltaTime = DateTime.UtcNow - _startTime;

            _needSendValue = deltaSpace > 0;

            if (_needSendValue)
                return new TimeSpan(currentAvailableSpace / deltaSpace * deltaTime.Ticks);

            if (deltaSpace < 0)
                InitStartingPoint(currentAvailableSpace);

            return TimeSpan.Zero;
        }

        private void InitStartingPoint(long? availableSpace = null)
        {
            _startAvailableSpace = availableSpace ?? _diskInfo.FreeSpace;
            _startTime = DateTime.UtcNow;
        }
    }
}
