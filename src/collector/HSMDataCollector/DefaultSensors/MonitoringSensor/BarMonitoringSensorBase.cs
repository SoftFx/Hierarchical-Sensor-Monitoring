using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Threading;

namespace HSMDataCollector.DefaultSensors
{
    public abstract class BarMonitoringSensorBase<U, T> : MonitoringSensorBase<T> where T : BarSensorValueBase, new()
    {
        private readonly Timer _collectTimer;


        internal virtual TimeSpan CollectBarPeriod { get; } = TimeSpan.FromSeconds(5);


        public BarMonitoringSensorBase(string nodePath) : base(nodePath)
        {
            _collectTimer = new Timer(CollectBar, null, Timeout.Infinite, Timeout.Infinite);
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


        protected abstract U GetBarData();

        protected sealed override T GetValue()
        {
            var bar = new T();

            return bar;
        }


        private void CollectBar(object _)
        {
            var partialValue = GetBarData();

            //add partialValue to bar
        }
    }
}
