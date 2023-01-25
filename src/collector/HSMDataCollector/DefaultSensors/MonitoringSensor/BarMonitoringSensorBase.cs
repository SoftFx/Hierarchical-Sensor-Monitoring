using HSMDataCollector.DefaultSensors.MonitoringSensor;
using HSMDataCollector.Options;
using System;
using System.Threading;

namespace HSMDataCollector.DefaultSensors
{
    public abstract class BarMonitoringSensorBase<BarType, T> : MonitoringSensorBase<BarType>
        where BarType : MonitoringBar<T>, new()
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
            Start();
        }


        internal override void Start()
        {
            _collectTimer.Change(CollectBarPeriod, CollectBarPeriod);

            base.Start();
        }

        internal override void Stop()
        {
            _collectTimer?.Dispose();

            base.Stop();
        }


        protected abstract T GetBarData();

        protected sealed override BarType GetValue() => _internalBar.Complete() as BarType;

        protected override void OnTimerTick(object _)
        {
            base.OnTimerTick();

            BuildNewBar();
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
