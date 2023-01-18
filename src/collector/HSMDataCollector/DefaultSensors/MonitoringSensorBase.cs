using HSMDataCollector.SensorsFactory;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Threading;

namespace HSMDataCollector.DefaultSensors
{
    public abstract class MonitoringSensorBase
    {
        private readonly string _nodePath;

        private Timer _sendTimer;


        internal abstract string SensorName { get; }

        internal abstract TimeSpan ReceiveDataPeriod { get; }


        internal string SensorPath => $"{_nodePath}/{SensorName}";


        internal event Action<SensorValueBase> ReceiveSensorValue;


        protected MonitoringSensorBase(string nodePath)
        {
            _nodePath = nodePath;

            Start();
        }


        public virtual void Start()
        {
            Stop();

            _sendTimer = new Timer(OnTimerTick, null, ReceiveDataPeriod, ReceiveDataPeriod);
        }

        public virtual void Stop()
        {
            _sendTimer?.Dispose();
            _sendTimer = null;
        }


        protected abstract void OnTimerTick(object _);

        protected void SendCollectedValue(SensorValueBase value) => ReceiveSensorValue?.Invoke(value);
    }

    public abstract class MonitoringSensorBase<T> : MonitoringSensorBase
    {
        protected MonitoringSensorBase(string nodePath) : base(nodePath) { }


        protected abstract T GetValue();

        protected sealed override void OnTimerTick(object _)
        {
            try
            {
                var value = SensorValuesFactory.BuildValue(GetValue());

                SendCollectedValue(value);
            }
            catch
            {

            }
        }
    }

    public abstract class BarMonitoringSensorBase<U, T> : MonitoringSensorBase<T> where T : BarSensorValueBase, new()
    {
        private readonly Timer _collectTimer;


        internal abstract TimeSpan CollectBarPeriod { get; }


        public BarMonitoringSensorBase(string nodePath) : base(nodePath)
        {
            _collectTimer = new Timer(CollectBar, null, CollectBarPeriod, CollectBarPeriod);
        }


        public override void Start()
        {
            base.Start();
        }

        public override void Stop()
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