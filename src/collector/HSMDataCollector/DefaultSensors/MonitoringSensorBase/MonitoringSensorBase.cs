using HSMDataCollector.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HSMDataCollector.DefaultSensors
{
    public abstract class MonitoringSensorBase : SensorBase,  IDisposable
    {
        private readonly Timer _sendTimer;

        protected readonly TimeSpan _receiveDataPeriod;

        private bool _isStarted;



        protected virtual TimeSpan TimerDueTime => _receiveDataPeriod;
        
        protected MonitoringSensorBase(MonitoringSensorOptions options) : base(options)
        {
            _receiveDataPeriod = options.PostDataPeriod;

            _sendTimer = new Timer(OnTimerTick, null, Timeout.Infinite, Timeout.Infinite);
        }

        internal override Task<bool> Start()
        {
            if (_isStarted)
                return Task.FromResult(false);

            _sendTimer.Change(TimerDueTime, _receiveDataPeriod);

            _isStarted = true;

            return Task.FromResult(_isStarted);
        }

        internal override void Stop()
        {
            _sendTimer?.Dispose();

            _isStarted = false;
        }


        protected abstract void OnTimerTick(object _ = null);

    }
}