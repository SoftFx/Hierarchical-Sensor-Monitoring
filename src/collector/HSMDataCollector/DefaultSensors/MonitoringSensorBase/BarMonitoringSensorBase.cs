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
        private readonly TimeSpan _barPeriod;
        private readonly TimeSpan _collectBarPeriod;

        private BarType _internalBar;
        private Timer _collectTimer;


        protected sealed override TimeSpan TimerDueTime => _receiveDataPeriod.GetTimerDueTime();


        protected BarMonitoringSensorBase(BarSensorOptions options) : base(options)
        {
            _barPeriod = options.BarPeriod;
            _collectBarPeriod = options.CollectBarPeriod;

            BuildNewBar();
        }


        internal override async Task<bool> Init()
        {
            var isStarted = await base.Init();

            if (isStarted)
                _collectTimer = new Timer(CollectBar, null, _collectBarPeriod, _collectBarPeriod);

            return isStarted;
        }

        internal override async Task Stop()
        {
            _collectTimer?.Dispose();

            await base.Stop();

            OnTimerTick();
        }


        protected abstract T GetBarData();

        protected sealed override BarType GetValue() => _internalBar.Complete() as BarType;

        protected sealed override BarType GetDefaultValue()
        {
            return new BarType()
            {
                OpenTime = _internalBar?.OpenTime ?? DateTime.UtcNow,
                CloseTime = _internalBar?.CloseTime ?? DateTime.UtcNow,
                Count = 1,
            };
        }

        private void CollectBar(object _)
        {
            try
            {
                if (_internalBar.CloseTime < DateTime.UtcNow)
                {
                    OnTimerTick();
                    BuildNewBar();
                }

                _internalBar.AddValue(GetBarData());
            }
            catch { }
        }

        private void BuildNewBar()
        {
            _internalBar = new BarType();
            _internalBar.Init(_barPeriod);
        }
    }
}
