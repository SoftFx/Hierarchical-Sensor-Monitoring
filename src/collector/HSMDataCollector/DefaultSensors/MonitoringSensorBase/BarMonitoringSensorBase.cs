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
        private readonly TimeSpan _collectBarPeriod;
        private readonly TimeSpan _barPeriod;

        private Timer _collectTimer;
        protected BarType _internalBar;

        protected sealed override TimeSpan TimerDueTime => _receiveDataPeriod.GetTimerDueTime();


        protected BarMonitoringSensorBase(BarSensorOptions options) : base(options)
        {
            _barPeriod = options.BarPeriod;
            _collectBarPeriod = options.CollectBarPeriod;

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


        protected virtual void CollectBar(object _)
        {
            try
            {
                if (_internalBar.CloseTime < DateTime.UtcNow)
                {
                    OnTimerTick();
                    BuildNewBar();
                }
            }
            catch (Exception ex)
            {
                ThrowException(ex);
            }
        }

        protected sealed override BarType GetValue() => _internalBar.Complete() as BarType;

        protected sealed override BarType GetDefaultValue() =>
            new BarType()
            {
                OpenTime = _internalBar?.OpenTime ?? DateTime.UtcNow,
                CloseTime = _internalBar?.CloseTime ?? DateTime.UtcNow,
                Count = 1,
            };


        private void BuildNewBar()
        {
            _internalBar = new BarType();
            _internalBar.Init(_barPeriod);
        }
    }
}
