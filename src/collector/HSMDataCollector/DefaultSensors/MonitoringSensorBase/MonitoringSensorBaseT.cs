using System;
using System.Threading;
using HSMDataCollector.Options;
using System.Threading.Tasks;
using HSMDataCollector.DefaultSensors.SensorBases;

namespace HSMDataCollector.DefaultSensors
{
    public abstract class MonitoringSensorBase<T> : SensorBase<T>
    {
        private readonly Timer _sendTimer;
        private bool _isStarted;
        
        
        protected readonly TimeSpan _receiveDataPeriod;
        
        protected virtual TimeSpan TimerDueTime => _receiveDataPeriod;
        
        
        protected bool NeedSendValue { get; set; } = true;

        
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

        internal override Task Stop()
        {
            _sendTimer?.Dispose();

            _isStarted = false;
            return Task.CompletedTask;
        }
        
        protected void OnTimerTick(object _ = null)
        {
            if (NeedSendValue)
                SendValue(GetValue());
        }
    }
}
