using HSMDataCollector.Options;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HSMDataCollector.DefaultSensors
{
    public abstract class MonitoringSensorBase : IDisposable
    {
        protected const int MbDivisor = 1 << 20;

        private readonly Timer _sendTimer;
        private readonly string _nodePath;

        protected bool IsMonitoringStarted { get; private set; }


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
            if (IsMonitoringStarted)
                return Task.FromResult(false);

            _sendTimer.Change(ReceiveDataPeriod, ReceiveDataPeriod);

            IsMonitoringStarted = true;

            return Task.FromResult(IsMonitoringStarted);
        }

        internal virtual void Stop()
        {
            _sendTimer?.Dispose();

            IsMonitoringStarted = false;
        }


        protected abstract void OnTimerTick(object _ = null);

        protected virtual string GetComment() => null;

        protected void SendCollectedValue(SensorValueBase value) => ReceiveSensorValue?.Invoke(value);
    }
}