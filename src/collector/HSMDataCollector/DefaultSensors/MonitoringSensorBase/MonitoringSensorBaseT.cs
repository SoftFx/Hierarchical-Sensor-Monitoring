using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HSMDataCollector.DefaultSensors
{
    public abstract class MonitoringSensorBase<T> : SensorBase<T>
    {
        protected readonly TimeSpan _receiveDataPeriod;

        private Timer _sendTimer;


        protected virtual TimeSpan TimerDueTime => _receiveDataPeriod;

        protected bool IsStarted => _sendTimer != null;

        protected bool NeedSendValue { get; set; } = true;


        protected MonitoringSensorBase(MonitoringSensorOptions options) : base(options)
        {
            _receiveDataPeriod = options.PostDataPeriod;

            _sendTimer = new Timer(OnTimerTick, null, Timeout.Infinite, Timeout.Infinite);
        }


        internal override Task<bool> Start()
        {
            if (IsStarted)
                return Task.FromResult(false);

            _sendTimer = new Timer(OnTimerTick, null, TimerDueTime, _receiveDataPeriod);

            return Task.FromResult(IsStarted);
        }

        internal override Task Stop()
        {
            if (!IsStarted)
                return Task.FromResult(false);

            _sendTimer?.Dispose();
            _sendTimer = null;

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
                SendValue(value);
        }

        protected SensorValueBase BuildSensorValue()
        {
            try
            {
                return GetSensorValue(GetValue()).Complete(GetComment(), GetStatus());
            }
            catch (Exception ex)
            {
                return GetSensorValue(default).Complete(ex.Message, SensorStatus.Error);
            }
        }
    }
}
