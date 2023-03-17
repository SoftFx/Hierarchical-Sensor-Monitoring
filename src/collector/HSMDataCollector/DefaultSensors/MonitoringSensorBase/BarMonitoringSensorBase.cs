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
        private readonly Timer _collectTimer;
        private readonly TimeSpan _barPeriod;
        private readonly TimeSpan _collectBarPeriod;

        private BarType _internalBar;

        protected sealed override TimeSpan TimerDueTime => _receiveDataPeriod.GetTimerDueTime();


        protected BarMonitoringSensorBase(BarSensorOptions options) : base(options)
        {
            _barPeriod = options.BarPeriod;
            _collectBarPeriod = options.CollectBarPeriod;

            _collectTimer = new Timer(CollectBar, null, Timeout.Infinite, Timeout.Infinite);

            BuildNewBar();
        }


        internal override async Task<bool> Start()
        {
            var isStarted = await base.Start();

            if (isStarted)
                _collectTimer.Change(_collectBarPeriod, _collectBarPeriod);

            return isStarted;
        }

        internal override Task Stop()
        {
            _collectTimer?.Dispose();

            base.Stop();
            
            return Task.CompletedTask;
        }


        protected abstract T GetBarData();

        protected sealed override BarType GetValue() => _internalBar.Complete() as BarType;


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
