using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

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

        private Timer _collectTimer;
        protected BarType _internalBar;

        protected sealed override TimeSpan TimerDueTime => _receiveDataPeriod.GetTimerDueTime();


        protected BarMonitoringSensorBase(BarSensorOptions options) : base(options)
        {
            _collectBarPeriod = options.BarTickPeriod;
            _barPeriod = options.BarPeriod;
            _precision = options.Precision;

            BuildNewBar();
        }


        internal override async Task<bool> Init()
        {
            var isInitialized = await base.Init();

            if (isInitialized)
                _collectTimer = new Timer(CollectBar, null, _collectBarPeriod, _collectBarPeriod);

            return isInitialized;
        }

        internal override async Task Stop()
        {
            _collectTimer?.Dispose();

            await base.Stop();

            OnTimerTick();
        }


        protected virtual void CollectBar(object _) => CheckCurrentBar();

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
                OpenTime = _internalBar?.OpenTime ?? DateTime.UtcNow,
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


        private void BuildNewBar()
        {
            _internalBar = new BarType();
            _internalBar.Init(_barPeriod, _precision);
        }
    }
}