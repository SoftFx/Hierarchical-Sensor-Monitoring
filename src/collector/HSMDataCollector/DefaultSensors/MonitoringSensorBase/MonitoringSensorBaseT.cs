using System;
using System.Threading;
using HSMDataCollector.Options;
using System.Threading.Tasks;
using HSMDataCollector.Extensions;
using HSMDataCollector.SensorsFactory;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;

namespace HSMDataCollector.DefaultSensors
{
    public abstract class MonitoringSensorBase<T> : SensorBase<T>
    {
        private readonly Timer _sendTimer;
        
        protected readonly TimeSpan _receiveDataPeriod;
        
        
        private bool _isStarted;
        
        
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
            if (!_isStarted)
                return Task.FromResult(false);

            _sendTimer?.Dispose();

            _isStarted = false;
            
            OnTimerTick();
            
            return Task.CompletedTask;
        }
        
        
        protected abstract T GetValue();
        
        protected virtual string GetComment() => null;
        
        protected virtual SensorStatus GetStatus() => SensorStatus.Ok;
        
        
        protected void OnTimerTick(object _ = null)
        {
            var value = BuildSensorValue();
            
            if (NeedSendValue)
                base.SendValue(value, SensorPath);
        }
        
        protected SensorValueBase BuildSensorValue()
        {
            try
            {
                var value = SensorValuesFactory.BuildValue(GetValue());

                return value.Complete(GetComment(), GetStatus());
            }
            catch (Exception ex)
            {
                var value = SensorValuesFactory.BuildValue(default(T));

                return value.Complete(ex.Message, SensorStatus.Error);
            }
        }
    }
}
