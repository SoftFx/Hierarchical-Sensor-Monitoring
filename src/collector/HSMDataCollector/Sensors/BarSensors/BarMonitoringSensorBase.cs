using System;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMDataCollector.Threading;


namespace HSMDataCollector.DefaultSensors
{
    public abstract class BarMonitoringSensorBase<BarType, T> : MonitoringSensorBase<BarType>
        where BarType : MonitoringBarBase<T>, new()
        where T : struct
    {
        private readonly object _lockBar = new object();

        private readonly TimeSpan _collectBarPeriod;
        private readonly TimeSpan _barPeriod;
        private readonly int _precision;

        private Task _collectTask;
        private CancellationTokenSource _cancellationTokenSource;
        protected BarType _internalBar;

        private volatile bool _isStarted = false;

        protected sealed override TimeSpan TimerDueTime => BarTimeHelper.GetTimerDueTime(PostTimePeriod);


        protected BarMonitoringSensorBase(BarSensorOptions options) : base(options)
        {
            _collectBarPeriod = options.BarTickPeriod;
            _barPeriod = options.BarPeriod;
            _precision = options.Precision;

            BuildNewBar();
        }


        internal override ValueTask<bool> InitAsync()
        {

            if (!_isStarted)
            {
                _isStarted = true;
                _cancellationTokenSource = new CancellationTokenSource();
                _collectTask = PeriodicTask.Run(CollectBar, _collectBarPeriod, _collectBarPeriod, _cancellationTokenSource.Token);
            }

            return base.InitAsync();
        }

        internal override ValueTask StopAsync()
        {
            if (_isStarted)
            {
                _isStarted = false;
                _cancellationTokenSource?.Cancel();
                _collectTask?.ConfigureAwait(false).GetAwaiter().GetResult();
                _cancellationTokenSource?.Dispose();
                _collectTask?.Dispose();
            }

            return base.StopAsync();
        }


        protected virtual void CollectBar() => CheckCurrentBar();

        protected sealed override BarType GetValue()
        {
            lock (_lockBar)
            {
                _needSendValue = _internalBar.Count > 0;

                return _internalBar.Complete().Copy() as BarType; //need copy for correct partialBar serialization
            }
        }

        protected sealed override BarType GetDefaultValue() =>
            new BarType()
            {
                OpenTime  = _internalBar?.OpenTime ?? DateTime.UtcNow,
                CloseTime = _internalBar?.CloseTime ?? DateTime.UtcNow,
                Count = 1,
            };

        protected void CheckCurrentBar()
        {
            try
            {
                lock (_lockBar)
                {
                    if (_internalBar.CloseTime < DateTime.UtcNow)
                    {
                        OnTimerTick();
                        BuildNewBar();
                    }
                }
            }
            catch (Exception ex)
            {
                ThrowException(ex);
            }
        }


        protected virtual void BuildNewBar()
        {
            _internalBar = new BarType();
            _internalBar.Init(_barPeriod, _precision);
        }
    }
}