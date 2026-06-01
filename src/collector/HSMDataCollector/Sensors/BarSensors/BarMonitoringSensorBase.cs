using System;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMDataCollector.Threading;
using HSMSensorDataObjects.SensorRequests;


namespace HSMDataCollector.DefaultSensors
{
    public abstract class BarMonitoringSensorBase<BarType, T> : MonitoringSensorBase<BarType, NoDisplayUnit>
        where BarType : MonitoringBarBase<T>, new()
        where T : struct
    {
        private readonly object _lockBar = new object();

        private readonly TimeSpan _collectBarPeriod;
        private readonly TimeSpan _barPeriod;
        private readonly int _precision;

        // Composed scheduling lifecycle for the bar-collect loop (the send loop is owned by the
        // MonitoringSensorBase base via its own handle). Replaces a hand-rolled ScheduledTask + lock.
        private readonly ScheduledTaskHandle _collectHandle;

        protected BarType _internalBar;

        public override BarType Current => (BarType)_internalBar.Copy().Complete();

        protected sealed override TimeSpan TimerDueTime => BarTimeHelper.GetTimerDueTime(PostTimePeriod);


        protected BarMonitoringSensorBase(BarSensorOptions options) : base(options)
        {
            if (options.BarTickPeriod <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options.BarTickPeriod), "Bar tick period must be greater than zero.");

            if (options.BarPeriod <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(options.BarPeriod), "Bar period must be greater than zero.");

            _collectBarPeriod = options.BarTickPeriod;
            _barPeriod = options.BarPeriod;
            _precision = options.Precision;

            _collectHandle = new ScheduledTaskHandle(_dataProcessor.Scheduler);

            BuildNewBar();
        }


        public override ValueTask<bool> InitAsync()
        {
            _collectHandle.Start(CollectBar, _collectBarPeriod, _collectBarPeriod, HandleException);

            return base.InitAsync();
        }

        public override async ValueTask StopAsync()
        {
            try
            {
                await _collectHandle.StopAsync(waitForCurrentRun: true).ConfigureAwait(false);

                await base.StopAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                HandleException(ex);
            }
        }


        protected virtual void CollectBar() => CheckCurrentBar();

        protected sealed override BarType GetValue()
        {
            lock (_lockBar)
            {
                if (_internalBar.Count <= 0)
                    return null;

                return _internalBar.Copy().Complete() as BarType; //need copy for correct partialBar serialization
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
                        SendValueAction();
                        BuildNewBar();
                    }
                }
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
        }


        protected virtual void BuildNewBar()
        {
            _internalBar = new BarType();
            _internalBar.Init(_barPeriod, _precision);
        }
    }
}
