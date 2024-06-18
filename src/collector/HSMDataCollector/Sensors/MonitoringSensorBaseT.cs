using System;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.DefaultSensors
{
    public abstract class MonitoringSensorBase<T> : SensorBase<T>
    {
        private readonly IMonitoringOptions _options;
        private Timer _sendTimer;

        protected bool _needSendValue = true;


        protected virtual TimeSpan TimerDueTime => PostTimePeriod;

        protected TimeSpan PostTimePeriod => _options.PostDataPeriod;

        protected bool IsInitialized => _sendTimer != null;


        protected MonitoringSensorBase(SensorOptions options) : base(options)
        {
            if (options is IMonitoringOptions monitoringOptions)
                _options = monitoringOptions;
            else
                throw new ArgumentNullException(nameof(monitoringOptions));
        }


        internal override async ValueTask<bool> InitAsync()
        {
            if (!IsInitialized)
            {
                var baseInit = await base.InitAsync();

                if (baseInit)
                    _sendTimer = new Timer(OnTimerTick, null, TimerDueTime, PostTimePeriod);
            }

            return IsInitialized;
        }

        internal override ValueTask StopAsync()
        {
            _sendTimer?.Dispose();
            _sendTimer = null;

            return default;
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

        protected void RestartTimer(TimeSpan newPostPeriod)
        {
            if (IsInitialized)
            {
                _options.PostDataPeriod = newPostPeriod;
                _sendTimer.Change(TimerDueTime, PostTimePeriod);
            }
        }


        private SensorValueBase BuildSensorValue()
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
