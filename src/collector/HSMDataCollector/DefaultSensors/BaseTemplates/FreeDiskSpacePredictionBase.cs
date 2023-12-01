using HSMDataCollector.DefaultSensors.SystemInfo;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HSMDataCollector.DefaultSensors
{
    internal abstract class FreeDiskSpacePredictionBase : MonitoringSensorBase<TimeSpan>
    {
        public const int DefaultSpaceCheckPeriodInSec = 30;

        private readonly TimeSpan _calculateSpeedDelay;
        private readonly IDiskInfo _diskInfo;
        private readonly int _calibrationRequests;

        private CancellationTokenSource _tokenSource;
        private DateTime _lastSpeedCheckTime;
        private TimeSpan _prevPrediction = TimeSpan.Zero;

        private double _currentChangeSpeed;
        private long _lastAvailableSpace;
        private long _requestsCount;
        private bool _isOffTime;


        private bool IsCalibration => _requestsCount <= _calibrationRequests;

        private long FreeSpace => _diskInfo.FreeSpace;


        public FreeDiskSpacePredictionBase(DiskSensorOptions options, IDiskInfo diskInfo) : base(options)
        {
            _calculateSpeedDelay = TimeSpan.FromSeconds(DefaultSpaceCheckPeriodInSec);
            _calibrationRequests = options.CalibrationRequests;
            _diskInfo = diskInfo;
        }


        internal override Task<bool> Start()
        {
            _tokenSource = new CancellationTokenSource();

            _lastSpeedCheckTime = DateTime.UtcNow;
            _lastAvailableSpace = FreeSpace;

            _currentChangeSpeed = 0.0;
            _requestsCount = 0;

            _ = UpdateDiskSpeed();

            return base.Start();
        }

        internal override Task Stop()
        {
            _tokenSource?.Cancel();
            return base.Stop();
        }


        protected sealed override string GetComment()
        {
            if (IsCalibration)
                return $"Calibration request ({_requestsCount}/{_calibrationRequests})";

            var mbPerSec = _currentChangeSpeed.BytesToMegabytes();

            return _isOffTime ? $"Free space increases by {-mbPerSec:F4} Mbytes/sec. Value cannot be calculated." :
                                $"Free space decreases by {mbPerSec:F4} Mbytes/sec.";
        }

        protected sealed override SensorStatus GetStatus() => IsCalibration || _isOffTime ? SensorStatus.OffTime : base.GetStatus();


        protected sealed override TimeSpan GetValue()
        {
            if (IsCalibration)
                _requestsCount++;

            var curSpace = FreeSpace;

            _isOffTime = _currentChangeSpeed < 0.0;

            if (_currentChangeSpeed > 0.0)
            {
                var newPreduction = TimeSpan.FromSeconds(curSpace / _currentChangeSpeed);

                _prevPrediction = newPreduction;

                return newPreduction;
            }

            return _prevPrediction;
        }

        private async Task UpdateDiskSpeed()
        {
            var start = DateTime.UtcNow.Ceil(_calculateSpeedDelay);

            await Task.Delay(start - DateTime.UtcNow, _tokenSource.Token);

            while (!_tokenSource.IsCancellationRequested)
            {
                var curSpace = FreeSpace;
                var utc = DateTime.UtcNow;

                var curSpeed = (_lastAvailableSpace - curSpace) / (utc - _lastSpeedCheckTime).TotalSeconds;

                Interlocked.Exchange(ref _currentChangeSpeed, _currentChangeSpeed * 0.9 + curSpeed * 0.1);

                _lastAvailableSpace = curSpace;
                _lastSpeedCheckTime = utc;

                await Task.Delay(_calculateSpeedDelay, _tokenSource.Token);
            }
        }
    }
}
