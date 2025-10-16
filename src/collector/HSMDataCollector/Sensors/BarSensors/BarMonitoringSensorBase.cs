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

        private readonly object _locker = new object();

        private Task _collectTask;
        private CancellationTokenSource _cancellationTokenSource;
        protected BarType _internalBar;

        public override BarType Current => (BarType)_internalBar.Copy().Complete();

        protected sealed override TimeSpan TimerDueTime => BarTimeHelper.GetTimerDueTime(PostTimePeriod);


        protected BarMonitoringSensorBase(BarSensorOptions options) : base(options)
        {
            _collectBarPeriod = options.BarTickPeriod;
            _barPeriod = options.BarPeriod;
            _precision = options.Precision;

            BuildNewBar();
        }


        public override ValueTask<bool> InitAsync()
        {
            lock (_locker)
            {
                if (_collectTask == null)
                {
                    _cancellationTokenSource = new CancellationTokenSource();
                    _collectTask = PeriodicTask.Run(CollectBar, _collectBarPeriod, _collectBarPeriod, _cancellationTokenSource.Token);
                }
            }

            return base.InitAsync();
        }

        public override async ValueTask StopAsync()
        {
            Task taskToWait = null;
            CancellationTokenSource cts = null;

            lock (_locker)
            {
                if (_collectTask != null)
                {
                    cts = _cancellationTokenSource;
                    _cancellationTokenSource = null;

                    taskToWait = _collectTask;
                    _collectTask = null;

                    cts?.Cancel();
                }
            }

            try
            {
                if (taskToWait != null)
                {
                    try
                    {
                        await taskToWait.ConfigureAwait(false);
                    }
                    finally
                    {
                        taskToWait.Dispose();
                    }
                }

                await base.StopAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                HandleException(ex);
            }
            finally
            {
                cts?.Dispose();
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