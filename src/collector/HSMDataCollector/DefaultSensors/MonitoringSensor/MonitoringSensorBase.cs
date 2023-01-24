using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Threading;

namespace HSMDataCollector.DefaultSensors
{
    public abstract class MonitoringSensorBase : IDisposable
    {
        private readonly string _nodePath;
        private readonly Timer _sendTimer;


        protected abstract string SensorName { get; }

        protected virtual TimeSpan ReceiveDataPeriod { get; set; } = TimeSpan.FromMinutes(5);


        internal string SensorPath => $"{_nodePath}/{SensorName}";


        internal event Action<SensorValueBase> ReceiveSensorValue;


        protected MonitoringSensorBase(string nodePath)
        {
            _nodePath = nodePath;
            _sendTimer = new Timer(OnTimerTick, null, Timeout.Infinite, Timeout.Infinite);
        }


        public void Dispose()
        {
            Stop();
        }

        internal abstract SensorValueBase GetLastValue();

        internal virtual void Start()
        {
            _sendTimer.Change(ReceiveDataPeriod, ReceiveDataPeriod);
        }

        internal virtual void Stop()
        {
            _sendTimer?.Dispose();
        }


        protected abstract void OnTimerTick(object _ = null);

        protected void SendCollectedValue(SensorValueBase value) => ReceiveSensorValue?.Invoke(value);
    }
}