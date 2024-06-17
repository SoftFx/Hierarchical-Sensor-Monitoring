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


        internal override Task<bool> StartAsync()
        {
            _tokenSource = new CancellationTokenSource();

            _lastSpeedCheckTime = DateTime.UtcNow;
            _lastAvailableSpace = FreeSpace;

            _currentChangeSpeed = 0.0;
            _requestsCount = 0;

            _ = UpdateDiskSpeed();

            return base.StartAsync();
        }

        internal override Task StopAsync()
        {
            _tokenSource?.Cancel();
            return base.StopAsync();
        }


        protected sealed override string GetComment()
        {
            if (IsCalibration)
                return $"Calibration request ({_requestsCount}/{_calibrationRequests})";

            var mbPerSec = _currentChangeSpeed.BytesToMegabytesDouble();

            return _isOffTime ? $"Free space increases by {-mbPerSec} Mbytes/sec. Value cannot be calculated." :
                                $"Free space decreases by {mbPerSec} Mbytes/sec.";
        }

        protected sealed override SensorStatus GetStatus() => IsCalibration || _isOffTime ? SensorStatus.OffTime : base.GetStatus();


        protected sealed override TimeSpan GetValue()
        {
            if (IsCalibration)
            {
                _requestsCount++;

                return TimeSpan.Zero;
            }

            var curSpace = FreeSpace;

            _isOffTime = _currentChangeSpeed < 0.0;

            if (_currentChangeSpeed > 0.0)
            {
                var newPrediction = TimeSpan.FromSeconds(curSpace / _currentChangeSpeed);

                _prevPrediction = newPrediction;

                return newPrediction;
            }

            return _prevPrediction;
        }

        private async Task UpdateDiskSpeed()
        {
            var start = DateTime.UtcNow.Ceil(_calculateSpeedDelay);

            await Task.Delay(start - DateTime.UtcNow, _tokenSource.Token);

            while (!_tokenSource.IsCancellationRequested)
            {
                var utc = DateTime.UtcNow;
                var curSpace = FreeSpace;

                var curSpeed = (_lastAvailableSpace - curSpace) / (utc - _lastSpeedCheckTime).TotalSeconds;

                //Console.WriteLine($"Free = {curSpace}, Prev {_currentChangeSpeed} - cur {curSpeed}");

                if (curSpeed > 0.0)
                    Interlocked.Exchange(ref _currentChangeSpeed, Math.Abs(_currentChangeSpeed) > 0.0 ? _currentChangeSpeed * 0.9 + curSpeed * 0.1 : curSpeed);

                _lastAvailableSpace = curSpace;
                _lastSpeedCheckTime = utc;

                await Task.Delay(_calculateSpeedDelay, _tokenSource.Token);
            }
        }
    }
}
