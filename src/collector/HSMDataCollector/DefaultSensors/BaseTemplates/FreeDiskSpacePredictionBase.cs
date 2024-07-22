using System;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.DefaultSensors.SystemInfo;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMDataCollector.Threading;
using HSMSensorDataObjects;


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

        private Task _workTask;

        private bool IsCalibration => _requestsCount <= _calibrationRequests;

        private long FreeSpace => _diskInfo.FreeSpace;

        public FreeDiskSpacePredictionBase(DiskSensorOptions options, IDiskInfo diskInfo) : base(options)
        {
            _calculateSpeedDelay = TimeSpan.FromSeconds(DefaultSpaceCheckPeriodInSec);
            _calibrationRequests = options.CalibrationRequests;
            _diskInfo = diskInfo;
        }


        internal override ValueTask<bool> StartAsync()
        {
            if (_workTask == null)
            {
                _tokenSource = new CancellationTokenSource();

                _lastSpeedCheckTime = DateTime.UtcNow;
                _lastAvailableSpace = FreeSpace;

                _currentChangeSpeed = 0.0;
                _requestsCount = 0;

                _workTask = PeriodicTask.Run(UpdateDiskSpeed, DateTime.UtcNow.Ceil(_calculateSpeedDelay) - DateTime.UtcNow, _calculateSpeedDelay, _tokenSource.Token);
            }

            return base.StartAsync();
        }

        internal override async ValueTask StopAsync()
        {
            try
            {
                if (_workTask != null)
                {
                    _tokenSource?.Cancel();
                    await _workTask.ConfigureAwait(false);
                    _tokenSource?.Dispose();
                    _workTask?.Dispose();
                    _workTask = null;
                }
                await base.StopAsync();
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
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

        private void UpdateDiskSpeed()
        {
            var utc = DateTime.UtcNow;
            var curSpace = FreeSpace;

            var curSpeed = (_lastAvailableSpace - curSpace) / (utc - _lastSpeedCheckTime).TotalSeconds;

            //Console.WriteLine($"Free = {curSpace}, Prev {_currentChangeSpeed} - cur {curSpeed}");

            if (curSpeed > 0.0)
                Interlocked.Exchange(ref _currentChangeSpeed, Math.Abs(_currentChangeSpeed) > 0.0 ? _currentChangeSpeed * 0.9 + curSpeed * 0.1 : curSpeed);

            _lastAvailableSpace = curSpace;
            _lastSpeedCheckTime = utc;
        }
    }
}
