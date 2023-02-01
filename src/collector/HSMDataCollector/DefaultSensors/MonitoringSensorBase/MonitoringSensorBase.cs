using HSMDataCollector.Options;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HSMDataCollector.DefaultSensors
{
    public abstract class MonitoringSensorBase : IDisposable
    {
        private readonly Timer _sendTimer;
        private readonly string _nodePath;

        protected bool IsStarted { get; private set; }


        protected abstract string SensorName { get; }

        protected TimeSpan ReceiveDataPeriod { get; }


        internal string SensorPath => $"{_nodePath}/{SensorName}";


        internal event Action<SensorValueBase> ReceiveSensorValue;


        protected MonitoringSensorBase(SensorOptions options)
        {
            _nodePath = options.NodePath;
            ReceiveDataPeriod = options.PostDataPeriod;

            _sendTimer = new Timer(OnTimerTick, null, Timeout.Infinite, Timeout.Infinite);
        }


        public void Dispose()
        {
            Stop();
        }

        internal virtual Task<bool> Start()
        {
            if (IsStarted)
                return Task.FromResult(false);

            _sendTimer.Change(GetTimerDueTime(), ReceiveDataPeriod);

            IsStarted = true;

            return Task.FromResult(IsStarted);
        }

        internal virtual void Stop()
        {
            _sendTimer?.Dispose();

            IsStarted = false;
        }


        protected abstract void OnTimerTick(object _ = null);

        protected virtual TimeSpan GetTimerDueTime() => ReceiveDataPeriod;

        protected virtual string GetComment() => null;

        protected void SendCollectedValue(SensorValueBase value) => ReceiveSensorValue?.Invoke(value);
    }
}