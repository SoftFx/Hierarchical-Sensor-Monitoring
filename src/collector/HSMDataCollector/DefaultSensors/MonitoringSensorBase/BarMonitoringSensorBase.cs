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

        private BarType _internalBar;


        private TimeSpan CollectBarPeriod { get; }


        public BarMonitoringSensorBase(BarSensorOptions options) : base(options)
        {
            CollectBarPeriod = options.CollectBarPeriod;

            _collectTimer = new Timer(CollectBar, null, Timeout.Infinite, Timeout.Infinite);

            BuildNewBar();
        }


        internal override async Task<bool> Start()
        {
            var isStarted = await base.Start();

            if (isStarted)
                _collectTimer.Change(CollectBarPeriod, CollectBarPeriod);

            return isStarted;
        }

        internal override void Stop()
        {
            _collectTimer?.Dispose();

            base.Stop();
        }


        protected abstract T GetBarData();

        protected sealed override BarType GetValue()
        {
            var value = _internalBar.Complete() as BarType;

            BuildNewBar();

            return value;
        }


        private void CollectBar(object _)
        {
            try
            {
                _internalBar.AddValue(GetBarData());
            }
            catch { }
        }

        private void BuildNewBar()
        {
            _internalBar = new BarType();
            _internalBar.Init(ReceiveDataPeriod);
        }
    }
}
