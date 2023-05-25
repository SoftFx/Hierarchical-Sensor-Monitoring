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
        private Timer _sendTimer;

        protected readonly TimeSpan _receiveDataPeriod;
        protected bool _needSendValue = true;


        protected virtual TimeSpan TimerDueTime => _receiveDataPeriod;

        protected bool IsStarted => _sendTimer != null;


        protected MonitoringSensorBase(MonitoringSensorOptions options) : base(options)
        {
            _receiveDataPeriod = options.PostDataPeriod;
        }


        internal override Task<bool> Start()
        {
            if (!IsStarted)
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

        protected virtual T GetDefaultValue() => default;

        protected virtual SensorStatus GetStatus() => SensorStatus.Ok;


        protected void OnTimerTick(object _ = null)
        {
            var value = BuildSensorValue();

            if (_needSendValue)
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
                ThrowException(ex);

                return GetSensorValue(GetDefaultValue()).Complete(ex.Message, SensorStatus.Error);
            }
        }
    }
}
