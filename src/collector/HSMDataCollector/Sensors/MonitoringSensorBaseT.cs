using System;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMDataCollector.Threading;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.DefaultSensors
{
    public abstract class MonitoringSensorBase<T> : SensorBase<T>
    {
        private readonly IMonitoringOptions _options;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _sendTask;

        protected bool _needSendValue = true;

        protected virtual TimeSpan TimerDueTime => PostTimePeriod;

        protected TimeSpan PostTimePeriod => _options.PostDataPeriod;

        protected bool IsInitialized => _sendTask != null;


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
                var baseInit = await base.InitAsync().ConfigureAwait(false);

                if (baseInit)
                {
                    StartSendTask();
                }
            }

            return IsInitialized;
        }

        internal override ValueTask StopAsync()
        {
            _cancellationTokenSource?.Cancel();
            _sendTask?.ConfigureAwait(false).GetAwaiter().GetResult();
            _cancellationTokenSource?.Dispose();
            _sendTask?.Dispose();
            return base.StopAsync();
        }

        protected abstract T GetValue();

        protected virtual string GetComment() => null;

        protected virtual T GetDefaultValue() => default;

        protected virtual SensorStatus GetStatus() => SensorStatus.Ok;

        protected void OnTimerTick()
        {
            if (_needSendValue)
                SendValue(BuildSensorValue());
        }

        protected void RestartTimer(TimeSpan newPostPeriod)
        {
            if (IsInitialized)
            {
                _options.PostDataPeriod = newPostPeriod;
                _cancellationTokenSource?.Cancel();
                _sendTask?.ConfigureAwait(false).GetAwaiter().GetResult();
                _sendTask?.Dispose();
                _cancellationTokenSource?.Dispose();
                StartSendTask();
            }
        }

        private void StartSendTask()
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _sendTask = PeriodicTask.Run(OnTimerTick, TimerDueTime, PostTimePeriod, _cancellationTokenSource.Token);
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
