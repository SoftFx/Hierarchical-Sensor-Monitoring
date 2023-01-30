using HSMDataCollector.Options;
using System;
using System.Threading;

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


        internal override void Start()
        {
            if (IsMonitoringStarted)
                return;

            _collectTimer.Change(CollectBarPeriod, CollectBarPeriod);

            base.Start();
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
                var partialValue = GetBarData();

                _internalBar.AddValue(partialValue);
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
