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

        protected readonly TimeSpan _receiveDataPeriod;

        private bool _isStarted;


        protected abstract string SensorName { get; }

        protected virtual TimeSpan TimerDueTime => _receiveDataPeriod;

        internal string SensorPath => $"{_nodePath}/{SensorName}";


        internal event Action<SensorValueBase> ReceiveSensorValue;


        protected MonitoringSensorBase(SensorOptions options)
        {
            _nodePath = options.NodePath;
            _receiveDataPeriod = options.PostDataPeriod;

            _sendTimer = new Timer(OnTimerTick, null, Timeout.Infinite, Timeout.Infinite);
        }


        public void Dispose()
        {
            Stop();
        }

        internal virtual Task<bool> Start()
        {
            if (_isStarted)
                return Task.FromResult(false);

            _sendTimer.Change(TimerDueTime, _receiveDataPeriod);

            _isStarted = true;

            return Task.FromResult(_isStarted);
        }

        internal virtual void Stop()
        {
            _sendTimer?.Dispose();

            _isStarted = false;
        }


        protected abstract void OnTimerTick(object _ = null);

        protected virtual string GetComment() => null;

        protected void SendCollectedValue(SensorValueBase value) => ReceiveSensorValue?.Invoke(value);
    }
}